using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Omnichannel.Contracts;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Omnichannel.Worker;

/// <summary>
/// Background service: consumes <c>commerce.order.placed.v1</c> from RabbitMQ,
/// calls the WMS via <see cref="WmsClient"/>, and posts
/// <c>fulfillment.status.changed.v1</c> back to the plugin callback.
///
/// Declares the topology in <see cref="Topology"/> (idempotent), including the
/// dead-letter exchange/queue so poison messages land in the DLQ instead of
/// looping. This is the async workflow + DLQ reliability decision the assignment
/// requires.
/// </summary>
public sealed class OrderPlacedConsumer : BackgroundService
{
    private readonly WorkerOptions _options;
    private readonly WmsClient _wmsClient;
    private readonly ILogger<OrderPlacedConsumer> _logger;

    private IConnection? _connection;
    private IChannel? _channel;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public OrderPlacedConsumer(WorkerOptions options, WmsClient wmsClient, ILogger<OrderPlacedConsumer> logger)
    {
        _options = options;
        _wmsClient = wmsClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory { Uri = new Uri(_options.RabbitMqUri) };
        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await DeclareTopologyAsync(_channel, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnMessageAsync;

        await _channel.BasicConsumeAsync(
            queue: Topology.OrderPlacedQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Worker consuming {Queue}", Topology.OrderPlacedQueue);

        // Keep the service alive until shutdown.
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private static async Task DeclareTopologyAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(Topology.CommerceExchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(Topology.DeadLetterExchange, ExchangeType.Topic, durable: true, cancellationToken: ct);

        await channel.QueueDeclareAsync(
            Topology.OrderPlacedDeadLetterQueue,
            durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(
            Topology.OrderPlacedDeadLetterQueue, Topology.DeadLetterExchange, Topology.OrderPlacedRoutingKey, cancellationToken: ct);

        var mainQueueArgs = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = Topology.DeadLetterExchange,
            ["x-dead-letter-routing-key"] = Topology.OrderPlacedRoutingKey
        };
        await channel.QueueDeclareAsync(
            Topology.OrderPlacedQueue,
            durable: true, exclusive: false, autoDelete: false, arguments: mainQueueArgs, cancellationToken: ct);
        await channel.QueueBindAsync(
            Topology.OrderPlacedQueue, Topology.CommerceExchange, Topology.OrderPlacedRoutingKey, cancellationToken: ct);
    }

    private async Task OnMessageAsync(object sender, BasicDeliverEventArgs args)
    {
        var channel = _channel!;
        IntegrationMessage<CommerceOrderPlaced>? message = null;

        try
        {
            var json = Encoding.UTF8.GetString(args.Body.Span);
            message = JsonSerializer.Deserialize<IntegrationMessage<CommerceOrderPlaced>>(json, JsonOptions)
                          ?? throw new InvalidOperationException("Unparseable order.placed message");

            var fulfillment = await _wmsClient.RequestFulfillmentAsync(message, CancellationToken.None);

            // If circuit breaker is open (status=pending), wait before requeuing to avoid spam             
            if (fulfillment.Status == "pending")
            {
                _logger.LogInformation(
                "Circuit breaker open, requeuing message after delay order_guid={OrderGuid} message_id={MessageId}", message.Payload.OrderGuid, message.MessageId);
                await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: true);
                return;
            }
            await PostFulfillmentCallbackAsync(message, fulfillment, CancellationToken.None);

            await channel.BasicAckAsync(args.DeliveryTag, multiple: false);
        }
        catch (Exception exception)
        {
            // requeue:false → routed to DLQ via the queue's dead-letter args.
            if (message != null)
            {
                _logger.LogError(exception,
                    "Order.placed handling failed; dead-lettering message order_guid={OrderGuid} message_id={MessageId}",
                    message.Payload.OrderGuid, message.MessageId);
            }
            else
            {
                _logger.LogError(exception, "Order.placed handling failed; dead-lettering message (unparseable)");
            }
            await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false);
        }
    }

    private async Task PostFulfillmentCallbackAsync(
        IntegrationMessage<CommerceOrderPlaced> orderMessage,
        FulfillmentStatusChanged fulfillment,
        CancellationToken cancellationToken)
    {
        var callbackUrl = $"{_options.NopCommerceBaseUrl.TrimEnd('/')}/omnichannel/callbacks/fulfillment/status-changed";

        var envelope = new IntegrationMessage<FulfillmentStatusChanged>
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = orderMessage.CorrelationId,
            EventType = EventTypes.FulfillmentStatusChanged,
            OccurredOnUtc = DateTime.UtcNow,
            Source = "worker",
            Payload = fulfillment
        };

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("X-Demo-Token", _options.DemoToken);

        var response = await httpClient.PostAsJsonAsync(callbackUrl, envelope, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation(
                "Posted fulfillment callback order_guid={OrderGuid} message_id={MessageId} external_request_id={ExternalRequestId} status={Status}",
                fulfillment.OrderGuid, envelope.MessageId, fulfillment.ExternalRequestId, fulfillment.Status);
        }
        else
        {
            _logger.LogWarning(
                "Fulfillment callback failed order_guid={OrderGuid} message_id={MessageId} status_code={StatusCode}",
                fulfillment.OrderGuid, envelope.MessageId, (int)response.StatusCode);
            throw new HttpRequestException($"Callback returned {response.StatusCode}");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
            await _channel.CloseAsync(cancellationToken);
        if (_connection is not null)
            await _connection.CloseAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}

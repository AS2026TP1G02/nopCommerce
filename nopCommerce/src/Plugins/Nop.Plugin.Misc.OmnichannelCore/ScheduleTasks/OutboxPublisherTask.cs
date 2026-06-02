using System.Text;
using Microsoft.Extensions.Configuration;
using Nop.Data;
using Nop.Plugin.Misc.OmnichannelCore.Domains;
using Nop.Services.Logging;
using Nop.Services.ScheduleTasks;
using RabbitMQ.Client;

namespace Nop.Plugin.Misc.OmnichannelCore.ScheduleTasks;

/// <summary>
/// Phase 2 scheduled task (ADR-0003; QA-5). Ships <see cref="OmniOutboxMessageStatus.Pending"/>
/// outbox rows to RabbitMQ (exchange <c>commerce</c>, routing key
/// <c>commerce.order.placed.v1</c>) with publisher confirms, then marks each row
/// <see cref="OmniOutboxMessageStatus.Published"/>. On failure it increments
/// <c>RetryCount</c>/<c>LastError</c> and leaves the row pending for the next run.
///
/// Registered as a nopCommerce ScheduleTask by <c>OmnichannelCorePlugin.InstallAsync</c>.
/// The publish path is decoupled from checkout: nothing here runs on the order thread.
/// </summary>
public class OutboxPublisherTask : IScheduleTask
{
    #region Fields

    private readonly IRepository<OmniOutboxMessage> _outboxMessageRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger _logger;

    private const int BatchSize = 100;

    #endregion

    #region Ctor

    public OutboxPublisherTask(IRepository<OmniOutboxMessage> outboxMessageRepository,
        IConfiguration configuration,
        ILogger logger)
    {
        _outboxMessageRepository = outboxMessageRepository;
        _configuration = configuration;
        _logger = logger;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Executes the task: publishes pending outbox rows with publisher confirms
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public async Task ExecuteAsync()
    {
        var pending = await _outboxMessageRepository.Table
            .Where(message => message.StatusId == (int)OmniOutboxMessageStatus.Pending)
            .OrderBy(message => message.Id)
            .Take(BatchSize)
            .ToListAsync();

        if (!pending.Any())
            return;

        var uri = _configuration[OmnichannelCoreDefaults.RabbitMqUriConfigKey]
                  ?? OmnichannelCoreDefaults.DefaultRabbitMqUri;

        var factory = new ConnectionFactory { Uri = new Uri(uri) };

        // Publisher confirms ON: BasicPublishAsync awaits the broker ack and throws on nack/timeout.
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(channelOptions);

        await channel.ExchangeDeclareAsync(
            OmnichannelCoreDefaults.CommerceExchange, ExchangeType.Topic, durable: true);

        foreach (var message in pending)
        {
            try
            {
                var properties = new BasicProperties
                {
                    Persistent = true,
                    MessageId = message.MessageId.ToString("D"),
                    CorrelationId = message.CorrelationId,
                    Type = message.EventType,
                    ContentType = "application/json"
                };

                var body = Encoding.UTF8.GetBytes(message.Payload ?? string.Empty);

                await channel.BasicPublishAsync(
                    exchange: OmnichannelCoreDefaults.CommerceExchange,
                    routingKey: OmnichannelCoreDefaults.OrderPlacedRoutingKey,
                    mandatory: false,
                    basicProperties: properties,
                    body: body);

                message.Status = OmniOutboxMessageStatus.Published;
                message.PublishedOnUtc = DateTime.UtcNow;
                message.UpdatedOnUtc = message.PublishedOnUtc;
                await _outboxMessageRepository.UpdateAsync(message);

                // ADR-0008 / QA-3: trace the publish point with the 3 correlation IDs.
                await _logger.InformationAsync(
                    $"OmnichannelCore outbox published order_guid={message.OrderGuid} message_id={message.MessageId} event_type={message.EventType}");
            }
            catch (Exception exception)
            {
                message.RetryCount += 1;
                message.LastError = exception.Message;
                message.UpdatedOnUtc = DateTime.UtcNow;
                await _outboxMessageRepository.UpdateAsync(message);

                await _logger.ErrorAsync(
                    $"OmnichannelCore outbox publish failed order_guid={message.OrderGuid} message_id={message.MessageId}", exception);
            }
        }
    }

    #endregion
}

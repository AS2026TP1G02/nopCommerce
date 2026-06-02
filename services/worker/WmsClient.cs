using System.Net;
using System.Net.Http.Json;
using Omnichannel.Contracts;
using Polly;
using Omnichannel.Worker.Resilience;

namespace Omnichannel.Worker;

/// <summary>
/// Calls the WMS simulator's POST /fulfillments and maps the response to a
/// <see cref="FulfillmentStatusChanged"/> payload. The WMS call is wrapped in
/// the retry + circuit-breaker pipeline so the worker degrades gracefully when
/// the WMS is slow/unavailable (QA-1). When the circuit breaker opens, returns
/// status "pending" instead of throwing, allowing the order to be acknowledged
/// and the callback to proceed with degraded state.
/// </summary>
public sealed class WmsClient
{
    private readonly HttpClient _httpClient;
    private readonly WorkerOptions _options;
    private readonly ResiliencePipeline _pipeline;
    private readonly ILogger<WmsClient> _logger;

    public WmsClient(HttpClient httpClient, WorkerOptions options, ILogger<WmsClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_options.WmsBaseUrl.TrimEnd('/'));
        _pipeline = WmsResiliencePipeline.Build(_options, logger);
    }

    public async Task<FulfillmentStatusChanged> RequestFulfillmentAsync(
        IntegrationMessage<CommerceOrderPlaced> message,
        CancellationToken cancellationToken)
    {
        var order = message.Payload;

        // The WMS expects the order-placed contract (see wms-sim/app/schemas.py).
        var wmsRequest = new
        {
            messageId = message.MessageId,
            correlationId = message.CorrelationId,
            eventType = message.EventType,
            occurredOnUtc = message.OccurredOnUtc,
            orderGuid = order.OrderGuid,
            orderId = order.OrderId,
            storeId = order.StoreId,
            items = order.Lines.Select(l => new
            {
                orderItemId = 0,
                productId = l.ProductId,
                sku = l.Sku,
                quantity = l.Quantity,
                warehouseId = 1
            })
        };

        try
        {
            var response = await _pipeline.ExecuteAsync(async ct =>
            {
                var http = await _httpClient.PostAsJsonAsync(_options.WmsFulfillmentPath, wmsRequest, ct);

                // 409 Conflict = business rejection (oversell / stock contradiction). It is NOT a
                // transient fault: returning here instead of throwing keeps the retry strategy and
                // circuit breaker — whose ShouldHandle only match exceptions — from acting on it, so a
                // per-order stock contradiction never trips the breaker or burns retry attempts.
                if (http.StatusCode == HttpStatusCode.Conflict)
                {
                    var error = await http.Content.ReadFromJsonAsync<WmsErrorResponse>(ct);
                    return new WmsFulfillmentResponse
                    {
                        Status = "rejected",
                        ExternalRequestId = string.Empty,
                        Reason = error?.Detail?.Error ?? "wms_rejected",
                        OrderGuid = order.OrderGuid
                    };
                }

                http.EnsureSuccessStatusCode();
                return await http.Content.ReadFromJsonAsync<WmsFulfillmentResponse>(ct)
                       ?? throw new InvalidOperationException("Empty WMS response");
            }, cancellationToken);

            if (response.Status == "rejected")
                _logger.LogWarning(
                    "WMS rejected fulfillment order_guid={OrderGuid} message_id={MessageId} reason={Reason}",
                    order.OrderGuid, message.MessageId, response.Reason);
            else
                _logger.LogInformation(
                    "WMS accepted order_guid={OrderGuid} message_id={MessageId} external_request_id={ExternalRequestId}",
                    order.OrderGuid, message.MessageId, response.ExternalRequestId);

            return new FulfillmentStatusChanged
            {
                OrderId = order.OrderId,
                OrderGuid = order.OrderGuid,
                ExternalRequestId = response.ExternalRequestId,
                Status = response.Status,
                Reason = response.Reason
            };
        }
        catch (Exception exception) when (IsTransientWmsFailure(exception))
        {
            _logger.LogWarning(
                exception,
                "WMS unavailable, marking fulfillment pending order_guid={OrderGuid} message_id={MessageId}",
                order.OrderGuid, message.MessageId);

            return new FulfillmentStatusChanged
            {
                OrderId = order.OrderId,
                OrderGuid = order.OrderGuid,
                ExternalRequestId = string.Empty,
                Status = "pending",
                Reason = "WMS unavailable or circuit breaker open"
            };
        }
    }

    private static bool IsTransientWmsFailure(Exception exception)
    {
        return exception is HttpRequestException
            or TaskCanceledException
            or Polly.CircuitBreaker.BrokenCircuitException;
    }

    private sealed record WmsFulfillmentResponse
    {
        public string ExternalRequestId { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string? Reason { get; init; }
        public Guid OrderGuid { get; init; }
        public Guid MessageId { get; init; }
    }

    // Matches the FastAPI 409 body, which nests the error under "detail":
    // { "detail": { "error": "inventory_contradiction", "mode": "contradictory", ... } }
    private sealed record WmsErrorResponse
    {
        public WmsErrorDetail? Detail { get; init; }
    }

    private sealed record WmsErrorDetail
    {
        public string Error { get; init; } = string.Empty;
    }
}

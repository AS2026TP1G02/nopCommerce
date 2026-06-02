using System.Text.Json;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.OmnichannelCore.Domains;
using Nop.Services.Catalog;
using Nop.Services.Orders;

namespace Nop.Plugin.Misc.OmnichannelCore.Services;

/// <summary>
/// Builds <see cref="OmniOutboxMessage"/> rows for the <c>commerce.order.placed.v1</c>
/// event. Single source of truth for the wire payload so the
/// <see cref="OrderPlacedOutboxConsumer"/> (fast path) and the
/// <c>OutboxReconcilerTask</c> (ADR-0011 crash safety net) cannot drift on shape.
///
/// Emits the shared envelope contract the worker consumes
/// (services/contracts <c>IntegrationMessage&lt;CommerceOrderPlaced&gt;</c>): envelope
/// fields plus a nested <c>payload</c>. Mirrors docs/evidence/sample-commerce-order-placed-v1.json.
/// </summary>
public class OutboxMessageFactory
{
    #region Fields

    private readonly IOrderService _orderService;
    private readonly IProductService _productService;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    #endregion

    #region Ctor

    public OutboxMessageFactory(IOrderService orderService, IProductService productService)
    {
        _orderService = orderService;
        _productService = productService;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Builds a pending outbox message for the given order
    /// </summary>
    /// <param name="order">Placed order</param>
    /// <returns>A task that resolves to a pending <see cref="OmniOutboxMessage"/></returns>
    public virtual async Task<OmniOutboxMessage> BuildOrderPlacedMessageAsync(Order order)
    {
        var now = DateTime.UtcNow;
        var messageId = Guid.NewGuid();
        var correlationId = order.OrderGuid.ToString("D");

        var lines = new List<object>();
        foreach (var orderItem in await _orderService.GetOrderItemsAsync(order.Id))
        {
            var product = await _productService.GetProductByIdAsync(orderItem.ProductId);
            lines.Add(new
            {
                productId = orderItem.ProductId,
                sku = product?.Sku ?? string.Empty,
                quantity = orderItem.Quantity,
                unitPrice = orderItem.UnitPriceInclTax
            });
        }

        // Envelope + nested payload = services/contracts IntegrationMessage<CommerceOrderPlaced>.
        var payload = JsonSerializer.Serialize(new
        {
            messageId,
            correlationId,
            eventType = OmnichannelCoreDefaults.OrderPlacedEventType,
            occurredOnUtc = now,
            source = OmnichannelCoreDefaults.SourceName,
            payload = new
            {
                orderId = order.Id,
                orderGuid = order.OrderGuid,
                storeId = order.StoreId,
                customerId = order.CustomerId,
                currency = string.IsNullOrEmpty(order.CustomerCurrencyCode) ? "EUR" : order.CustomerCurrencyCode,
                totalAmount = order.OrderTotal,
                lines
            }
        }, _jsonOptions);

        return new OmniOutboxMessage
        {
            MessageId = messageId,
            EventType = OmnichannelCoreDefaults.OrderPlacedEventType,
            CorrelationId = correlationId,
            OrderGuid = order.OrderGuid,
            OrderId = order.Id,
            Payload = payload,
            Status = OmniOutboxMessageStatus.Pending,
            RetryCount = 0,
            CreatedOnUtc = now
        };
    }

    #endregion
}

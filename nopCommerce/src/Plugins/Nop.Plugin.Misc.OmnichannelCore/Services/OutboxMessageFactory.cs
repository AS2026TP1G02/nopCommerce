using System.Text.Json;
using Nop.Core.Domain.Orders;
using Nop.Plugin.Misc.OmnichannelCore.Domains;
using Nop.Services.Catalog;
using Nop.Services.Orders;

namespace Nop.Plugin.Misc.OmnichannelCore.Services;

/// <summary>
/// Builds <see cref="OmniOutboxMessage"/> rows for the <c>commerce.order.placed.v1</c>
/// event. Single source of truth for the FLAT wire payload so the
/// <see cref="OrderPlacedOutboxConsumer"/> (fast path) and the
/// <c>OutboxReconcilerTask</c> (ADR-0011 crash safety net) cannot drift on shape.
///
/// Payload mirrors docs/evidence/sample-commerce-order-placed-v1.json and
/// services/contracts Events.cs (CommerceOrderPlacedMessage).
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

        var items = new List<object>();
        foreach (var orderItem in await _orderService.GetOrderItemsAsync(order.Id))
        {
            var product = await _productService.GetProductByIdAsync(orderItem.ProductId);
            items.Add(new
            {
                orderItemId = orderItem.Id,
                productId = orderItem.ProductId,
                sku = product?.Sku ?? string.Empty,
                quantity = orderItem.Quantity,
                warehouseId = product?.WarehouseId ?? 0
            });
        }

        var payload = JsonSerializer.Serialize(new
        {
            messageId,
            correlationId,
            eventType = OmnichannelCoreDefaults.OrderPlacedEventType,
            occurredOnUtc = now,
            source = OmnichannelCoreDefaults.SourceName,
            orderGuid = order.OrderGuid,
            orderId = order.Id,
            storeId = order.StoreId,
            items
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

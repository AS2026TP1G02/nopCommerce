namespace Omnichannel.Contracts;

/// <summary>
/// Canonical event type strings. Kept here so plugin, worker and simulators agree
/// on exact wire values. Mirrors OmnichannelCoreDefaults in the plugin.
/// </summary>
public static class EventTypes
{
    public const string CommerceOrderPlaced = "commerce.order.placed.v1";
    public const string FulfillmentStatusChanged = "fulfillment.status.changed.v1";
    public const string PosStockChanged = "pos.stock.changed.v1";
}

/// <summary>
/// <c>commerce.order.placed.v1</c> — produced by the plugin outbox, consumed by the
/// worker, forwarded to the WMS. FLAT shape; mirrors
/// docs/evidence/sample-commerce-order-placed-v1.json and the WMS
/// <c>FulfillmentRequest</c> schema.
/// </summary>
public record CommerceOrderPlacedMessage : MessageEnvelope
{
    public Guid OrderGuid { get; init; }
    public int OrderId { get; init; }
    public int StoreId { get; init; }
    public IReadOnlyList<OrderLineItem> Items { get; init; } = Array.Empty<OrderLineItem>();
}

public record OrderLineItem
{
    public int OrderItemId { get; init; }
    public int ProductId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public int WarehouseId { get; init; }
}

/// <summary>
/// <c>fulfillment.status.changed.v1</c> — produced by the worker after the WMS call,
/// posted back to the plugin callback. FLAT shape; mirrors
/// docs/evidence/sample-fulfillment-status-changed-v1.json.
/// </summary>
public record FulfillmentStatusChangedMessage : MessageEnvelope
{
    public Guid OrderGuid { get; init; }
    public string ExternalRequestId { get; init; } = string.Empty;

    /// <summary>Accepted | Rejected | Pending | Degraded</summary>
    public string Status { get; init; } = string.Empty;
    public string? TrackingNumber { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// <c>pos.stock.changed.v1</c> — produced by the POS simulator, posted directly to
/// the plugin callback. FLAT shape; mirrors the plugin's PosStockChangedRequest.
/// </summary>
public record PosStockChangedMessage : MessageEnvelope
{
    public long SourceVersion { get; init; }
    public int ProductId { get; init; }
    public string Sku { get; init; } = string.Empty;
    public int WarehouseId { get; init; }
    public int QuantityOnHand { get; init; }
}

namespace Nop.Plugin.Misc.OmnichannelCore.Models.Callbacks;

/// <summary>
/// Represents a worker-originated fulfillment status changed event
/// (<c>fulfillment.status.changed.v1</c>, FLAT envelope + domain fields).
/// </summary>
public record FulfillmentStatusChangedRequest
{
    /// <summary>
    /// Gets or sets the message identifier
    /// </summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// Gets or sets the correlation identifier
    /// </summary>
    public string CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets the event type
    /// </summary>
    public string EventType { get; set; }

    /// <summary>
    /// Gets or sets the event occurrence date and time
    /// </summary>
    public DateTime OccurredOnUtc { get; set; }

    /// <summary>
    /// Gets or sets the source system
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// Gets or sets the related nopCommerce order GUID
    /// </summary>
    public Guid OrderGuid { get; set; }

    /// <summary>
    /// Gets or sets the external fulfillment request identifier (from the WMS)
    /// </summary>
    public string ExternalRequestId { get; set; }

    /// <summary>
    /// Gets or sets the fulfillment status (Accepted | Rejected | Pending | Degraded | Completed)
    /// </summary>
    public string Status { get; set; }

    /// <summary>
    /// Gets or sets an optional tracking number
    /// </summary>
    public string TrackingNumber { get; set; }

    /// <summary>
    /// Gets or sets an optional reason / diagnostic note
    /// </summary>
    public string Reason { get; set; }
}

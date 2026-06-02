using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.OmnichannelCore.Models;

/// <summary>
/// Represents the order traceability lookup model (QA-3): enter an OrderGuid, see the
/// outbox → inbox → fulfillment chain linked by OrderGuid / messageId / correlationId.
/// </summary>
public record OrderTraceModel : BaseNopModel
{
    public OrderTraceModel()
    {
        OutboxMessages = new List<OutboxRow>();
        InboxMessages = new List<InboxRow>();
    }

    /// <summary>Order GUID entered in the lookup form.</summary>
    public string OrderGuid { get; set; }

    /// <summary>True once a lookup has been performed (controls result rendering).</summary>
    public bool Searched { get; set; }

    /// <summary>True if the entered value was not a valid GUID.</summary>
    public bool InvalidGuid { get; set; }

    public IList<OutboxRow> OutboxMessages { get; set; }
    public IList<InboxRow> InboxMessages { get; set; }

    public bool HasFulfillment { get; set; }
    public string FulfillmentStatus { get; set; }
    public string ExternalRequestId { get; set; }
    public string TrackingNumber { get; set; }
    public string FulfillmentReason { get; set; }

    public record OutboxRow
    {
        public int Id { get; set; }
        public string MessageId { get; set; }
        public string CorrelationId { get; set; }
        public string EventType { get; set; }
        public string Status { get; set; }
        public int RetryCount { get; set; }
        public string LastError { get; set; }
        public DateTime CreatedOnUtc { get; set; }
        public DateTime? PublishedOnUtc { get; set; }
    }

    public record InboxRow
    {
        public int Id { get; set; }
        public string MessageId { get; set; }
        public string CorrelationId { get; set; }
        public string EventType { get; set; }
        public string Status { get; set; }
        public DateTime ReceivedOnUtc { get; set; }
    }
}

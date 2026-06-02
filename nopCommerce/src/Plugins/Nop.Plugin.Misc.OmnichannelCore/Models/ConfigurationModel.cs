using Nop.Web.Framework.Models;

namespace Nop.Plugin.Misc.OmnichannelCore.Models;

/// <summary>
/// Represents plugin configuration page model
/// </summary>
public record ConfigurationModel : BaseNopModel
{
    /// <summary>
    /// Gets or sets the outbox message count
    /// </summary>
    public int OutboxMessages { get; set; }

    /// <summary>
    /// Gets or sets the inbox message count
    /// </summary>
    public int InboxMessages { get; set; }

    /// <summary>
    /// Gets or sets the fulfillment record count
    /// </summary>
    public int FulfillmentRecords { get; set; }

    /// <summary>
    /// Gets or sets the count of orders still awaiting fulfillment or marked Pending/Degraded (QA-4)
    /// </summary>
    public int PendingFulfillmentRecords { get; set; }

    /// <summary>
    /// Gets or sets the stock projection record count
    /// </summary>
    public int StockProjectionRecords { get; set; }
}

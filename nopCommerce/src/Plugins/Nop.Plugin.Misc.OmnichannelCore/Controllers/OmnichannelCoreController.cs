using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Misc.OmnichannelCore.Models;
using Nop.Plugin.Misc.OmnichannelCore.Services;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Misc.OmnichannelCore.Controllers;

[AutoValidateAntiforgeryToken]
[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
public class OmnichannelCoreController : BasePluginController
{
    #region Fields

    private readonly OmnichannelCoreService _omnichannelCoreService;

    #endregion

    #region Ctor

    public OmnichannelCoreController(OmnichannelCoreService omnichannelCoreService)
    {
        _omnichannelCoreService = omnichannelCoreService;
    }

    #endregion

    #region Methods

    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public virtual async Task<IActionResult> Configure()
    {
        var model = new ConfigurationModel
        {
            OutboxMessages = await _omnichannelCoreService.GetOutboxMessageCountAsync(),
            InboxMessages = await _omnichannelCoreService.GetInboxMessageCountAsync(),
            FulfillmentRecords = await _omnichannelCoreService.GetFulfillmentRecordCountAsync(),
            StockProjectionRecords = await _omnichannelCoreService.GetStockProjectionRecordCountAsync()
        };

        return View("~/Plugins/Misc.OmnichannelCore/Views/Configure.cshtml", model);
    }

    /// <summary>
    /// Traceability lookup (QA-3): enter an OrderGuid → return the outbox/inbox/fulfillment
    /// chain. Lookup only — no search, filters, or editing.
    /// </summary>
    [CheckPermission(StandardPermission.Configuration.MANAGE_PLUGINS)]
    public virtual async Task<IActionResult> Trace(string orderGuid)
    {
        var model = new OrderTraceModel { OrderGuid = orderGuid };

        if (string.IsNullOrWhiteSpace(orderGuid))
            return View("~/Plugins/Misc.OmnichannelCore/Views/Trace.cshtml", model);

        model.Searched = true;

        if (!Guid.TryParse(orderGuid, out var parsedGuid))
        {
            model.InvalidGuid = true;
            return View("~/Plugins/Misc.OmnichannelCore/Views/Trace.cshtml", model);
        }

        foreach (var outbox in await _omnichannelCoreService.GetOutboxMessagesByOrderGuidAsync(parsedGuid))
        {
            model.OutboxMessages.Add(new OrderTraceModel.OutboxRow
            {
                Id = outbox.Id,
                MessageId = outbox.MessageId.ToString("D"),
                CorrelationId = outbox.CorrelationId,
                EventType = outbox.EventType,
                Status = outbox.Status.ToString(),
                RetryCount = outbox.RetryCount,
                LastError = outbox.LastError,
                CreatedOnUtc = outbox.CreatedOnUtc,
                PublishedOnUtc = outbox.PublishedOnUtc
            });
        }

        foreach (var inbox in await _omnichannelCoreService.GetInboxMessagesByOrderGuidAsync(parsedGuid))
        {
            model.InboxMessages.Add(new OrderTraceModel.InboxRow
            {
                Id = inbox.Id,
                MessageId = inbox.MessageId.ToString("D"),
                CorrelationId = inbox.CorrelationId,
                EventType = inbox.EventType,
                Status = inbox.Status.ToString(),
                ReceivedOnUtc = inbox.ReceivedOnUtc
            });
        }

        var fulfillment = await _omnichannelCoreService.GetFulfillmentByOrderGuidAsync(parsedGuid);
        if (fulfillment != null)
        {
            model.HasFulfillment = true;
            model.FulfillmentStatus = fulfillment.Status.ToString();
            model.ExternalRequestId = fulfillment.ExternalRequestId;
            model.TrackingNumber = fulfillment.TrackingNumber;
            model.FulfillmentReason = fulfillment.Reason;
        }

        return View("~/Plugins/Misc.OmnichannelCore/Views/Trace.cshtml", model);
    }

    #endregion
}

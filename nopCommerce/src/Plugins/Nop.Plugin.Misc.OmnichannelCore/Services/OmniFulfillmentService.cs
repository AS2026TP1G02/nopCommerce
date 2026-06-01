using Nop.Data;
using Nop.Plugin.Misc.OmnichannelCore.Domains;
using Nop.Plugin.Misc.OmnichannelCore.Models.Callbacks;
using Nop.Services.Logging;

namespace Nop.Plugin.Misc.OmnichannelCore.Services;

/// <summary>
/// Applies worker-originated <c>fulfillment.status.changed.v1</c> events to the
/// <see cref="OmniOrderFulfillment"/> projection (ADR-0007: the plugin owns this
/// projection; nopCommerce core order state is not mutated here).
///
/// Upserts one fulfillment row per <c>OrderGuid</c> so a redelivered status update
/// (different messageId, same order) advances the same row rather than duplicating it.
/// </summary>
public class OmniFulfillmentService
{
    #region Fields

    private readonly IRepository<OmniOrderFulfillment> _orderFulfillmentRepository;
    private readonly ILogger _logger;

    #endregion

    #region Ctor

    public OmniFulfillmentService(IRepository<OmniOrderFulfillment> orderFulfillmentRepository, ILogger logger)
    {
        _orderFulfillmentRepository = orderFulfillmentRepository;
        _logger = logger;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Applies a fulfillment status changed event to the projection
    /// </summary>
    /// <param name="request">Fulfillment status changed event</param>
    /// <returns>A task that resolves to the updated fulfillment row</returns>
    public virtual async Task<OmniOrderFulfillment> ApplyFulfillmentStatusChangedAsync(FulfillmentStatusChangedRequest request)
    {
        var status = MapStatus(request.Status);
        var now = DateTime.UtcNow;

        var fulfillment = await _orderFulfillmentRepository.Table
            .FirstOrDefaultAsync(record => record.OrderGuid == request.OrderGuid);

        if (fulfillment == null)
        {
            fulfillment = new OmniOrderFulfillment
            {
                OrderGuid = request.OrderGuid,
                MessageId = request.MessageId,
                CorrelationId = request.CorrelationId,
                ExternalRequestId = request.ExternalRequestId,
                TrackingNumber = request.TrackingNumber,
                Reason = request.Reason,
                Status = status,
                CreatedOnUtc = now,
                UpdatedOnUtc = now,
                AcceptedOnUtc = status == OmniFulfillmentStatus.Accepted ? now : null,
                CompletedOnUtc = status == OmniFulfillmentStatus.Completed ? now : null
            };

            await _orderFulfillmentRepository.InsertAsync(fulfillment);

            // ADR-0008: 3-ID structured trace on every integration-path write.
            await _logger.InformationAsync(
                $"OmnichannelCore fulfillment created order_guid={request.OrderGuid} message_id={request.MessageId} external_request_id={request.ExternalRequestId} status={status}");

            return fulfillment;
        }

        fulfillment.MessageId = request.MessageId;
        fulfillment.CorrelationId = request.CorrelationId;
        fulfillment.ExternalRequestId = request.ExternalRequestId;
        fulfillment.TrackingNumber = request.TrackingNumber;
        fulfillment.Reason = request.Reason;
        fulfillment.Status = status;
        fulfillment.UpdatedOnUtc = now;

        if (status == OmniFulfillmentStatus.Accepted && fulfillment.AcceptedOnUtc == null)
            fulfillment.AcceptedOnUtc = now;
        if (status == OmniFulfillmentStatus.Completed && fulfillment.CompletedOnUtc == null)
            fulfillment.CompletedOnUtc = now;

        await _orderFulfillmentRepository.UpdateAsync(fulfillment);

        // ADR-0008: 3-ID structured trace on every integration-path write.
        await _logger.InformationAsync(
            $"OmnichannelCore fulfillment updated order_guid={request.OrderGuid} message_id={request.MessageId} external_request_id={request.ExternalRequestId} status={status}");

        return fulfillment;
    }

    #endregion

    #region Utilities

    private static OmniFulfillmentStatus MapStatus(string status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "accepted" => OmniFulfillmentStatus.Accepted,
            "completed" => OmniFulfillmentStatus.Completed,
            "rejected" => OmniFulfillmentStatus.Rejected,
            "degraded" => OmniFulfillmentStatus.Degraded,
            _ => OmniFulfillmentStatus.Pending
        };
    }

    #endregion
}

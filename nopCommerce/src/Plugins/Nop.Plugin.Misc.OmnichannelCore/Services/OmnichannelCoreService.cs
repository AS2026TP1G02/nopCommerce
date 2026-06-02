using Nop.Data;
using Nop.Plugin.Misc.OmnichannelCore;
using Nop.Plugin.Misc.OmnichannelCore.Domains;

namespace Nop.Plugin.Misc.OmnichannelCore.Services;

/// <summary>
/// Represents omnichannel core read service
/// </summary>
public class OmnichannelCoreService
{
    #region Fields

    private readonly IRepository<OmniInboxMessage> _inboxMessageRepository;
    private readonly IRepository<OmniOrderFulfillment> _orderFulfillmentRepository;
    private readonly IRepository<OmniOutboxMessage> _outboxMessageRepository;
    private readonly IRepository<OmniStockSyncState> _stockSyncStateRepository;

    #endregion

    #region Ctor

    public OmnichannelCoreService(IRepository<OmniInboxMessage> inboxMessageRepository,
        IRepository<OmniOrderFulfillment> orderFulfillmentRepository,
        IRepository<OmniOutboxMessage> outboxMessageRepository,
        IRepository<OmniStockSyncState> stockSyncStateRepository)
    {
        _inboxMessageRepository = inboxMessageRepository;
        _orderFulfillmentRepository = orderFulfillmentRepository;
        _outboxMessageRepository = outboxMessageRepository;
        _stockSyncStateRepository = stockSyncStateRepository;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets the outbox message count
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public virtual async Task<int> GetOutboxMessageCountAsync()
    {
        return await _outboxMessageRepository.Table.CountAsync();
    }

    /// <summary>
    /// Gets the inbox message count
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public virtual async Task<int> GetInboxMessageCountAsync()
    {
        return await _inboxMessageRepository.Table.CountAsync();
    }

    /// <summary>
    /// Gets the fulfillment record count
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public virtual async Task<int> GetFulfillmentRecordCountAsync()
    {
        return await _orderFulfillmentRepository.Table.CountAsync();
    }

    /// <summary>
    /// Gets the stock projection record count
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public virtual async Task<int> GetStockProjectionRecordCountAsync()
    {
        return await _stockSyncStateRepository.Table.CountAsync();
    }

    /// <summary>
    /// Gets the count of orders still awaiting fulfillment or already marked degraded (QA-4 operability:
    /// the "fulfillment-pending count" surfaced in the admin alongside the RabbitMQ dashboard)
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public virtual async Task<int> GetPendingFulfillmentCountAsync()
    {
        var pendingOrDegradedRows = await _orderFulfillmentRepository.Table
            .Where(fulfillment => fulfillment.StatusId == (int)OmniFulfillmentStatus.Pending
                || fulfillment.StatusId == (int)OmniFulfillmentStatus.Degraded)
            .CountAsync();

        var awaitingProjectionRows = await _outboxMessageRepository.Table
            .Where(message => message.EventType == OmnichannelCoreDefaults.OrderPlacedEventType
                && message.OrderGuid.HasValue
                && message.StatusId != (int)OmniOutboxMessageStatus.DeadLettered
                && !_orderFulfillmentRepository.Table.Any(fulfillment => fulfillment.OrderGuid == message.OrderGuid.Value))
            .Select(message => message.OrderGuid)
            .Distinct()
            .CountAsync();

        return pendingOrDegradedRows + awaitingProjectionRows;
    }

    /// <summary>
    /// Gets the outbox messages written for an order (QA-3 trace lookup)
    /// </summary>
    /// <param name="orderGuid">Order GUID</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public virtual async Task<IList<OmniOutboxMessage>> GetOutboxMessagesByOrderGuidAsync(Guid orderGuid)
    {
        return await _outboxMessageRepository.Table
            .Where(message => message.OrderGuid == orderGuid)
            .OrderBy(message => message.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Gets the inbox messages received for an order (QA-3 trace lookup)
    /// </summary>
    /// <param name="orderGuid">Order GUID</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public virtual async Task<IList<OmniInboxMessage>> GetInboxMessagesByOrderGuidAsync(Guid orderGuid)
    {
        return await _inboxMessageRepository.Table
            .Where(message => message.OrderGuid == orderGuid)
            .OrderBy(message => message.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Gets the fulfillment projection row for an order, if any (QA-3 trace lookup)
    /// </summary>
    /// <param name="orderGuid">Order GUID</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public virtual async Task<OmniOrderFulfillment> GetFulfillmentByOrderGuidAsync(Guid orderGuid)
    {
        return await _orderFulfillmentRepository.Table
            .FirstOrDefaultAsync(fulfillment => fulfillment.OrderGuid == orderGuid);
    }

    #endregion
}

using Nop.Core.Domain.Orders;
using Nop.Core.Events;
using Nop.Data;
using Nop.Plugin.Misc.OmnichannelCore.Domains;
using Nop.Services.Logging;

namespace Nop.Plugin.Misc.OmnichannelCore.Services;

/// <summary>
/// Phase 2 consumer (ADR-0003, ADR-0011; QA-5). When nopCommerce raises
/// <see cref="OrderPlacedEvent"/>, write a durable <see cref="OmniOutboxMessage"/> row in
/// the SAME request instead of calling the warehouse synchronously on the checkout thread.
/// The scheduled <c>OutboxPublisherTask</c> later ships the row to RabbitMQ; the
/// <c>OutboxReconcilerTask</c> is the crash safety net per ADR-0011.
///
/// nopCommerce auto-discovers <see cref="IConsumer{T}"/> implementations, so no manual DI
/// registration is required. The payload shape is produced by the shared
/// <see cref="OutboxMessageFactory"/> so it cannot drift from the reconciler.
/// </summary>
public class OrderPlacedOutboxConsumer : IConsumer<OrderPlacedEvent>
{
    #region Fields

    private readonly IRepository<OmniOutboxMessage> _outboxMessageRepository;
    private readonly OutboxMessageFactory _outboxMessageFactory;
    private readonly ILogger _logger;

    #endregion

    #region Ctor

    public OrderPlacedOutboxConsumer(IRepository<OmniOutboxMessage> outboxMessageRepository,
        OutboxMessageFactory outboxMessageFactory,
        ILogger logger)
    {
        _outboxMessageRepository = outboxMessageRepository;
        _outboxMessageFactory = outboxMessageFactory;
        _logger = logger;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Handles the order placed event by recording an outbox message
    /// </summary>
    /// <param name="eventMessage">Order placed event</param>
    /// <returns>A task that represents the asynchronous operation</returns>
    public async Task HandleEventAsync(OrderPlacedEvent eventMessage)
    {
        var message = await _outboxMessageFactory.BuildOrderPlacedMessageAsync(eventMessage.Order);
        await _outboxMessageRepository.InsertAsync(message);

        await _logger.InformationAsync(
            $"OmnichannelCore outbox queued order_guid={message.OrderGuid} message_id={message.MessageId} order_id={message.OrderId}");
    }

    #endregion
}

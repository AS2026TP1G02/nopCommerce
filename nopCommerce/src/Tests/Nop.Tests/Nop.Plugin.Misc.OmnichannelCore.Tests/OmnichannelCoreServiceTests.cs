using AwesomeAssertions;
using Nop.Plugin.Misc.OmnichannelCore;
using Nop.Plugin.Misc.OmnichannelCore.Domains;
using Nop.Plugin.Misc.OmnichannelCore.Services;
using NUnit.Framework;

namespace Nop.Tests.Nop.Plugin.Misc.OmnichannelCore.Tests;

[TestFixture]
public class OmnichannelCoreServiceTests
{
    [Test]
    public async Task GetPendingFulfillmentCountAsync_CountsPublishedOutboxMessagesWithoutFulfillmentProjection()
    {
        var service = CreateService(out _, out var fulfillmentRepository, out var outboxRepository, out _);

        for (var i = 0; i < 10; i++)
        {
            outboxRepository.Entities.Add(CreateOrderPlacedOutboxMessage(Guid.NewGuid()));
        }

        var count = await service.GetPendingFulfillmentCountAsync();

        count.Should().Be(10);
        fulfillmentRepository.Entities.Should().BeEmpty();
    }

    [Test]
    public async Task GetPendingFulfillmentCountAsync_DoesNotDoubleCountOutboxWhenPendingProjectionExists()
    {
        var service = CreateService(out _, out var fulfillmentRepository, out var outboxRepository, out _);
        var orderGuid = Guid.NewGuid();

        outboxRepository.Entities.Add(CreateOrderPlacedOutboxMessage(orderGuid));
        fulfillmentRepository.Entities.Add(CreateFulfillment(orderGuid, OmniFulfillmentStatus.Pending));

        var count = await service.GetPendingFulfillmentCountAsync();

        count.Should().Be(1);
    }

    [Test]
    public async Task GetPendingFulfillmentCountAsync_DoesNotCountAcceptedFulfillment()
    {
        var service = CreateService(out _, out var fulfillmentRepository, out var outboxRepository, out _);
        var orderGuid = Guid.NewGuid();

        outboxRepository.Entities.Add(CreateOrderPlacedOutboxMessage(orderGuid));
        fulfillmentRepository.Entities.Add(CreateFulfillment(orderGuid, OmniFulfillmentStatus.Accepted));

        var count = await service.GetPendingFulfillmentCountAsync();

        count.Should().Be(0);
    }

    private static OmnichannelCoreService CreateService(
        out InMemoryRepository<OmniInboxMessage> inboxRepository,
        out InMemoryRepository<OmniOrderFulfillment> fulfillmentRepository,
        out InMemoryRepository<OmniOutboxMessage> outboxRepository,
        out InMemoryRepository<OmniStockSyncState> stockSyncRepository)
    {
        inboxRepository = new InMemoryRepository<OmniInboxMessage>();
        fulfillmentRepository = new InMemoryRepository<OmniOrderFulfillment>();
        outboxRepository = new InMemoryRepository<OmniOutboxMessage>();
        stockSyncRepository = new InMemoryRepository<OmniStockSyncState>();

        return new OmnichannelCoreService(inboxRepository, fulfillmentRepository, outboxRepository, stockSyncRepository);
    }

    private static OmniOutboxMessage CreateOrderPlacedOutboxMessage(Guid orderGuid)
    {
        return new OmniOutboxMessage
        {
            MessageId = Guid.NewGuid(),
            EventType = OmnichannelCoreDefaults.OrderPlacedEventType,
            OrderGuid = orderGuid,
            Status = OmniOutboxMessageStatus.Published,
            Payload = "{}",
            CreatedOnUtc = DateTime.UtcNow
        };
    }

    private static OmniOrderFulfillment CreateFulfillment(Guid orderGuid, OmniFulfillmentStatus status)
    {
        return new OmniOrderFulfillment
        {
            OrderGuid = orderGuid,
            Status = status,
            CreatedOnUtc = DateTime.UtcNow
        };
    }
}

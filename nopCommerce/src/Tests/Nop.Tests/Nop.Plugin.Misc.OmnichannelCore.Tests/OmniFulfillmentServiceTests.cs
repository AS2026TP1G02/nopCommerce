using AwesomeAssertions;
using Moq;
using Nop.Plugin.Misc.OmnichannelCore;
using Nop.Plugin.Misc.OmnichannelCore.Domains;
using Nop.Plugin.Misc.OmnichannelCore.Models.Callbacks;
using Nop.Plugin.Misc.OmnichannelCore.Services;
using Nop.Services.Logging;
using NUnit.Framework;

namespace Nop.Tests.Nop.Plugin.Misc.OmnichannelCore.Tests;

[TestFixture]
public class OmniFulfillmentServiceTests
{
    [Test]
    public async Task ApplyFulfillmentStatusChangedAsync_InsertsAcceptedRow()
    {
        var service = CreateService(out var repository);
        var orderGuid = Guid.NewGuid();

        var result = await service.ApplyFulfillmentStatusChangedAsync(CreateRequest(orderGuid, "accepted"));

        result.Status.Should().Be(OmniFulfillmentStatus.Accepted);
        result.AcceptedOnUtc.Should().NotBeNull();
        result.CompletedOnUtc.Should().BeNull();
        repository.Entities.Should().ContainSingle();
        repository.Entities[0].OrderGuid.Should().Be(orderGuid);
    }

    [Test]
    public async Task ApplyFulfillmentStatusChangedAsync_AdvancesSameOrderRow_NoDuplicate()
    {
        var service = CreateService(out var repository);
        var orderGuid = Guid.NewGuid();

        var accepted = await service.ApplyFulfillmentStatusChangedAsync(CreateRequest(orderGuid, "accepted"));
        var acceptedOnUtc = accepted.AcceptedOnUtc;

        // A later status for the SAME order (different messageId) advances the same row.
        var completed = await service.ApplyFulfillmentStatusChangedAsync(CreateRequest(orderGuid, "completed"));

        repository.Entities.Should().ContainSingle();
        completed.Status.Should().Be(OmniFulfillmentStatus.Completed);
        completed.CompletedOnUtc.Should().NotBeNull();
        // The original AcceptedOnUtc is preserved, not overwritten.
        completed.AcceptedOnUtc.Should().Be(acceptedOnUtc);
    }

    private static OmniFulfillmentService CreateService(out InMemoryRepository<OmniOrderFulfillment> repository)
    {
        repository = new InMemoryRepository<OmniOrderFulfillment>();
        var logger = new Mock<ILogger>();
        logger.SetReturnsDefault<Task>(Task.CompletedTask);
        return new OmniFulfillmentService(repository, logger.Object);
    }

    private static FulfillmentStatusChangedRequest CreateRequest(Guid orderGuid, string status)
    {
        return new FulfillmentStatusChangedRequest
        {
            MessageId = Guid.NewGuid(),
            CorrelationId = orderGuid.ToString("D"),
            EventType = OmnichannelCoreDefaults.FulfillmentStatusChangedEventType,
            OccurredOnUtc = DateTime.UtcNow,
            Source = "worker",
            OrderGuid = orderGuid,
            ExternalRequestId = "wms-req-1",
            Status = status,
            TrackingNumber = "TRACK-1"
        };
    }
}

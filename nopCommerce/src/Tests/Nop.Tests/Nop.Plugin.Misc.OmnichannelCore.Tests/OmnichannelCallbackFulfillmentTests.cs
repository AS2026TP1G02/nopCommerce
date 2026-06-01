using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using Nop.Plugin.Misc.OmnichannelCore;
using Nop.Plugin.Misc.OmnichannelCore.Controllers;
using Nop.Plugin.Misc.OmnichannelCore.Domains;
using Nop.Plugin.Misc.OmnichannelCore.Models.Callbacks;
using Nop.Plugin.Misc.OmnichannelCore.Services;
using Nop.Services.Logging;
using NUnit.Framework;

namespace Nop.Tests.Nop.Plugin.Misc.OmnichannelCore.Tests;

[TestFixture]
public class OmnichannelCallbackFulfillmentTests
{
    [Test]
    public async Task FulfillmentStatusChanged_ReturnsUnauthorized_WhenDemoTokenIsMissing()
    {
        var fixture = CreateFixture(addDemoToken: false);

        var result = await fixture.Controller.FulfillmentStatusChanged(CreateRequest(Guid.NewGuid(), "accepted"));

        result.Should().BeOfType<UnauthorizedObjectResult>();
        fixture.InboxRepository.Entities.Should().BeEmpty();
        fixture.FulfillmentRepository.Entities.Should().BeEmpty();
    }

    [Test]
    public async Task FulfillmentStatusChanged_AppliesAndMarksInboxProcessed()
    {
        var fixture = CreateFixture();

        var response = ReadOkResponse(await fixture.Controller.FulfillmentStatusChanged(CreateRequest(Guid.NewGuid(), "accepted")));

        response.Result.Should().Be("applied");
        response.Applied.Should().BeTrue();
        response.Duplicate.Should().BeFalse();
        fixture.FulfillmentRepository.Entities.Should().ContainSingle();
        fixture.FulfillmentRepository.Entities[0].Status.Should().Be(OmniFulfillmentStatus.Accepted);
        fixture.InboxRepository.Entities.Should().ContainSingle();
        fixture.InboxRepository.Entities[0].Status.Should().Be(OmniInboxMessageStatus.Processed);
    }

    [Test]
    public async Task FulfillmentStatusChanged_IgnoresDuplicateMessageId()
    {
        var fixture = CreateFixture();
        var request = CreateRequest(Guid.NewGuid(), "accepted");

        var first = ReadOkResponse(await fixture.Controller.FulfillmentStatusChanged(request));
        var duplicate = ReadOkResponse(await fixture.Controller.FulfillmentStatusChanged(request));

        first.Result.Should().Be("applied");
        duplicate.Result.Should().Be("duplicate");
        duplicate.Duplicate.Should().BeTrue();

        // The duplicate is rejected via the existing inbox row; no second row, fulfillment untouched.
        fixture.InboxRepository.Entities.Should().ContainSingle();
        fixture.FulfillmentRepository.Entities.Should().ContainSingle();
    }

    private static CallbackFixture CreateFixture(bool addDemoToken = true)
    {
        var inboxRepository = new InMemoryRepository<OmniInboxMessage>();
        var stockRepository = new InMemoryRepository<OmniStockSyncState>();
        var fulfillmentRepository = new InMemoryRepository<OmniOrderFulfillment>();

        var logger = new Mock<ILogger>();
        logger.SetReturnsDefault<Task>(Task.CompletedTask);

        var controller = new OmnichannelCallbackController(
            CreateConfiguration(),
            new OmniFulfillmentService(fulfillmentRepository, logger.Object),
            new OmniInboxService(inboxRepository),
            new OmniStockSyncService(stockRepository))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        if (addDemoToken)
            controller.Request.Headers[OmnichannelCoreDefaults.DemoTokenHeaderName] = OmnichannelCoreDefaults.DefaultDemoToken;

        return new CallbackFixture(controller, inboxRepository, fulfillmentRepository);
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["OmnichannelCore:DemoToken"] = OmnichannelCoreDefaults.DefaultDemoToken
            })
            .Build();
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
            Status = status
        };
    }

    private static OmnichannelCallbackResponse ReadOkResponse(IActionResult actionResult)
    {
        var okResult = actionResult.Should().BeOfType<OkObjectResult>().Subject;
        return okResult.Value.Should().BeOfType<OmnichannelCallbackResponse>().Subject;
    }

    private sealed record CallbackFixture(OmnichannelCallbackController Controller,
        InMemoryRepository<OmniInboxMessage> InboxRepository,
        InMemoryRepository<OmniOrderFulfillment> FulfillmentRepository);
}

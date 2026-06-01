using System.Net.Http.Json;
using Omnichannel.Contracts;

namespace Omnichannel.Worker;

/// <summary>
/// Posts <c>fulfillment.status.changed.v1</c> back to the nopCommerce plugin callback
/// (<c>POST /omnichannel/callbacks/fulfillment/status-changed</c>) with the demo-token
/// header. This closes the async loop: order → outbox → MQ → worker → WMS → here →
/// OmniOrderFulfillment projection.
/// </summary>
public sealed class NopCallbackClient
{
    private const string DemoTokenHeader = "X-Demo-Token";
    private const string FulfillmentPath = "/omnichannel/callbacks/fulfillment/status-changed";

    private readonly HttpClient _httpClient;
    private readonly WorkerOptions _options;
    private readonly ILogger<NopCallbackClient> _logger;

    public NopCallbackClient(HttpClient httpClient, WorkerOptions options, ILogger<NopCallbackClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_options.NopCommerceBaseUrl.TrimEnd('/'));
    }

    public async Task PostFulfillmentStatusAsync(
        FulfillmentStatusChangedMessage message,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, FulfillmentPath)
        {
            Content = JsonContent.Create(message)
        };
        request.Headers.Add(DemoTokenHeader, _options.DemoToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        _logger.LogInformation(
            "Posted fulfillment status order_guid={OrderGuid} message_id={MessageId} status={Status}",
            message.OrderGuid, message.MessageId, message.Status);
    }
}

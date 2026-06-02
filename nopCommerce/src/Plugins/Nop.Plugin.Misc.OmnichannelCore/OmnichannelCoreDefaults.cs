namespace Nop.Plugin.Misc.OmnichannelCore;

/// <summary>
/// Represents omnichannel core plugin defaults
/// </summary>
public static class OmnichannelCoreDefaults
{
    /// <summary>
    /// Gets the plugin system name
    /// </summary>
    public const string SystemName = "Misc.OmnichannelCore";

    /// <summary>
    /// Gets the configuration page route
    /// </summary>
    public const string ConfigurationRoute = "Admin/OmnichannelCore/Configure";

    /// <summary>
    /// Gets the internal callback demo token header name
    /// </summary>
    public const string DemoTokenHeaderName = "X-Demo-Token";

    /// <summary>
    /// Gets the default demo token used by local simulators
    /// </summary>
    public const string DefaultDemoToken = "omni-demo-token";

    /// <summary>
    /// Gets the POS stock changed event type
    /// </summary>
    public const string PosStockChangedEventType = "pos.stock.changed.v1";

    /// <summary>
    /// Gets the order placed integration event type (plugin outbox → worker)
    /// </summary>
    public const string OrderPlacedEventType = "commerce.order.placed.v1";

    /// <summary>
    /// Gets the fulfillment status changed integration event type (worker → plugin callback)
    /// </summary>
    public const string FulfillmentStatusChangedEventType = "fulfillment.status.changed.v1";

    /// <summary>
    /// Gets the source system name stamped on messages this plugin produces
    /// </summary>
    public const string SourceName = "nopcommerce";

    #region Messaging (ADR-0003)

    /// <summary>
    /// Gets the configuration key holding the RabbitMQ connection URI
    /// </summary>
    public const string RabbitMqUriConfigKey = "OmnichannelCore:RabbitMqUri";

    /// <summary>
    /// Gets the default RabbitMQ connection URI (docker-compose service name)
    /// </summary>
    public const string DefaultRabbitMqUri = "amqp://guest:guest@rabbitmq:5672/";

    /// <summary>
    /// Gets the topic exchange order-placed messages are published to
    /// </summary>
    public const string CommerceExchange = "commerce";

    /// <summary>
    /// Gets the routing key for order-placed messages
    /// </summary>
    public const string OrderPlacedRoutingKey = "commerce.order.placed.v1";

    #endregion

    #region Schedule tasks

    /// <summary>
    /// Gets the outbox publisher task type and metadata
    /// </summary>
    public static (string Name, string Type, int Period) OutboxPublisherTask =>
        ("Omnichannel outbox publisher",
         "Nop.Plugin.Misc.OmnichannelCore.ScheduleTasks.OutboxPublisherTask, Nop.Plugin.Misc.OmnichannelCore",
         60);

    /// <summary>
    /// Gets the outbox reconciler task type and metadata
    /// </summary>
    public static (string Name, string Type, int Period) OutboxReconcilerTask =>
        ("Omnichannel outbox reconciler",
         "Nop.Plugin.Misc.OmnichannelCore.ScheduleTasks.OutboxReconcilerTask, Nop.Plugin.Misc.OmnichannelCore",
         300);

    #endregion
}

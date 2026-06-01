using Nop.Core.Domain.ScheduleTasks;
using Nop.Services.Common;
using Nop.Services.Plugins;
using Nop.Services.ScheduleTasks;

namespace Nop.Plugin.Misc.OmnichannelCore;

/// <summary>
/// Represents the omnichannel core plugin
/// </summary>
public class OmnichannelCorePlugin : BasePlugin, IMiscPlugin
{
    #region Fields

    private readonly IScheduleTaskService _scheduleTaskService;
    private readonly IWebHelper _webHelper;

    #endregion

    #region Ctor

    public OmnichannelCorePlugin(IScheduleTaskService scheduleTaskService, IWebHelper webHelper)
    {
        _scheduleTaskService = scheduleTaskService;
        _webHelper = webHelper;
    }

    #endregion

    #region Utilities

    /// <summary>
    /// Inserts a schedule task if one with the same type does not already exist
    /// </summary>
    private async Task EnsureScheduleTaskAsync((string Name, string Type, int Period) task)
    {
        if (await _scheduleTaskService.GetTaskByTypeAsync(task.Type) is not null)
            return;

        await _scheduleTaskService.InsertTaskAsync(new ScheduleTask
        {
            Name = task.Name,
            Type = task.Type,
            Seconds = task.Period,
            Enabled = true,
            StopOnError = false,
            LastEnabledUtc = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Deletes a schedule task by type if it exists
    /// </summary>
    private async Task RemoveScheduleTaskAsync(string type)
    {
        var task = await _scheduleTaskService.GetTaskByTypeAsync(type);
        if (task is not null)
            await _scheduleTaskService.DeleteTaskAsync(task);
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets a configuration page URL
    /// </summary>
    public override string GetConfigurationPageUrl()
    {
        return $"{_webHelper.GetStoreLocation()}{OmnichannelCoreDefaults.ConfigurationRoute}";
    }

    /// <summary>
    /// Install the plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task InstallAsync()
    {
        await EnsureScheduleTaskAsync(OmnichannelCoreDefaults.OutboxPublisherTask);
        await EnsureScheduleTaskAsync(OmnichannelCoreDefaults.OutboxReconcilerTask);

        await base.InstallAsync();
    }

    /// <summary>
    /// Uninstall the plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task UninstallAsync()
    {
        await RemoveScheduleTaskAsync(OmnichannelCoreDefaults.OutboxPublisherTask.Type);
        await RemoveScheduleTaskAsync(OmnichannelCoreDefaults.OutboxReconcilerTask.Type);

        await base.UninstallAsync();
    }

    #endregion
}

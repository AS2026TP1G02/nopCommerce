using Nop.Core.Domain.Cms;
using Nop.Core.Infrastructure;
using Nop.Plugin.Widgets.Jotform.Components;
using Nop.Services.Cms;
using Nop.Services.Configuration;
using Nop.Services.Helpers;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Plugins;
using Nop.Web.Framework.Infrastructure;

namespace Nop.Plugin.Widgets.Jotform;

/// <summary>
/// Represents swiper widget
/// </summary>
public class JotformPlugin : BasePlugin, IWidgetPlugin
{
    #region Fields

    protected readonly ILocalizationService _localizationService;
    protected readonly INopFileProvider _fileProvider;
    protected readonly IPictureService _pictureService;
    protected readonly ISettingService _settingService;
    protected readonly IWebHelper _webHelper;
    protected readonly WidgetSettings _widgetSettings;

    #endregion

    #region Ctor

    public JotformPlugin(ILocalizationService localizationService,
        INopFileProvider fileProvider,
        IPictureService pictureService,
        ISettingService settingService,
        IWebHelper webHelper,
        WidgetSettings widgetSettings)
    {
        _localizationService = localizationService;
        _fileProvider = fileProvider;
        _pictureService = pictureService;
        _settingService = settingService;
        _webHelper = webHelper;
        _widgetSettings = widgetSettings;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Gets widget zones where this widget should be rendered
    /// </summary>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the widget zones
    /// </returns>
    public Task<IList<string>> GetWidgetZonesAsync()
    {
        return Task.FromResult<IList<string>>(new List<string> { PublicWidgetZones.BodyEndHtmlTagBefore });
    }

    /// <summary>
    /// Gets a configuration page URL
    /// </summary>
    public override string GetConfigurationPageUrl()
    {
        return $"{_webHelper.GetStoreLocation()}Admin/Jotform/Configure";
    }

    /// <summary>
    /// Gets a name of a view component for displaying widget
    /// </summary>
    /// <param name="widgetZone">Name of the widget zone</param>
    /// <returns>View component name</returns>
    public Type GetWidgetViewComponent(string widgetZone)
    {
        return typeof(WidgetJotformViewComponent);
    }

    /// <summary>
    /// Install plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task InstallAsync()
    {
        //settings
        await _settingService.SaveSettingAsync(new JotformSettings
        {
            Enabled = false,
            EmbedCode = string.Empty
        });

        if (!_widgetSettings.ActiveWidgetSystemNames.Contains(PluginDescriptor.SystemName))
        {
            _widgetSettings.ActiveWidgetSystemNames.Add(PluginDescriptor.SystemName);
            await _settingService.SaveSettingAsync(_widgetSettings);
        }

        await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
        {
            ["Plugins.Widgets.Jotform.Enabled"] = "Enabled",
            ["Plugins.Widgets.Jotform.Enabled.Hint"] = "Check to enable Jotform AI chat boot functionality",
            ["Plugins.Widgets.Jotform.EmbedCode"] = "Embed code",
            ["Plugins.Widgets.Jotform.EmbedCode.Hint"] = "Add your Jotform embed code here. You can get it from your Jotform account, on the publish tab of the chatbot settings.",
            ["Plugins.Widgets.Jotform.ScriptRequired"] = "Jotform script is required",
            ["Plugins.Widgets.Jotform.Description"] = "<div>" +
                "<p>AI Agents are powerful customer service tools that provide real-time assistance, answer user queries, and guide customers through processes like form-filling and troubleshooting.</p>" +
                "<p>By offering personalized, conversational interactions and 24-7 availability, they enhance customer satisfaction, streamline support workflows, and reduce response times, ensuring a seamless and efficient customer experience.</p>" +
                "<p><strong><a href=\"https://www.jotform.com/ai/agents/?partner=nopCommerce\" target=\"_blank\">Get your JotForm AI agent here</a>.</strong></p>" +
                "<p>Find more about its configuration <a href=\"\" target=\"_blank\">here</a>.</p>" +
                "</div>"
        });

        await base.InstallAsync();
    }

    /// <summary>
    /// Uninstall plugin
    /// </summary>
    /// <returns>A task that represents the asynchronous operation</returns>
    public override async Task UninstallAsync()
    {
        //settings
        await _settingService.DeleteSettingAsync<JotformSettings>();

        if (_widgetSettings.ActiveWidgetSystemNames.Contains(PluginDescriptor.SystemName))
        {
            _widgetSettings.ActiveWidgetSystemNames.Remove(PluginDescriptor.SystemName);
            await _settingService.SaveSettingAsync(_widgetSettings);
        }

        //locales
        await _localizationService.DeleteLocaleResourcesAsync("Plugins.Widgets.Jotform");

        await base.UninstallAsync();
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets a value indicating whether to hide this plugin on the widget list page in the admin area
    /// </summary>
    public bool HideInWidgetList => false;

    #endregion
}
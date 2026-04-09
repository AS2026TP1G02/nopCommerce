using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Widgets.Jotform.Components;

/// <summary>
/// Represents the view component to display Jotform AI agent chat boot button
/// </summary>
public class WidgetJotformViewComponent : NopViewComponent
{
    #region Fields
    protected readonly IStoreContext _storeContext;
    protected readonly ISettingService _settingService;
    protected readonly ILogger _logger;
    protected readonly ILocalizationService _localizationService;
    #endregion

    #region Ctor
    public WidgetJotformViewComponent(IStoreContext storeContext,
        ISettingService settingService,
        ILogger logger,
        ILocalizationService localizationService)
    {
        _storeContext = storeContext;
        _settingService = settingService;
        _logger = logger;
        _localizationService = localizationService;
    }
    #endregion

    #region Methods

    /// <summary>
    /// Invoke view component
    /// </summary>
    /// <param name="widgetZone">Widget zone name</param>
    /// <param name="additionalData">Additional data</param>
    /// <returns>
    /// A task that represents the asynchronous operation
    /// The task result contains the view component result
    /// </returns>
    public async Task<IViewComponentResult> InvokeAsync(string widgetZone, object additionalData)
    {
        var currentStore = await _storeContext.GetCurrentStoreAsync();
        var settings = await _settingService.LoadSettingAsync<JotformSettings>(currentStore.Id);

        if (!settings.Enabled || string.IsNullOrEmpty(settings.EmbedCode))
            return Content(string.Empty);

        return View("~/Plugins/Widgets.Jotform/Views/PublicInfo.cshtml", settings.EmbedCode);
    }
    #endregion
}

using Microsoft.AspNetCore.Mvc.ViewFeatures;
using EPiServer.Web.Mvc.TagHelpers.Experience.Internal;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.Rendering;

/// <summary>
/// Reads Visual Builder Style display settings for the composition node currently being rendered
/// by the epi-outline/epi-component tag helpers (see VisualBuilderStyleInit for the keys).
/// </summary>
public static class DisplaySettingExtensions
{
    public static string DisplaySetting(this ViewDataDictionary viewData, string key, string fallback)
        => viewData.TryGetCompositionRenderingNode(out var node)
           && node.DisplaySettings is not null
           && node.DisplaySettings.TryGetValue(key, out var value)
           && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;

    public static bool DisplaySettingEnabled(this ViewDataDictionary viewData, string key)
        => bool.TryParse(viewData.DisplaySetting(key, "false"), out var enabled) && enabled;
}

using EPiServer.Authorization;
using EPiServer.Framework;
using EPiServer.Logging;
using EPiServer.Shell.Navigation;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.UI;

/// <summary>
/// Registers the ContentSeeder admin gadget as a menu item in the Optimizely CMS shell.
/// Restricted to the CmsAdmin role via AuthorizationPolicy.
/// </summary>
[MenuProvider]
public sealed class ContentSeederMenuProvider : IMenuProvider
{
    private static readonly ILogger _log = LogManager.GetLogger(typeof(ContentSeederMenuProvider));

    /// <inheritdoc/>
    public IEnumerable<MenuItem> GetMenuItems()
    {
        _log.Information("ContentSeeder: GetMenuItems called — menu item being registered.");

        yield return new UrlMenuItem(
            "Content Seeder",
            MenuPaths.Global + "/cms/contentseeder",
            "/contentseeder/")
        {
            SortIndex = 100,
            AuthorizationPolicy = CmsPolicyNames.CmsAdmin
        };
    }
}

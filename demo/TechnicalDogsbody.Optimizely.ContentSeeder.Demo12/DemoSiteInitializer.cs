using EPiServer;
using EPiServer.Core;
using EPiServer.DataAccess;
using EPiServer.Security;
using EPiServer.Web;
using Microsoft.Extensions.Logging;
using TechnicalDogsbody.Optimizely.ContentSeeder.Demo12.ContentTypes;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo12;

/// <summary>
/// Makes sure the demo has a site: a site definition whose start page is a home page of its own, so the root URL (/)
/// resolves to a page.
/// <para>
/// The site's home page is not seeded content. It sits beside the home pages that seed definitions create, and seeding
/// never changes it or the site definition. A site that already exists is left alone while its start page still exists; it is
/// only repointed if that page has been deleted.
/// </para>
/// </summary>
public sealed class DemoSiteInitializer(
    ISiteDefinitionRepository siteDefinitionRepository,
    IContentLoader contentLoader,
    IContentRepository contentRepository,
    ILogger<DemoSiteInitializer> logger)
{
    // Fixed name so re-runs on the same DB are idempotent.
    private const string DemoSiteName = "DemoSite";

    // The site's own home page. The seeder doesn't create it or track it, so reseeding and deleting never touch it.
    private const string SiteHomeName = "Demo Site";

    /// <summary>Creates the site home page and the site definition if they are missing. Safe to run repeatedly.</summary>
    public Task EnsureSiteAsync(CancellationToken ct = default)
    {
        var existing = siteDefinitionRepository.List().FirstOrDefault(s => s.Name == DemoSiteName);

        // Leave a working site exactly as it is. Seeding must never move the site's home page.
        if (existing is not null && contentLoader.TryGet<IContent>(existing.StartPage, out _))
        {
            logger.LogDebug("Demo site already has a home page - nothing to do.");
            return Task.CompletedTask;
        }

        var homeRef = FindOrCreateSiteHome();

        if (existing is not null)
        {
            var writable = existing.CreateWritableClone();
            writable.StartPage = homeRef;
            siteDefinitionRepository.Save(writable);

            logger.LogInformation("Demo site's home page was missing, so it now points at '{Home}' (id {Id}).", SiteHomeName, homeRef.ID);
            return Task.CompletedTask;
        }

        var site = new SiteDefinition
        {
            Name = DemoSiteName,
            SiteUrl = new Uri("https://localhost:5001/"),
            StartPage = homeRef,
        };

        // EPiServer needs at least one host. localhost covers local dev regardless of port, and the wildcard catches any
        // other hostname Aspire assigns.
        site.Hosts.Add(new HostDefinition { Name = "localhost", Type = HostDefinitionType.Primary });
        site.Hosts.Add(new HostDefinition { Name = SiteDefinition.WildcardHostName });

        siteDefinitionRepository.Save(site);

        logger.LogInformation("Demo site created. The site root / routes to '{Home}' (id {Id}).", SiteHomeName, homeRef.ID);
        return Task.CompletedTask;
    }

    // The site's home page, under the root beside the seeded home pages. Created once, and found by name after that.
    private ContentReference FindOrCreateSiteHome()
    {
        var home = contentLoader.GetChildren<StartPage>(ContentReference.RootPage)
            .FirstOrDefault(p => p.Name == SiteHomeName);

        if (home is not null)
            return new ContentReference(home.ContentLink.ID);

        var page = contentRepository.GetDefault<StartPage>(ContentReference.RootPage);
        page.Name = SiteHomeName;
        var saved = contentRepository.Save(page, SaveAction.Publish, AccessLevel.NoAccess);

        // Strip WorkID — the start page must be a permanent reference (WorkID = 0).
        return new ContentReference(saved.ID);
    }
}

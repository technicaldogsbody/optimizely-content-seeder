using EPiServer;
using EPiServer.Applications;
using EPiServer.Core;
using EPiServer.DataAccess;
using EPiServer.Security;
using Microsoft.Extensions.Logging;
using TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo;

/// <summary>
/// Makes sure the demo has a site: a wildcard application (CMS 13's replacement for SiteDefinition) whose entry point is a
/// home page of its own, so the root URL (/) resolves to a page.
/// <para>
/// The site's home page is not seeded content. It sits beside the home pages that seed definitions create, and seeding
/// never changes it or the application. An application that already exists is left alone while its entry point still
/// exists; it is only repointed if that page has been deleted.
/// </para>
/// </summary>
public sealed class DemoSiteInitializer(
    IApplicationRepository applicationRepository,
    IContentLoader contentLoader,
    IContentRepository contentRepository,
    ILogger<DemoSiteInitializer> logger)
{
    // Fixed name so re-runs on the same DB are idempotent — applications are keyed by name, not a GUID id.
    private const string DemoApplicationName = "DemoSite";

    // The site's own home page. The seeder doesn't create it or track it, so reseeding and deleting never touch it.
    private const string SiteHomeName = "Demo Site";

    /// <summary>Creates the site home page and the application if they are missing. Safe to run repeatedly.</summary>
    public async Task EnsureSiteAsync(CancellationToken ct = default)
    {
        var existing = applicationRepository.List()
            .OfType<InProcessWebsite>()
            .FirstOrDefault(a => a.Name == DemoApplicationName);

        // Leave a working application exactly as it is. Seeding must never move the site's home page.
        if (existing is not null && contentLoader.TryGet<IContent>(existing.EntryPoint, out _))
        {
            logger.LogDebug("Demo application already has a home page - nothing to do.");
            return;
        }

        var homeRef = FindOrCreateSiteHome();

        if (existing is not null)
        {
            var writable = (InProcessWebsite)existing.CreateWritableClone();
            writable.EntryPoint = homeRef;
            await applicationRepository.SaveAsync(writable, ct).ConfigureAwait(false);

            logger.LogInformation("Demo application's home page was missing, so it now points at '{Home}' (id {Id}).", SiteHomeName, homeRef.ID);
            return;
        }

        var site = new InProcessWebsite(DemoApplicationName, homeRef);

        // Hosts has no public setter — it returns a mutable list to append to, not to reassign.
        // EPiServer requires at least one Primary host for outbound link generation.
        // localhost covers local dev regardless of port.
        // The wildcard ("*", Default) catches any other hostname Aspire assigns.
        site.Hosts.Add(new ApplicationHost("localhost") { Type = ApplicationHostType.Primary });
        site.Hosts.Add(new ApplicationHost("*") { Type = ApplicationHostType.Default });

        await applicationRepository.SaveAsync(site, ct).ConfigureAwait(false);
        // IsDefault has no public setter either — MakeDefaultAsync is the documented way to set it.
        await applicationRepository.MakeDefaultAsync(site, true, ct).ConfigureAwait(false);

        logger.LogInformation("Demo application created. The site root / routes to '{Home}' (id {Id}).", SiteHomeName, homeRef.ID);
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

        // Strip WorkID — EntryPoint must be a permanent reference (WorkID = 0).
        return new ContentReference(saved.ID);
    }
}

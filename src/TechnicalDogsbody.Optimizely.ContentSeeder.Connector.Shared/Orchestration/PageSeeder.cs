using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.DataAnnotations;
using EPiServer.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Infrastructure;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;
using static TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration.ContentProperties;
using static TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration.ContentRepositoryMethods;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;

/// <summary>
/// Seeds the page tree: each page under its parent, its content area and layout, and its language branches.
/// </summary>
internal sealed class PageSeeder(
    IContentRepository contentRepository,
    IContentLoader contentLoader,
    IContentTypeRepository contentTypeRepository,
    ISeedTracker seedTracker,
    IContentGenerationProvider contentGenerationProvider,
    SaveActions saves,
    VideoApplier videos,
    LanguageTranslator translator,
    ISectionSeeder sections,
    ILogger logger)
{
    // ── private: page tree recursion ─────────────────────────────────────────

    /// <summary>
    /// Creates the pages for a list of nodes under a parent, and then their children, recursively.
    /// </summary>
    internal async Task SeedNodesAsync(
        IReadOnlyList<SeedNode> nodes,
        ContentReference parent,
        string seedDefinitionId,
        string language,
        string[] additionalLanguages,
        Dictionary<string, ContentReference> blockRefs,
        ISectionSet sectionSet,
        int depth,
        Guid seedRunId,
        ResumeIndex completePagesQueue,
        CancellationToken ct)
    {
        foreach (var node in nodes)
        {
            for (int i = 0; i < node.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var pageRef = await CreatePageAsync(
                    node, i, parent, seedDefinitionId, language, additionalLanguages,
                    blockRefs, sectionSet, depth, seedRunId, completePagesQueue, ct).ConfigureAwait(false);

                if (node.Children.Count > 0)
                {
                    await SeedNodesAsync(
                        node.Children, pageRef, seedDefinitionId, language, additionalLanguages,
                        blockRefs, sectionSet, depth + 1, seedRunId, completePagesQueue, ct).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task<ContentReference> CreatePageAsync(
        SeedNode node,
        int instanceIndex,
        ContentReference parent,
        string seedDefinitionId,
        string language,
        string[] additionalLanguages,
        Dictionary<string, ContentReference> blockRefs,
        ISectionSet sectionSet,
        int depth,
        Guid seedRunId,
        ResumeIndex completePagesQueue,
        CancellationToken ct)
    {
        using var _ = SeedInstanceScope.Enter(node.Instance);

        var pageContentType = contentTypeRepository.Load(node.Type)
            ?? throw new InvalidOperationException(
                $"ContentSeeder: No content type registered for page type '{node.Type.FullName}'. " +
                "Ensure the type has a [ContentType] attribute and the application has initialised.");

        // Resume: reuse a page that already exists *under this parent and of this type*. Matching on both is what makes
        // adding or reordering nodes safe: a new node finds nothing to reuse and is created, and its children are created
        // under it, rather than under whichever existing page happened to come next.
        // Language versions of a reused page are left as they are.
        if (completePagesQueue.TryTake(parent.ID, pageContentType.ID, out var existingRecord))
        {
            logger.LogDebug("Skipping already-seeded page (ContentId {ContentId}) on resume.", existingRecord.ContentId);
            return new ContentReference(existingRecord.ContentId);
        }

        var page = (PageData)_getDefaultTwoParam
            .MakeGenericMethod(node.Type)
            .Invoke(contentRepository, [parent, Cultures.Resolve(language, logger)])!;

        // Use the concrete type (not the Castle proxy) — see CreateBlockLanguageBranchAsync for rationale.
        var props = GetContentProperties(node.Type).ToList();
        await contentGenerationProvider.PopulateAsync(page, props, language, node.Type, node.ImageSizes, ct).ConfigureAwait(false);
        await videos.ApplyVideosAsync(page, node.Videos, ct).ConfigureAwait(false);
        node.Configure?.Invoke(page, instanceIndex);

        // Restore ContentTypeID — providers must never overwrite it.
        page.ContentTypeID = pageContentType.ID;

        // EPiServer rejects a save when Name is empty. GetDefault returns an empty name;
        // Bogus does not set it (Name is on IContent, always filtered); and not every node
        // has a configure callback. Set a safe fallback so every page has a visible name.
        if (string.IsNullOrWhiteSpace(page.Name))
            page.Name = $"{node.Type.Name}-{instanceIndex}";

        var trackRecord = new ContentSeederRecord
        {
            Id = Guid.NewGuid(),
            SeedDefinitionId = seedDefinitionId,
            ContentGuid = page.ContentGuid,
            ContentId = 0,
            ContentType = ContentSeederRecord.ContentTypes.Page,
            Language = language,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            SeedRunId = seedRunId,
            IsPartial = true
        };
        await seedTracker.InsertAsync(trackRecord, ct).ConfigureAwait(false);

        // A page with an empty [Required] field is saved as a draft, and its later saves in this method keep that choice.
        var pageAction = saves.ActionFor(page, GetContentProperties(node.Type), $"Page {node.Type.Name}");
        var savedRef = contentRepository.Save(page, pageAction, AccessLevel.NoAccess);
        trackRecord.ContentId = savedRef.ID;

        // Apply ContentArea assignments on master language version
        if (node.ContentAreaAssignments.Count > 0)
        {
            var writablePage = contentLoader.Get<PageData>(savedRef).CreateWritableClone() as PageData
                ?? throw new InvalidOperationException($"Failed to get writable clone of page {savedRef}.");

            foreach (var assignment in node.ContentAreaAssignments)
            {
                var contentArea = new ContentArea();
                foreach (string blockId in assignment.BlockIds)
                {
                    if (!blockRefs.TryGetValue(blockId, out var blockRef))
                        throw new InvalidOperationException(
                            $"ContentAreaAssignment references unknown BlockId '{blockId}'. " +
                            "Ensure this ID is declared in SeedTree.Blocks.");
                    contentArea.Items.Add(new ContentAreaItem { ContentLink = blockRef });
                }

                var prop = writablePage.GetType().GetProperty(assignment.PropertyName,
                    BindingFlags.Public | BindingFlags.Instance);
                prop?.SetValue(writablePage, contentArea);
            }

            contentRepository.Save(writablePage, pageAction, AccessLevel.NoAccess);
        }

        // Section placements (Visual Builder layouts, for connectors that have them) are the connector's to apply.
        await sections.PlaceSectionsAsync(node, savedRef, blockRefs, sectionSet, language, pageAction, ct).ConfigureAwait(false);

        await seedTracker.MarkCompleteAsync(trackRecord.Id, savedRef.ID, ct).ConfigureAwait(false);

        // Language branches — apply after master is settled; no ContentArea on branches
        foreach (string lang in additionalLanguages)
        {
            ct.ThrowIfCancellationRequested();
            await CreatePageLanguageBranchAsync(
                node, instanceIndex, savedRef, seedDefinitionId, language, lang, seedRunId, sectionSet, ct).ConfigureAwait(false);
        }

        return savedRef;
    }

    private async Task CreatePageLanguageBranchAsync(
        SeedNode node,
        int instanceIndex,
        ContentReference masterRef,
        string seedDefinitionId,
        string masterLanguage,
        string language,
        Guid seedRunId,
        ISectionSet sectionSet,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var _ = SeedInstanceScope.Enter(node.Instance);

        var pageContentType = contentTypeRepository.Load(node.Type)
            ?? throw new InvalidOperationException(
                $"ContentSeeder: No content type registered for page type '{node.Type.FullName}'.");

        var branch = (PageData)_createLanguageBranch
            .MakeGenericMethod(node.Type)
            .Invoke(contentRepository, [masterRef, Cultures.Resolve(language, logger)])!;

        // Use the concrete type (not the Castle proxy) — see CreateBlockLanguageBranchAsync for rationale.
        // For language branches only populate [CultureSpecific] properties. Page properties
        // without [CultureSpecific] are "common" — EPiServer shares them from the master language
        // version and will warn (and silently discard) any attempt to save them on a branch.
        var props = GetContentProperties(node.Type)
            .Where(p => p.GetCustomAttribute<CultureSpecificAttribute>() is not null)
            .ToList();
        await translator.PopulateBranchAsync(branch, masterRef, masterLanguage, props, language, node.Type, node.ImageSizes, node.Videos, ct).ConfigureAwait(false);
        LanguageTranslator.ConfigureLanguageVersion(branch, node.Configure, instanceIndex, node.Type);

        // A page's layout belongs to each language version. Without one, the page throws when it is rendered in
        // that language, so every language gets the master's layout, translated.
        await sections.CopyLayoutToBranchAsync(node, branch, masterRef, sectionSet, masterLanguage, language, ct).ConfigureAwait(false);

        // EPiServer validates that Name is non-empty before saving; set it if missing.
        if (string.IsNullOrWhiteSpace(branch.Name))
            branch.Name = $"{node.Type.Name}-{instanceIndex}";

        // Restore ContentTypeID — providers must never overwrite it.
        branch.ContentTypeID = pageContentType.ID;

        var trackRecord = new ContentSeederRecord
        {
            Id = Guid.NewGuid(),
            SeedDefinitionId = seedDefinitionId,
            ContentGuid = branch.ContentGuid,
            ContentId = masterRef.ID,
            ContentType = ContentSeederRecord.ContentTypes.Page,
            Language = language,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            SeedRunId = seedRunId,
            IsPartial = true
        };
        await seedTracker.InsertAsync(trackRecord, ct).ConfigureAwait(false);

        var branchRef = contentRepository.Save(
            branch, saves.ActionFor(branch, GetContentProperties(node.Type), $"Page {node.Type.Name} ({language})"), AccessLevel.NoAccess);
        await seedTracker.MarkCompleteAsync(trackRecord.Id, branchRef.ID, ct).ConfigureAwait(false);
    }
}

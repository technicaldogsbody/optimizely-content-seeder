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
        PageLinks links,
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

                // Reused pages are recorded too, so a page from an earlier run can still be pointed at, and gets any
                // references added to the definition since.
                links.Add(node, pageRef);

                if (node.Children.Count > 0)
                {
                    await SeedNodesAsync(
                        node.Children, pageRef, seedDefinitionId, language, additionalLanguages,
                        blockRefs, sectionSet, depth + 1, seedRunId, completePagesQueue, links, ct).ConfigureAwait(false);
                }
            }
        }
    }

    // ── reference pass ───────────────────────────────────────────────────────

    /// <summary>
    /// Sets each page's content reference properties to the keyed pages and shared blocks they name, and fills content
    /// areas whose assignment names a keyed page. Runs after the whole
    /// tree exists, so a page can point at a sibling or a page created after it. A property that already has a value is
    /// left alone, so an editor's change is kept and a later Seed only fills references that are still empty.
    /// <c>[CultureSpecific]</c> properties are set on every language version; other properties are shared by EPiServer
    /// and set on the master only.
    /// </summary>
    internal void ApplyReferences(
        PageLinks links,
        IReadOnlyDictionary<string, ContentReference> blockRefs,
        string masterLanguage,
        string[] additionalLanguages,
        CancellationToken ct)
    {
        foreach (var (node, pageRef) in links.Pending)
        {
            ct.ThrowIfCancellationRequested();

            var targets = node.References
                .Select(reference => (Reference: reference, Property: ReferenceProperty(node, reference), Target: Resolve(node, reference.PropertyName, reference.Key)))
                .ToList();

            // Content areas, like ContentAreaAssignments made when a page is created, are set on the master only.
            var areas = node.ContentAreaAssignments.Where(links.NamesPage).ToList();

            ApplyReferencesToLanguage(node, pageRef, masterLanguage, targets, areas, Resolve);

            var cultureSpecific = targets
                .Where(t => t.Property.GetCustomAttribute<CultureSpecificAttribute>() is not null)
                .ToList();
            if (cultureSpecific.Count == 0)
                continue;

            foreach (string language in additionalLanguages)
            {
                ct.ThrowIfCancellationRequested();
                ApplyReferencesToLanguage(node, pageRef, language, cultureSpecific, [], Resolve);
            }
        }

        (ContentReference Link, bool IsPage) Resolve(SeedNode node, string propertyName, string key)
        {
            if (links.TryGetPage(key, out var page))
                return (page, true);
            if (blockRefs.TryGetValue(key, out var block))
                return (block, false);

            throw new InvalidOperationException(
                $"'{node.Type.Name}.{propertyName}' references unknown key '{key}'. " +
                "Give the target page that key, or declare a block with that ID in SeedTree.Blocks.");
        }
    }

    /// <summary>
    /// Checks every reference names a writable <c>ContentReference</c> property, and that a <c>PageReference</c> property
    /// never names a block, so a bad definition fails before any content is created.
    /// </summary>
    internal static void ValidateReferenceProperties(IReadOnlyList<SeedNode> nodes, IReadOnlyCollection<string> blockIds)
    {
        foreach (var node in nodes)
        {
            // Only assignments that name a page: a block-only one keeps its original behaviour.
            foreach (var area in node.ContentAreaAssignments.Where(a => a.BlockIds.Any(id => !blockIds.Contains(id))))
                _ = ContentAreaProperty(node, area);

            foreach (var reference in node.References)
            {
                var property = ReferenceProperty(node, reference);
                // Any reference will do: only whether the target is a page or a block matters here.
                _ = ToPropertyValue(node, reference, property, (new ContentReference(1), !blockIds.Contains(reference.Key)));
            }

            ValidateReferenceProperties(node.Children, blockIds);
        }
    }

    private void ApplyReferencesToLanguage(
        SeedNode node,
        ContentReference pageRef,
        string language,
        IReadOnlyList<(ContentReferenceAssignment Reference, PropertyInfo Property, (ContentReference Link, bool IsPage) Target)> targets,
        IReadOnlyList<ContentAreaAssignment> areas,
        Func<SeedNode, string, string, (ContentReference Link, bool IsPage)> resolve)
    {
        if (!contentLoader.TryGet<PageData>(pageRef, Cultures.Resolve(language, logger), out var page))
        {
            logger.LogWarning(
                "ContentSeeder: Page {ContentId} ({Language}) could not be loaded, so its references were not set.", pageRef.ID, language);
            return;
        }

        PageData? writable = null;
        foreach (var (reference, property, target) in targets)
        {
            if (property.GetValue(page) is ContentReference current && !ContentReference.IsNullOrEmpty(current))
            {
                logger.LogDebug("Keeping existing value of {Property} on page {ContentId} ({Language}).",
                    property.Name, pageRef.ID, language);
                continue;
            }

            writable ??= page.CreateWritableClone() as PageData
                ?? throw new InvalidOperationException($"Failed to get writable clone of page {pageRef}.");

            property.SetValue(writable, ToPropertyValue(node, reference, property, target));
        }

        foreach (var area in areas)
        {
            var property = ContentAreaProperty(node, area);
            if (property.GetValue(page) is ContentArea { Items.Count: > 0 })
            {
                logger.LogDebug("Keeping existing content of {Property} on page {ContentId} ({Language}).",
                    property.Name, pageRef.ID, language);
                continue;
            }

            var contentArea = new ContentArea();
            foreach (string id in area.BlockIds)
                contentArea.Items.Add(new ContentAreaItem { ContentLink = resolve(node, area.PropertyName, id).Link.ToReferenceWithoutVersion() });

            writable ??= page.CreateWritableClone() as PageData
                ?? throw new InvalidOperationException($"Failed to get writable clone of page {pageRef}.");

            property.SetValue(writable, contentArea);
        }

        if (writable is null)
            return;

        contentRepository.Save(
            writable,
            saves.ActionFor(writable, GetContentProperties(node.Type), $"Page {node.Type.Name} ({language})"),
            AccessLevel.NoAccess);
    }

    private static PropertyInfo ReferenceProperty(SeedNode node, ContentReferenceAssignment reference)
    {
        var property = node.Type.GetProperty(reference.PropertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null || !property.CanWrite)
            throw new InvalidOperationException(
                $"ContentReferenceAssignment names '{node.Type.Name}.{reference.PropertyName}', which is not a writable property.");

        if (!typeof(ContentReference).IsAssignableFrom(property.PropertyType))
            throw new InvalidOperationException(
                $"ContentReferenceAssignment names '{node.Type.Name}.{reference.PropertyName}', which is a {property.PropertyType.Name}. " +
                "It must be a ContentReference or PageReference.");

        return property;
    }

    private static PropertyInfo ContentAreaProperty(SeedNode node, ContentAreaAssignment area)
    {
        var property = node.Type.GetProperty(area.PropertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null || !property.CanWrite || !property.PropertyType.IsAssignableFrom(typeof(ContentArea)))
            throw new InvalidOperationException(
                $"ContentAreaAssignment names '{node.Type.Name}.{area.PropertyName}', which is not a writable ContentArea property.");

        return property;
    }

    private static ContentReference ToPropertyValue(
        SeedNode node, ContentReferenceAssignment reference, PropertyInfo property, (ContentReference Link, bool IsPage) target)
    {
        if (property.PropertyType.IsAssignableFrom(typeof(ContentReference)))
            return target.Link.ToReferenceWithoutVersion();

        // PageReference is obsolete in CMS 13 but still common on CMS 12 page types.
#pragma warning disable CS0618
        if (property.PropertyType.IsAssignableFrom(typeof(PageReference)) && target.IsPage)
            return new PageReference(target.Link.ID);
#pragma warning restore CS0618

        throw new InvalidOperationException(
            $"'{node.Type.Name}.{reference.PropertyName}' is a {property.PropertyType.Name}, so it cannot point at '{reference.Key}', " +
            "which is a block. Use a ContentReference property for a block.");
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
        await contentGenerationProvider.PopulateWithImageProvidersAsync(page, props, language, node.Type, node.ImageSizes, node.ImageProviders, ct).ConfigureAwait(false);
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

        // Apply ContentArea assignments on master language version. An assignment that names a page waits for the
        // reference pass (see ApplyReferences), since that page may not exist yet.
        var blockOnlyAssignments = node.ContentAreaAssignments
            .Where(a => a.BlockIds.All(blockRefs.ContainsKey))
            .ToList();
        if (blockOnlyAssignments.Count > 0)
        {
            var writablePage = contentLoader.Get<PageData>(savedRef).CreateWritableClone() as PageData
                ?? throw new InvalidOperationException($"Failed to get writable clone of page {savedRef}.");

            foreach (var assignment in blockOnlyAssignments)
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
        // For language branches only populate [CultureSpecific] properties (and the culture-specific properties of local
        // blocks). Page properties without [CultureSpecific] are "common" — EPiServer shares them from the master language
        // version and will warn (and silently discard) any attempt to save them on a branch.
        var props = GetLanguageVersionProperties(node.Type);
        await translator.PopulateBranchAsync(branch, masterRef, masterLanguage, props, language, node.Type, node.ImageSizes, node.Videos, ct, node.ImageProviders).ConfigureAwait(false);
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

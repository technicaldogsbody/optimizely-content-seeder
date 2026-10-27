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
using EPiServer.VisualBuilder;
using EPiServer.VisualBuilder.Compositions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Infrastructure;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;
using static TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration.ContentProperties;
using static TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration.CompositionNodes;
using static TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration.ContentRepositoryMethods;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;

/// <summary>
/// The named rule sets a structural section placement was built with: the section's own, and each of its inline elements in the
/// order they were created. Kept so a language branch can use the same rules when it translates the placement.
/// </summary>
internal sealed record SectionInstancePlan(string? SectionInstance, IReadOnlyList<string?> ElementInstances);

/// <summary>A section of a seed definition, as the Optimizely 13 connector seeds it.</summary>
/// <param name="Definition">The section's definition.</param>
/// <param name="IsStructural">True for a SectionData section, created once as a blueprint; false for a shared section, seeded as a block.</param>
/// <param name="BlueprintId">The blueprint of a structural section. Empty for a shared section.</param>
internal readonly record struct SectionRef(SectionDefinition Definition, bool IsStructural, Guid BlueprintId);

/// <summary>The sections a seed definition resolved to, held by the Optimizely 13 connector between seeding and placing them.</summary>
internal sealed record SectionSet(IReadOnlyDictionary<string, SectionRef> Refs) : ISectionSet;

/// <summary>
/// Seeds Visual Builder sections for Optimizely 13. A structural section is a blueprint per definition, placed inline on each
/// Experience with its rows, columns and elements; element content is generated in parallel and attached in order. A shared
/// section is seeded as a block by the shared seeder, and referenced from every Experience.
/// </summary>
internal sealed class SectionSeeder(
    IContentRepository contentRepository,
    IContentLoader contentLoader,
    IContentTypeRepository contentTypeRepository,
    IContentGenerationProvider contentGenerationProvider,
    IBlueprintRepository blueprintRepository,
    IBlueprintContentFactory blueprintContentFactory,
    ICompositionMapper compositionMapper,
    ISeedTracker seedTracker,
    VideoApplier videos,
    GenerationRunner runner,
    ILogger logger) : ISectionSeeder
{
    private readonly CompositionTranslator _layouts = new(contentLoader, contentGenerationProvider, compositionMapper, runner, logger);

    // ── ISectionSeeder ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool SupportsSections => true;

    /// <inheritdoc/>
    public IReadOnlyList<BlockDefinition> SharedSectionBlocks(SeedTree tree)
        => tree.SectionsOf()
            .Where(section => !typeof(SectionData).IsAssignableFrom(section.Type))
            .Select(section => new BlockDefinition(section.Id, section.Type, section.Configure, section.ImageSizes, section.Videos, section.Instance))
            .ToList();

    /// <inheritdoc/>
    public (int Structural, int Shared) CountSections(SeedTree tree)
    {
        int structural = tree.SectionsOf().Count(section => typeof(SectionData).IsAssignableFrom(section.Type));
        return (structural, tree.SectionsOf().Count - structural);
    }

    /// <inheritdoc/>
    public async Task<ISectionSet> SeedBlueprintsAsync(
        SeedTree tree,
        string seedDefinitionId,
        string masterLanguage,
        Guid seedRunId,
        IReadOnlyList<ContentSeederRecord> existingRecords,
        CancellationToken ct)
    {
        var refs = new Dictionary<string, SectionRef>(StringComparer.Ordinal);
        foreach (var sectionDef in tree.SectionsOf())
        {
            ct.ThrowIfCancellationRequested();

            if (!typeof(SectionData).IsAssignableFrom(sectionDef.Type))
            {
                refs[sectionDef.Id] = new SectionRef(sectionDef, false, Guid.Empty);
                continue;
            }

            var existingBlueprint = existingRecords
                .FirstOrDefault(r => r.ContentType == ContentSeederRecord.ContentTypes.SectionBlueprint
                                      && r.BlockDefinitionId == sectionDef.Id);

            if (existingBlueprint is { IsPartial: false })
            {
                refs[sectionDef.Id] = new SectionRef(sectionDef, true, existingBlueprint.ContentGuid);
                logger.LogDebug("Skipping existing section blueprint {SectionId}.", sectionDef.Id);
                continue;
            }

            var blueprintId = await CreateSectionBlueprintAsync(
                sectionDef, seedDefinitionId, masterLanguage, seedRunId, ct).ConfigureAwait(false);
            refs[sectionDef.Id] = new SectionRef(sectionDef, true, blueprintId);
        }

        return new SectionSet(refs);
    }

    /// <inheritdoc/>
    public async Task PlaceSectionsAsync(
        SeedNode node,
        ContentReference pageRef,
        IReadOnlyDictionary<string, ContentReference> blockRefs,
        ISectionSet sectionSet,
        string language,
        SaveAction pageAction,
        CancellationToken ct)
    {
        var slots = node.SectionsOf();
        if (slots.Count == 0)
            return;

        var refs = Refs(sectionSet);
        var writableExperience = contentLoader.Get<ExperienceData>(pageRef).CreateWritableClone() as ExperienceData
            ?? throw new InvalidOperationException(
                $"Node type '{node.Type.Name}' declares Sections but its page is not an ExperienceData.");
        var composition = new Composition { ExperienceData = writableExperience };

        foreach (var slot in slots)
        {
            ct.ThrowIfCancellationRequested();

            if (!refs.TryGetValue(slot.SectionId, out var sectionRef))
                throw new InvalidOperationException(
                    $"SectionSlotAssignment references unknown SectionId '{slot.SectionId}'. " +
                    "Ensure this ID is declared in SeedTree.sections.");

            CompositionNode slotNode;
            if (sectionRef.IsStructural)
            {
                // Inline — embedded directly in this Experience, never saved as separate content.
                var instance = await blueprintContentFactory.CreateInlineBlockAsync(
                    sectionRef.BlueprintId,
                    pageRef,
                    new CreateBlueprintInlineBlockOptions
                    {
                        RequiredBlueprintAccess = AccessLevel.NoAccess,
                        RequiredDestinationAccess = AccessLevel.NoAccess
                    },
                    ct).ConfigureAwait(false);

                if (instance is not SectionData sectionData)
                    throw new InvalidOperationException(
                        $"Section '{slot.SectionId}' resolved to unsupported type '{instance.GetType().Name}'.");

                var sectionNode = composition.AddSection(sectionData, slot.Name);
                await BuildSectionRowsAsync(sectionNode, sectionRef.Definition, language, ct).ConfigureAwait(false);
                slotNode = sectionNode;
            }
            else
            {
                // Shared — the block seeded once for the definition, referenced from every page.
                if (!blockRefs.TryGetValue(slot.SectionId, out var sharedRef))
                    throw new InvalidOperationException($"Section '{slot.SectionId}' has no shared content to reference.");

                var shared = contentLoader.Get<IContentData>(sharedRef);
                slotNode = shared is BlockData sharedBlock
                    ? composition.AddComponent(sharedBlock, slot.Name)
                    : throw new InvalidOperationException(
                        $"Section '{slot.SectionId}' resolved to unsupported type '{shared.GetType().Name}'.");
            }

            if (slot.DisplayTemplateKey is not null || slot.DisplaySettings is not null)
                slotNode.WithStyles(slot.DisplayTemplateKey!, slot.DisplaySettings?.ToDictionary(k => k.Key, v => v.Value)!);
        }

        compositionMapper.Populate(writableExperience, composition);
        contentRepository.Save(writableExperience, pageAction, AccessLevel.NoAccess);
    }

    /// <inheritdoc/>
    public async Task CopyLayoutToBranchAsync(
        SeedNode node,
        PageData branch,
        ContentReference masterRef,
        ISectionSet sectionSet,
        string masterLanguage,
        string language,
        CancellationToken ct)
    {
        // An Experience's layout belongs to each language version. Other pages have none to copy.
        if (branch is not ExperienceData branchExperience)
            return;

        await _layouts.CopyCompositionToBranchAsync(
            branchExperience, masterRef, masterLanguage, language, ct, PlanSlots(node, Refs(sectionSet))).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task DeleteBlueprintAsync(Guid blueprintId, CancellationToken ct)
        => blueprintRepository.DeleteAsync(
            blueprintId,
            new DeleteBlueprintOptions { RequiredAccess = AccessLevel.NoAccess },
            ct);

    private static IReadOnlyDictionary<string, SectionRef> Refs(ISectionSet sectionSet)
        => ((SectionSet)sectionSet).Refs;

    // The named rule set of each slot's section, and of its inline elements, in the order the master composition holds them.
    // Shared slots have no plan: their content is translated as a block, with its own named set.
    private IReadOnlyList<SectionInstancePlan?> PlanSlots(SeedNode node, IReadOnlyDictionary<string, SectionRef> refs)
        => node.SectionsOf().Select(slot => refs.TryGetValue(slot.SectionId, out var sectionRef) && sectionRef.IsStructural
            ? new SectionInstancePlan(sectionRef.Definition.Instance, PlanElementInstances(sectionRef.Definition))
            : null).ToList();

    // ── private: section blueprint creation ──────────────────────────────────

    // Builds and Bogus-populates an in-memory section instance, then creates an Optimizely
    // Blueprint from it (never saves the section itself as standalone content — grid-layout
    // SectionData is rejected by SharedSectionValidator when saved directly; see plan notes).
    // Blueprint creation is treated as atomic from the tracker's perspective: if the process
    // crashes mid-creation there's simply no record, and the next run re-creates it fresh.
    /// <summary>
    /// Creates the blueprint for a structural section and tracks it. The section itself is never saved as standalone content.
    /// </summary>
    internal async Task<Guid> CreateSectionBlueprintAsync(
        SectionDefinition sectionDef,
        string seedDefinitionId,
        string language,
        Guid seedRunId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var _ = SeedInstanceScope.Enter(sectionDef.Instance);

        var contentType = contentTypeRepository.Load(sectionDef.Type)
            ?? throw new InvalidOperationException(
                $"ContentSeeder: No content type registered for section type '{sectionDef.Type.FullName}'. " +
                "Ensure the type has a [ContentType] attribute and the application has initialised.");

        var section = (IContentData)_getDefaultOneParam
            .MakeGenericMethod(sectionDef.Type)
            .Invoke(contentRepository, [ContentReference.GlobalBlockFolder])!;

        var props = GetContentProperties(sectionDef.Type).ToList();
        await contentGenerationProvider.PopulateAsync(section, props, language, sectionDef.Type, sectionDef.ImageSizes, ct).ConfigureAwait(false);
        await videos.ApplyVideosAsync(section, sectionDef.Videos, ct).ConfigureAwait(false);
        sectionDef.Configure?.Invoke(section, 0);

        // Defensive: always restore ContentTypeID — providers must never overwrite it.
        ((IContent)section).ContentTypeID = contentType.ID;

        // A SectionData must always have PopulateSection called on it — even with zero rows —
        // before it can be blueprinted. This sets Layout.Type = "grid"; skipping it (e.g. gating
        // on Rows.Count > 0) leaves Layout.Type empty and blueprint creation fails validation.
        //
        // The row/column/element tree is deliberately NOT baked in here — it is built per placement
        // on the Composition's SectionNode (see BuildSectionRowsAsync / CreatePageAsync), because
        // that node tree, not the SectionData, is what the composition mapper serialises.
        if (section is SectionData structuralSection)
        {
            compositionMapper.PopulateSection(structuralSection, new SectionNode());
        }

        // RequiredAccess must be set explicitly — the simple 3-arg CreateAsync overload defaults
        // to requiring Publish access, which this background execution context does not have.
        var blueprint = await blueprintRepository.CreateAsync(
            section,
            new CreateBlueprintOptions { Name = sectionDef.Id, RequiredAccess = AccessLevel.NoAccess },
            ct).ConfigureAwait(false);

        var trackRecord = new ContentSeederRecord
        {
            Id = Guid.NewGuid(),
            SeedDefinitionId = seedDefinitionId,
            BlockDefinitionId = sectionDef.Id,
            ContentGuid = blueprint.ID,
            ContentId = 0, // blueprints are Guid-keyed, not ContentId-keyed
            ContentType = ContentSeederRecord.ContentTypes.SectionBlueprint,
            Language = language,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            SeedRunId = seedRunId,
            IsPartial = false
        };
        await seedTracker.InsertAsync(trackRecord, ct).ConfigureAwait(false);

        return blueprint.ID;
    }

    // Builds a structural section's rows/columns/elements directly onto the SectionNode that
    // Composition.AddSection returned. This must be the node tree itself: when the Experience is
    // populated, ICompositionMapper rebuilds an inline section's Layout/UnstructuredData from
    // SectionNode.Nodes, so anything written to the SectionData beforehand (e.g. via
    // PopulateSection) is overwritten by the empty node. Runs once per placement, so every
    // Experience gets its own freshly generated elements.
    //
    // Three steps, so the slow part (generating each element's content, one AI request apiece) can run
    // in parallel without touching the CMS from several threads at once:
    //   1. walk the definition, creating rows/columns and a blank element for each slot;
    //   2. generate every element's content, MaxParallelGeneration at a time;
    //   3. turn each element into an inline block and attach it, in order.
    /// <summary>
    /// Builds a section's rows, columns and elements on a placement's section node. Element content is generated in parallel, then the elements are attached in order.
    /// </summary>
    internal async Task BuildSectionRowsAsync(
        SectionNode sectionNode,
        SectionDefinition sectionDef,
        string language,
        CancellationToken ct)
    {
        var jobs = new List<ElementJob>();

        var planner = new RowPlanner(this, sectionNode, jobs);
        foreach (var source in sectionDef.Rows)
        {
            ct.ThrowIfCancellationRequested();
            source.Accept(planner);
        }

        logger.LogInformation("Generating {Count} elements for section '{Section}' ({Language}), up to {Parallel} at a time…", jobs.Count, sectionDef.Id, language, runner.Parallelism);
        var generating = System.Diagnostics.Stopwatch.StartNew();
        await runner.RunAsync(
            jobs.Select<ElementJob, Func<CancellationToken, Task>>(job => token => PopulateElementAsync(job, language, token)),
            ct).ConfigureAwait(false);
        logger.LogInformation("Generated {Count} elements for section '{Section}' in {Seconds:0.0}s.", jobs.Count, sectionDef.Id, generating.Elapsed.TotalSeconds);

        foreach (var job in jobs)
        {
            ct.ThrowIfCancellationRequested();
            var element = await MaterializeInlineElementAsync(job, ct).ConfigureAwait(false);
            var componentNode = AddComponentNode(job.Column, element, job.Name);
            ApplyStyles(componentNode, job.DisplayTemplateKey, job.DisplaySettings);
        }
    }

    // One element of a structural section, between being planned and being attached to its column.
    private sealed record ElementJob(
        StructureNode Column,
        Type ElementType,
        ContentType ContentType,
        IContentData Source,
        IReadOnlyDictionary<string, (int Width, int Height)>? ImageSizes,
        Action<object, int>? Configure,
        int ConfigureIndex,
        string? Name,
        string? DisplayTemplateKey,
        IReadOnlyDictionary<string, string>? DisplaySettings,
        IReadOnlyDictionary<string, VideoSource> Videos,
        ElementJob? CloneOf = null,
        string? Instance = null)
    {
        // The finished inline element; set once it has been materialized, so later copies can be made from it.
        /// <summary>
        /// The finished inline element, set once it has been materialized. Later copies are made from it.
        /// </summary>
        public BlockData? Element { get; set; }
    }

    private ElementJob NewElementJob(
        StructureNode column,
        Type elementType,
        IReadOnlyDictionary<string, (int Width, int Height)>? imageSizes,
        Action<object, int>? configure,
        int configureIndex,
        string? name,
        string? displayTemplateKey,
        IReadOnlyDictionary<string, string>? displaySettings,
        IReadOnlyDictionary<string, VideoSource> videos,
        ElementJob? cloneOf = null,
        string? instance = null)
    {
        // A copy needs no content of its own: it is cloned from the element it copies once that exists.
        if (cloneOf is not null)
            return new ElementJob(column, elementType, cloneOf.ContentType, cloneOf.Source, imageSizes, configure, configureIndex, name, displayTemplateKey, displaySettings, videos, cloneOf, instance);

        var elementContentType = contentTypeRepository.Load(elementType)
            ?? throw new InvalidOperationException(
                $"ContentSeeder: No content type registered for element type '{elementType.FullName}'. " +
                "Ensure the type has a [ContentType] attribute and the application has initialised.");

        var elementSource = (IContentData)_getDefaultOneParam
            .MakeGenericMethod(elementType)
            .Invoke(contentRepository, [ContentReference.GlobalBlockFolder])!;

        return new ElementJob(
            column, elementType, elementContentType, elementSource, imageSizes, configure, configureIndex,
            name, displayTemplateKey, displaySettings, videos, null, instance);
    }

    // Fills an element with generated content. Safe to run for several elements at once.
    private async Task PopulateElementAsync(ElementJob job, string language, CancellationToken ct)
    {
        if (job.CloneOf is not null)
            return;

        // Each element job sets its own name, so parallel jobs cannot see one another's.
        using var _ = SeedInstanceScope.Enter(job.Instance);

        var elProps = GetContentProperties(job.ElementType).ToList();
        await contentGenerationProvider.PopulateAsync(job.Source, elProps, language, job.ElementType, job.ImageSizes, ct).ConfigureAwait(false);
        await videos.ApplyVideosAsync(job.Source, job.Videos, ct).ConfigureAwait(false);
        job.Configure?.Invoke(job.Source, job.ConfigureIndex);

        // Defensive: always restore ContentTypeID — providers must never overwrite it.
        ((IContent)job.Source).ContentTypeID = job.ContentType.ID;
    }

    // Turns a populated element into a fresh, identity-free BlockData instance. A plain
    // GetDefault(ContentReference) instance implements IContent (it has "its own identity" —
    // ParentLink, ContentGuid, etc.), which ContentAreaItem.InlineBlock explicitly rejects with
    // "It is not supported to assign IContent instances as InlineBlock." Routing it through a
    // throwaway Blueprint + CreateInlineBlockAsync produces a properly initialized, identity-free
    // BlockData instead; the throwaway blueprint is deleted immediately since it's never reused.
    private async Task<BlockData> MaterializeInlineElementAsync(ElementJob job, CancellationToken ct)
    {
        if (job.CloneOf is { Element: { } original })
        {
            var copy = (BlockData)original.CreateWritableClone();
            job.Configure?.Invoke(copy, job.ConfigureIndex);
            return job.Element = copy;
        }

        var elementBlueprint = await blueprintRepository.CreateAsync(
            job.Source,
            new CreateBlueprintOptions { Name = $"element-{job.ElementType.Name}-{Guid.NewGuid():N}", RequiredAccess = AccessLevel.NoAccess },
            ct).ConfigureAwait(false);

        var element = await blueprintContentFactory.CreateInlineBlockAsync(
            elementBlueprint.ID,
            ContentReference.GlobalBlockFolder,
            new CreateBlueprintInlineBlockOptions { RequiredBlueprintAccess = AccessLevel.NoAccess, RequiredDestinationAccess = AccessLevel.NoAccess },
            ct).ConfigureAwait(false);

        // The blueprint was only a stepping stone to materialize one inline instance —
        // clean it up immediately rather than accumulating one per seeded element.
        await blueprintRepository.DeleteAsync(
            elementBlueprint.ID,
            new DeleteBlueprintOptions { RequiredAccess = AccessLevel.NoAccess },
            ct).ConfigureAwait(false);

        return job.Element = element;
    }

    // The named rule set of each inline element of a section, in the order the rows and columns are built. It walks the rows
    // the same way RowPlanner does, so the list lines up with the elements in the master composition.
    internal IReadOnlyList<string?> PlanElementInstances(SectionDefinition sectionDef)
    {
        var instances = new List<string?>();
        foreach (var source in sectionDef.Rows)
        {
            if (source is RowDefinition row)
            {
                foreach (var column in row.Columns)
                    foreach (var element in column.Elements)
                        instances.Add(element.Instance);
            }
            else if (source is RepeatingElementDefinition repeating)
            {
                for (int columnCount = repeating.MinColumns; columnCount <= repeating.MaxColumns; columnCount++)
                    for (int c = 1; c <= columnCount; c++)
                        instances.Add(repeating.Instance);
            }
        }
        return instances;
    }

    // Plans the rows of one section: each row source adds its rows, columns and element jobs to the section node.
    private sealed class RowPlanner(SectionSeeder seeder, SectionNode sectionNode, List<ElementJob> jobs) : ISectionRowSourceVisitor
    {
        /// <inheritdoc/>
        public void Visit(RowDefinition rowDef)
        {
            var row = AddRowNode(sectionNode, rowDef.Name);
            ApplyStyles(row, rowDef.DisplayTemplateKey, rowDef.DisplaySettings);

            foreach (var colDef in rowDef.Columns)
            {
                var col = AddColumnNode(row, colDef.Name);
                ApplyStyles(col, colDef.DisplayTemplateKey, colDef.DisplaySettings);

                foreach (var elDef in colDef.Elements)
                {
                    jobs.Add(seeder.NewElementJob(
                        col, elDef.Type, elDef.ImageSizes, elDef.Configure, 0,
                        elDef.Name, elDef.DisplayTemplateKey, elDef.DisplaySettings, elDef.Videos,
                        instance: elDef.Instance));
                }
            }
        }

        /// <inheritdoc/>
        public void Visit(RepeatingElementDefinition repeating)
        {
            int elementIndex = 0;
            ElementJob? firstOfRepeating = null;
            int rowNumber = 0;
            for (int columnCount = repeating.MinColumns; columnCount <= repeating.MaxColumns; columnCount++)
            {
                rowNumber++;
                var row = AddRowNode(sectionNode, null);

                for (int c = 1; c <= columnCount; c++)
                {
                    var col = AddColumnNode(row, null);
                    string? name = repeating.Name is null ? null : $"{repeating.Name} {rowNumber}.{c}";
                    var job = seeder.NewElementJob(
                        col, repeating.Type, repeating.ImageSizes, repeating.Configure, elementIndex++,
                        name, repeating.DisplayTemplateKey, repeating.DisplaySettings, repeating.Videos,
                        cloneOf: repeating.ReuseContent ? firstOfRepeating : null, instance: repeating.Instance);
                    firstOfRepeating ??= job;
                    jobs.Add(job);
                }
            }
        }
    }
}

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
/// Orchestrates full seed runs, language branching, partial resume, delete, and dry-run.
/// Scoped lifetime — resolved fresh per operation via IServiceScopeFactory.
/// </summary>
internal sealed class SeedOrchestrator(
    IContentRepository contentRepository,
    IContentLoader contentLoader,
    IContentTypeRepository contentTypeRepository,
    ISeedTracker seedTracker,
    IContentGenerationProvider contentGenerationProvider,
    IImageContentFactory imageContentFactory,
    ISectionSeeder sections,
    IOptions<ContentSeederOptions> options,
    IEnumerable<ISeedDefinition> seedDefinitions,
    ILogger<SeedOrchestrator> logger,
    ISeedDefinitionAccessor definitionAccessor,
    IVideoProvider videoProvider) : ISeedOrchestrator
{
    private readonly ContentSeederOptions _options = options.Value;
    private readonly VideoApplier _videos = new(videoProvider, logger);
    // Shared by the block seeder and the deleter, so a block folder the deleter removes is not still cached here.
    private readonly SeededFolders _seededFolders = new(contentRepository, contentLoader);

    private ContentDeleter? _deleter;

    private ContentDeleter Deleter => _deleter ??= new ContentDeleter(
        contentRepository, contentLoader, sections, seedTracker, imageContentFactory, seedDefinitions,
        _seededFolders, _options, logger);
    private readonly DefinitionFingerprints _fingerprints = new(seedTracker);

    private LanguageTranslator? _languageTranslator;

    private LanguageTranslator Translator => _languageTranslator ??= new LanguageTranslator(
        contentLoader, contentGenerationProvider, logger);

    private readonly SaveActions _saves = new(logger);

    private BlockSeeder? _blockSeeder;

    private BlockSeeder Blocks => _blockSeeder ??= new BlockSeeder(
        contentRepository, contentTypeRepository, contentGenerationProvider, seedTracker, _saves, _videos, Translator,
        _seededFolders, _options, logger);

    private PageSeeder? _pageSeeder;

    private PageSeeder Pages => _pageSeeder ??= new PageSeeder(
        contentRepository, contentLoader, contentTypeRepository, seedTracker, contentGenerationProvider,
        _saves, _videos, Translator, sections, logger);

    // ── ISeedOrchestrator ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<string> DefinitionIds => seedDefinitions.Select(d => d.Id).ToList();

    /// <inheritdoc/>
    public Task SeedAsync(CancellationToken ct = default) => SeedDefinitionsAsync(seedDefinitions, ct);

    /// <inheritdoc/>
    public Task SeedAsync(string seedDefinitionId, CancellationToken ct = default)
        => SeedDefinitionsAsync(Select(seedDefinitionId), ct);

    /// <inheritdoc/>
    public async Task ReseedAsync(CancellationToken ct = default)
    {
        await DeleteAllAsync(ct).ConfigureAwait(false);
        await SeedAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ReseedAsync(string seedDefinitionId, CancellationToken ct = default)
    {
        var definitions = Select(seedDefinitionId);
        await DeleteAllAsync(seedDefinitionId, ct).ConfigureAwait(false);
        await SeedDefinitionsAsync(definitions, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task DeleteAllAsync(CancellationToken ct = default) => Deleter.DeleteAllAsync(null, ct);

    /// <inheritdoc/>
    public Task DeleteAllAsync(string seedDefinitionId, CancellationToken ct = default)
        => Deleter.DeleteAllAsync(seedDefinitionId, ct);

    private async Task SeedDefinitionsAsync(IEnumerable<ISeedDefinition> definitions, CancellationToken ct)
    {
        var seedRunId = Guid.NewGuid();
        logger.LogInformation("Starting seed run {SeedRunId}.", seedRunId);

        foreach (var definition in definitions)
        {
            ct.ThrowIfCancellationRequested();
            await SeedDefinitionAsync(definition, seedRunId, ct).ConfigureAwait(false);
        }

        logger.LogInformation("Seed run {SeedRunId} complete.", seedRunId);
    }

    // Resolves one definition by id. Throws, rather than doing nothing, when the id is unknown.
    private List<ISeedDefinition> Select(string seedDefinitionId)
    {
        var found = seedDefinitions.Where(d => d.Id == seedDefinitionId).ToList();
        if (found.Count == 0)
            throw new ArgumentException($"No seed definition has the id '{seedDefinitionId}'.", nameof(seedDefinitionId));

        return found;
    }
    /// <inheritdoc/>
    public Task<DryRunResult> DryRunAsync(CancellationToken ct = default) => DryRunDefinitionsAsync(seedDefinitions, ct);

    /// <inheritdoc/>
    public Task<DryRunResult> DryRunAsync(string seedDefinitionId, CancellationToken ct = default)
        => DryRunDefinitionsAsync(Select(seedDefinitionId), ct);

    private async Task<DryRunResult> DryRunDefinitionsAsync(IEnumerable<ISeedDefinition> definitions, CancellationToken ct)
    {
        var results = new List<DryRunDefinitionResult>();

        foreach (var definition in definitions)
        {
            ct.ThrowIfCancellationRequested();
            var tree = definition.Build();
            var existing = await seedTracker.GetByDefinitionAsync(definition.Id, ct).ConfigureAwait(false);
            int nonPartialCount = existing.Count(r => !r.IsPartial);

            var languages = SeedLanguages.Resolve(definition, _options);
            var plan = PlanKinds(tree, languages.All.Count);

            // Each kind is planned on its own, so a kind that is more than complete never offsets one that is missing.
            var kinds = new List<DryRunKindCount>();
            foreach (var (kind, planned, types) in plan)
            {
                int alreadySeeded = existing.Count(r => !r.IsPartial && types.Contains(r.ContentType));
                kinds.Add(new DryRunKindCount(kind, planned, alreadySeeded, Math.Max(0, planned - alreadySeeded)));
            }

            int wouldCreate = kinds.Sum(k => k.WouldCreate);
            bool changed = await _fingerprints.HasDefinitionChangedAsync(definition.Id, tree, languages, ct).ConfigureAwait(false);
            bool hasPartialRun = existing.Any(r => r.IsPartial);

            results.Add(new DryRunDefinitionResult(definition.Id, wouldCreate, nonPartialCount, changed,
                kinds, languages.All.ToList(), hasPartialRun));
        }

        return new DryRunResult([.. results]);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetChangedDefinitionsAsync(CancellationToken ct = default)
    {
        var changed = new List<string>();
        foreach (var definition in seedDefinitions)
        {
            ct.ThrowIfCancellationRequested();
            var languages = SeedLanguages.Resolve(definition, _options);
            if (await _fingerprints.HasDefinitionChangedAsync(definition.Id, definition.Build(), languages, ct).ConfigureAwait(false))
                changed.Add(definition.Id);
        }
        return changed;
    }

    // ── private: seed one definition ─────────────────────────────────────────

    private async Task SeedDefinitionAsync(
        ISeedDefinition definition,
        Guid seedRunId,
        CancellationToken ct)
    {
        var tree = definition.Build();
        tree.Validate();
        InstanceNames.Validate(tree, definition);

        // Content generation providers read definition-level settings (e.g. an AI writing brief) from here.
        definitionAccessor.Current = definition;

        var languages = SeedLanguages.Resolve(definition, _options);
        string masterLanguage = languages.Master;
        string[] additionalLanguages = languages.Additional;
        var rootPage = GetRootContentReference();

        // Load existing records once for resume decisions.
        var existingRecords = await seedTracker.GetByDefinitionAsync(definition.Id, ct).ConfigureAwait(false);

        // A fresh run builds everything from this exact shape, so record it now (before any content is
        // created, so an interrupted run that is later resumed keeps the shape it started with). Runs over
        // existing content don't re-record it: Seed only fills gaps, so a changed shape stays unapplied
        // and must keep showing as changed until a Reseed.
        if (existingRecords.Count == 0)
            await seedTracker.SetDefinitionFingerprintAsync(definition.Id, DefinitionFingerprints.ComputeFingerprint(tree, languages), ct).ConfigureAwait(false);

        // The pages that already exist, to be reused by the tree walk instead of created again.
        // Partial page records are intentionally NOT reused — they will be re-attempted.
        var completePagesQueue = BuildResumeIndex(existingRecords, masterLanguage);

        // 1. Seed shared blocks (master language, then language branches)
        var blockRefs = new Dictionary<string, ContentReference>(StringComparer.Ordinal);

        // Shared sections are seeded here too, after the blocks: each is content like a block, typed as a section instance.
        var blockSeeds = tree.Blocks.Select(block => (Block: block, Type: ContentSeederRecord.ContentTypes.Block))
            .Concat(sections.SharedSectionBlocks(tree).Select(block => (Block: block, Type: ContentSeederRecord.ContentTypes.SectionInstance)));

        foreach (var (blockDef, blockType) in blockSeeds)
        {
            ct.ThrowIfCancellationRequested();

            // Resolve master block: prefer existing complete record, then existing partial (reuse from IContentLoader), else create fresh.
            var existingMasterBlock = existingRecords
                .Where(r => r.BlockDefinitionId == blockDef.Id && r.Language == masterLanguage && r.ContentType == blockType)
                .OrderByDescending(r => r.IsPartial ? 0 : 1)
                .FirstOrDefault();

            ContentReference blockRef;
            if (existingMasterBlock is { IsPartial: false })
            {
                blockRef = new ContentReference(existingMasterBlock.ContentId);
                blockRefs[blockDef.Id] = blockRef;
                logger.LogDebug("Skipping existing block {BlockId}.", blockDef.Id);
            }
            else if (existingMasterBlock is { IsPartial: true, ContentId: > 0 })
            {
                // Partial block: resolve existing ContentReference and reuse without re-saving
                blockRef = new ContentReference(existingMasterBlock.ContentId);
                blockRefs[blockDef.Id] = blockRef;
                logger.LogDebug("Resuming partial block {BlockId} from ContentId {ContentId}.",
                    blockDef.Id, existingMasterBlock.ContentId);
            }
            else
            {
                blockRef = await Blocks.CreateBlockAsync(
                    blockDef, definition.Id, masterLanguage, seedRunId, ct, blockType).ConfigureAwait(false);
                blockRefs[blockDef.Id] = blockRef;
            }

            // Language branches for the block. A shared section gets a version in every language too, or pages in
            // other languages would render it as missing.
            foreach (string lang in additionalLanguages)
            {
                ct.ThrowIfCancellationRequested();
                var existingBranch = existingRecords
                    .FirstOrDefault(r => r.BlockDefinitionId == blockDef.Id && r.Language == lang && r.ContentType == blockType);
                if (existingBranch is { IsPartial: false })
                {
                    logger.LogDebug("Skipping existing block branch {BlockId}/{Language}.", blockDef.Id, lang);
                    continue;
                }

                await Blocks.CreateBlockLanguageBranchAsync(
                    blockDef, blockRef, definition.Id, masterLanguage, lang, seedRunId, ct, blockType).ConfigureAwait(false);
            }
        }

        // 2. Seed structural sections: a blueprint each, created once. Shared sections were seeded as blocks above.
        var sectionSet = await sections.SeedBlueprintsAsync(
            tree, definition.Id, masterLanguage, seedRunId, existingRecords, ct).ConfigureAwait(false);

        // 3. Seed page tree recursively
        await Pages.SeedNodesAsync(
            tree.Nodes, rootPage, definition.Id, masterLanguage, additionalLanguages,
            blockRefs, sectionSet, 0, seedRunId, completePagesQueue, ct).ConfigureAwait(false);
    }

    // ── private: resuming over existing content ──────────────────────────────

    // A record is only reusable if its page still exists. One that was deleted in the CMS is dropped here, so the
    // page is created again instead of being handed back as a reference to nothing.
    private ResumeIndex BuildResumeIndex(IReadOnlyList<ContentSeederRecord> records, string masterLanguage)
    {
        var index = new ResumeIndex();
        var culture = Cultures.Resolve(masterLanguage, logger);

        foreach (var record in records
                     .Where(r => r.ContentType == ContentSeederRecord.ContentTypes.Page
                                 && !r.IsPartial
                                 && string.Equals(r.Language, masterLanguage, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(r => r.CreatedUtc))
        {
            if (!contentLoader.TryGet<IContent>(new ContentReference(record.ContentId), culture, out var content))
            {
                logger.LogDebug("Page {ContentId} from an earlier run no longer exists and will be created again.", record.ContentId);
                continue;
            }

            index.Add(content.ParentLink.ID, content.ContentTypeID, record);
        }

        return index;
    }

    // Root nodes in a SeedTree are always created under ContentReference.RootPage (ID=1).
    // Using SiteDefinition.Current.StartPage here was a bug — on a fresh install,
    // SiteDefinition.Current can return a non-null site with an unexpected StartPage
    // (e.g. the waste basket) before any site definitions have been registered,
    // causing seeded pages to land in the wrong location.
    private static ContentReference GetRootContentReference()
        => ContentReference.RootPage;

    // The kinds of item a seed writes, with how many of each a full seed would make. Pages, blocks and shared sections each
    // have a version in every language, so they are planned per language. A structural section is a blueprint, created once
    // whatever the languages. Per-placement instances stamped from a blueprint are not counted: rows, columns and elements
    // baked into a section are never separately created content.
    private IEnumerable<(string Kind, int Planned, string[] Types)> PlanKinds(SeedTree tree, int languageCount)
    {
        var (structural, shared) = sections.CountSections(tree);
        int pages = CountNodes(tree.Nodes);
        int blocksAndShared = tree.Blocks.Count + shared;

        yield return (DryRunKinds.Pages, pages * languageCount, [ContentSeederRecord.ContentTypes.Page]);

        // A CMS without sections has only blocks, so a dry run does not mention sections at all.
        if (!sections.SupportsSections)
        {
            yield return (DryRunKinds.Blocks, tree.Blocks.Count * languageCount, [ContentSeederRecord.ContentTypes.Block]);
            yield break;
        }

        yield return (DryRunKinds.BlocksAndSharedSections, blocksAndShared * languageCount,
            [ContentSeederRecord.ContentTypes.Block, ContentSeederRecord.ContentTypes.SectionInstance]);
        yield return (DryRunKinds.SectionBlueprints, structural, [ContentSeederRecord.ContentTypes.SectionBlueprint]);
    }

    private static int CountNodes(IReadOnlyList<SeedNode> nodes)
    {
        int count = 0;
        foreach (var node in nodes)
        {
            count += node.Count;
            count += CountNodes(node.Children) * node.Count;
        }
        return count;
    }

}

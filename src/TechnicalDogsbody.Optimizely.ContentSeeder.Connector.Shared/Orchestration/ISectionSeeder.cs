using EPiServer.Core;
using EPiServer.DataAccess;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;

/// <summary>
/// The section steps of a seed run. A connector implements this for its CMS's section model, so the shared orchestrator
/// and page seeder never name one. A section is seeded in one of two ways: a structural section is created once as a
/// blueprint and placed inline on each page; a shared section is seeded as a block (see <see cref="SharedSectionBlocks"/>)
/// and referenced from every page.
/// </summary>
internal interface ISectionSeeder
{
    /// <summary>
    /// True when the CMS has sections. The admin UI hides everything about sections when this is false, and a dry run does not report them.
    /// </summary>
    bool SupportsSections { get; }

    /// <summary>The shared sections of a tree, as blocks. They are seeded like blocks, in the master language and each language branch.</summary>
    IReadOnlyList<BlockDefinition> SharedSectionBlocks(SeedTree tree);

    /// <summary>How many of a tree's sections are structural (blueprints) and how many are shared.</summary>
    (int Structural, int Shared) CountSections(SeedTree tree);

    /// <summary>
    /// Creates the blueprint of each structural section that has none yet, and returns the set the pages of the definition are seeded from.
    /// </summary>
    Task<ISectionSet> SeedBlueprintsAsync(
        SeedTree tree,
        string seedDefinitionId,
        string masterLanguage,
        Guid seedRunId,
        IReadOnlyList<ContentSeederRecord> existingRecords,
        CancellationToken ct);

    /// <summary>
    /// Places a page's sections on its master version. A structural section is built inline; a shared section is
    /// referenced by its block in <paramref name="blockRefs"/>. A node that declares no sections is left as it is.
    /// </summary>
    Task PlaceSectionsAsync(
        SeedNode node,
        ContentReference pageRef,
        IReadOnlyDictionary<string, ContentReference> blockRefs,
        ISectionSet sections,
        string language,
        SaveAction pageAction,
        CancellationToken ct);

    /// <summary>Copies a page's layout into one language version. A page without a layout is left as it is.</summary>
    Task CopyLayoutToBranchAsync(
        SeedNode node,
        PageData branch,
        ContentReference masterRef,
        ISectionSet sections,
        string masterLanguage,
        string language,
        CancellationToken ct);

    /// <summary>Deletes a structural section's blueprint. Blueprints are keyed by Guid, not by ContentReference.</summary>
    Task DeleteBlueprintAsync(Guid blueprintId, CancellationToken ct);
}

/// <summary>
/// The blueprints and section references a connector resolved for one seed definition. The shared code only passes it
/// back to the <see cref="ISectionSeeder"/> that created it, so its contents belong to the connector.
/// </summary>
internal interface ISectionSet;

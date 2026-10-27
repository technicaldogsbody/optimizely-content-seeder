using EPiServer.Core;
using EPiServer.DataAccess;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;

/// <summary>
/// The <see cref="ISectionSeeder"/> for a CMS without sections, such as Optimizely CMS 12. A tree has none, so every step
/// does nothing. A connector that supports sections registers its own seeder, which takes the place of this one.
/// </summary>
internal sealed class NoSectionSeeder : ISectionSeeder
{
    /// <inheritdoc/>
    public bool SupportsSections => false;

    private sealed class NoSections : ISectionSet;

    private static readonly NoSections _none = new();

    /// <inheritdoc/>
    public IReadOnlyList<BlockDefinition> SharedSectionBlocks(SeedTree tree) => [];

    /// <inheritdoc/>
    public (int Structural, int Shared) CountSections(SeedTree tree) => (0, 0);

    /// <inheritdoc/>
    public Task<ISectionSet> SeedBlueprintsAsync(
        SeedTree tree,
        string seedDefinitionId,
        string masterLanguage,
        Guid seedRunId,
        IReadOnlyList<ContentSeederRecord> existingRecords,
        CancellationToken ct)
        => Task.FromResult<ISectionSet>(_none);

    /// <inheritdoc/>
    public Task PlaceSectionsAsync(
        SeedNode node,
        ContentReference pageRef,
        IReadOnlyDictionary<string, ContentReference> blockRefs,
        ISectionSet sections,
        string language,
        SaveAction pageAction,
        CancellationToken ct)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task CopyLayoutToBranchAsync(
        SeedNode node,
        PageData branch,
        ContentReference masterRef,
        ISectionSet sections,
        string masterLanguage,
        string language,
        CancellationToken ct)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task DeleteBlueprintAsync(Guid blueprintId, CancellationToken ct) => Task.CompletedTask;
}

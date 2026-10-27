using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Orchestration;

/// <summary>
/// The section seeder for tests that declare no Visual Builder sections. It creates nothing and places nothing.
/// </summary>
internal sealed class NoSectionSeeder : ISectionSeeder
{
    // These tests stand in for a CMS that has sections but a tree that declares none, so a dry run still reports the section kinds.
    public bool SupportsSections => true;

    // A tree that declares sections needs the real seeder, or the sections would be skipped without a sign.
    public IReadOnlyList<BlockDefinition> SharedSectionBlocks(SeedTree tree)
    {
        Refuse(tree);
        return [];
    }

    public (int Structural, int Shared) CountSections(SeedTree tree)
    {
        Refuse(tree);
        return (0, 0);
    }

    private static void Refuse(SeedTree tree)
    {
        if (tree.SectionsOf().Count > 0)
            throw new InvalidOperationException("The test declares sections, so it needs a TestSectionSeeder rather than NoSectionSeeder.");
    }

    public Task<ISectionSet> SeedBlueprintsAsync(
        SeedTree tree, string seedDefinitionId, string masterLanguage, Guid seedRunId,
        IReadOnlyList<ContentSeederRecord> existingRecords, CancellationToken ct)
        => Task.FromResult<ISectionSet>(new NoSections());

    public Task PlaceSectionsAsync(
        SeedNode node, EPiServer.Core.ContentReference pageRef, IReadOnlyDictionary<string, EPiServer.Core.ContentReference> blockRefs,
        ISectionSet sections, string language, EPiServer.DataAccess.SaveAction pageAction, CancellationToken ct)
        => Task.CompletedTask;

    public Task CopyLayoutToBranchAsync(
        SeedNode node, EPiServer.Core.PageData branch, EPiServer.Core.ContentReference masterRef,
        ISectionSet sections, string masterLanguage, string language, CancellationToken ct)
        => Task.CompletedTask;

    public Task DeleteBlueprintAsync(Guid blueprintId, CancellationToken ct) => Task.CompletedTask;

    private sealed record NoSections : ISectionSet;
}

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>Per-definition breakdown from a DryRunAsync call.</summary>
public sealed class DryRunDefinitionResult
{
    /// <summary>The ISeedDefinition.Id.</summary>
    public string SeedDefinitionId { get; }

    /// <summary>Content + language items that would be created (not yet tracked as non-partial).</summary>
    public int WouldCreate { get; }

    /// <summary>Content + language items already tracked as non-partial (would be skipped).</summary>
    public int WouldSkip { get; }

    /// <summary>
    /// True when the definition's shape has changed since its content was seeded. Seed won't apply the
    /// change to existing content, so the counts above don't reflect it; a Reseed is needed.
    /// </summary>
    public bool DefinitionChanged { get; }

    /// <summary>The figures for each kind of item: pages, blocks and shared sections, and section blueprints.</summary>
    public IReadOnlyList<DryRunKindCount> Kinds { get; }

    /// <summary>The languages a seed would write, the master language first.</summary>
    public IReadOnlyList<string> Languages { get; }

    /// <summary>True when an earlier run of this definition was interrupted and has unfinished items.</summary>
    public bool HasPartialRun { get; }

    /// <summary>
    /// The dry-run outcome for one definition: how many items would be created, how many already exist, and whether its shape has changed.
    /// </summary>
    public DryRunDefinitionResult(
        string seedDefinitionId,
        int wouldCreate,
        int wouldSkip,
        bool definitionChanged = false,
        IReadOnlyList<DryRunKindCount>? kinds = null,
        IReadOnlyList<string>? languages = null,
        bool hasPartialRun = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seedDefinitionId);
        SeedDefinitionId = seedDefinitionId;
        WouldCreate = wouldCreate;
        WouldSkip = wouldSkip;
        DefinitionChanged = definitionChanged;
        Kinds = kinds ?? [];
        Languages = languages ?? [];
        HasPartialRun = hasPartialRun;
    }
}

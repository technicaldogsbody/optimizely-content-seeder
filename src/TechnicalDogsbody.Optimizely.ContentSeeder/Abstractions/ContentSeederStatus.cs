using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
namespace TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;


/// <summary>Which providers are generating the seeded content.</summary>
public sealed record SeederProviders(ProviderInfo Text, ProviderInfo Images);

/// <summary>What has been seeded for one seed definition.</summary>
/// <param name="SeedDefinitionId">The definition's id.</param>
/// <param name="PageCount">Complete page records created by the definition.</param>
/// <param name="BlockCount">Complete block records created by the definition.</param>
/// <param name="SectionCount">Complete section records (blueprints and shared sections) created by the definition.</param>
/// <param name="LastSeededUtc">When the definition's most recent complete record was updated. Null if none is complete.</param>
/// <param name="HasPartialRun">True when a run of this definition was interrupted and is not finished.</param>
/// <param name="IsChanged">True when the definition's shape has changed since it was seeded, so a reseed is needed.</param>
public sealed record SeedDefinitionStatus(
    string SeedDefinitionId,
    int PageCount,
    int BlockCount,
    int SectionCount,
    DateTime? LastSeededUtc,
    bool HasPartialRun,
    bool IsChanged = false);

/// <summary>
/// Response DTO returned by GET /api/contentseeder/status.
/// </summary>
public sealed record ContentSeederStatus
{
    /// <summary>
    /// Every seed definition, with what has been seeded for it. A definition that has never been seeded still appears, with zero counts.
    /// </summary>
    public IReadOnlyList<SeedDefinitionStatus> Definitions { get; init; } = [];

    /// <summary>Count of non-partial page records in ContentSeederRecord.</summary>
    public int TrackedPageCount { get; init; }

    /// <summary>Count of non-partial block records in ContentSeederRecord.</summary>
    public int TrackedBlockCount { get; init; }

    /// <summary>Count of non-partial Visual Builder section records (blueprints and shared sections).</summary>
    public int TrackedSectionCount { get; init; }

    /// <summary>MAX(UpdatedUtc) across all non-partial records. Null if nothing seeded.</summary>
    public DateTime? LastSeededUtc { get; init; }

    /// <summary>Current ContentSeederOptions.SeedOnStartup value (read-only display).</summary>
    public bool SeedOnStartup { get; init; }

    /// <summary>
    /// False when the CMS has no sections (Optimizely CMS 12). The admin UI then hides sections, and the dry run does not report them.
    /// Set by the controller, not the tracker.
    /// </summary>
    public bool SupportsSections { get; init; } = true;

    /// <summary>MasterLanguage + Languages from options. Empty in single-language mode.</summary>
    public string[] Languages { get; init; } = [];

    /// <summary>True if any IsPartial=1 records exist (enables resume indicator in UI).</summary>
    public bool HasPartialRun { get; init; }

    /// <summary>
    /// Seed definitions whose shape has changed since their content was seeded (Reseed to apply).
    /// Set by the controller, not the tracker.
    /// </summary>
    public string[] ChangedDefinitions { get; init; } = [];

    /// <summary>The providers generating text and images (Bogus, Ollama, Picsum…). Set by the controller, not the tracker.</summary>
    public SeederProviders Providers { get; init; } = new(new ProviderInfo("Unknown"), new ProviderInfo("Unknown"));

    /// <summary>The current or most recent background operation. Set by the controller, not the tracker.</summary>
    public SeedOperationSnapshot Operation { get; init; } = SeedOperationSnapshot.Idle;
}

/// <summary>
/// Point-in-time view of the current (or most recent) seed/reseed/delete operation,
/// returned to the admin UI so it can poll until a run finishes.
/// </summary>
public sealed record SeedOperationSnapshot(
    string? Name,
    bool IsRunning,
    DateTime? StartedUtc,
    DateTime? CompletedUtc,
    bool? Succeeded,
    string? Error)
{
    /// <summary>
    /// The snapshot reported before any operation has run.
    /// </summary>
    public static SeedOperationSnapshot Idle { get; } = new(null, false, null, null, null, null);
}

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Result of a DryRunAsync call. Contains per-definition would-create / would-skip counts.
/// </summary>
public sealed class DryRunResult
{
    /// <summary>
    /// The dry-run outcome for each seed definition.
    /// </summary>
    public IReadOnlyList<DryRunDefinitionResult> Definitions { get; }

    /// <summary>
    /// The dry-run outcome for a set of seed definitions.
    /// </summary>
    public DryRunResult(IReadOnlyList<DryRunDefinitionResult> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        Definitions = definitions;
    }
}

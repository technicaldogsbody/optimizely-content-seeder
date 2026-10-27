using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

/// <summary>
/// Exposes the seed definition currently being seeded within a scope (like IHttpContextAccessor),
/// so content generation providers can read definition-level settings such as an
/// <see cref="IAiSeedDefinition"/> writing brief without changing
/// <see cref="IContentGenerationProvider"/>. Set by the orchestrator. Scoped lifetime.
/// </summary>
public interface ISeedDefinitionAccessor
{
    /// <summary>
    /// The seed definition being written in the current scope, or null outside a seed run. Content generation providers read definition-level settings from it.
    /// </summary>
    ISeedDefinition? Current { get; set; }
}

/// <summary>
/// The default accessor: a plain holder for the definition currently being seeded.
/// </summary>
internal sealed class SeedDefinitionAccessor : ISeedDefinitionAccessor
{
    /// <inheritdoc/>
    public ISeedDefinition? Current { get; set; }
}

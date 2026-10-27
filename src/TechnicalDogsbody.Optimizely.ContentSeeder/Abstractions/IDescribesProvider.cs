namespace TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

/// <summary>What a provider is, for display in the admin UI.</summary>
/// <param name="Name">Short name, e.g. "Bogus", "Ollama" or "Picsum Photos".</param>
/// <param name="Detail">Extra detail, e.g. the model name. Optional.</param>
/// <param name="IsAi">True when the content is AI-generated rather than placeholder data.</param>
public sealed record ProviderInfo(string Name, string? Detail = null, bool IsAi = false);

/// <summary>
/// Optional interface for an <see cref="IContentGenerationProvider"/> or <see cref="IImageContentFactory"/>
/// so the admin UI can show which one is in use. Providers that don't implement it are shown by type name.
/// </summary>
public interface IDescribesProvider
{
    /// <summary>Describes the provider for display in the admin UI.</summary>
    ProviderInfo Describe();
}

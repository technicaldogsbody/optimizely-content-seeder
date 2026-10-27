using Microsoft.Extensions.Logging;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

/// <summary>
/// Used when no video provider is registered. It resolves no videos, so declared videos are skipped, and it
/// warns once that this has happened.
/// </summary>
internal sealed class NullVideoProvider(ILogger logger) : IVideoProvider
{
    private int _warned;

    /// <inheritdoc/>
    public Task<ResolvedVideo?> ResolveAsync(VideoSource source, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0)
            logger.LogWarning("ContentSeeder: A definition declares videos but no IVideoProvider is registered. Videos are skipped.");

        return Task.FromResult<ResolvedVideo?>(null);
    }
}

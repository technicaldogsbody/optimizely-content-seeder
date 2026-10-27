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

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;

/// <summary>
/// Runs content generation work in parallel. Only an AI provider runs more than one item at a time.
/// </summary>
internal sealed class GenerationRunner(IContentGenerationProvider contentGenerationProvider, ContentSeederOptions options)
{
    // How many items to generate at once. Only an AI provider has anything to gain from running in
    // parallel (each item is a slow request); the default Bogus provider is instant, so it stays serial.
    /// <summary>
    /// How many items may be generated at once. Only an AI provider runs more than one at a time.
    /// </summary>
    internal int Parallelism
        => contentGenerationProvider is IDescribesProvider { } describes && describes.Describe().IsAi
            ? Math.Max(1, options.MaxParallelGeneration)
            : 1;

    /// <summary>
    /// Runs the work items, with up to Parallelism of them in progress at once.
    /// </summary>
    internal Task RunAsync(IEnumerable<Func<CancellationToken, Task>> work, CancellationToken ct)
        => Parallel.ForEachAsync(
            work,
            new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct },
            async (job, token) => await job(token).ConfigureAwait(false));
}

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
/// Works out whether a seed definition's shape has changed since it was first seeded.
/// </summary>
internal sealed class DefinitionFingerprints(ISeedTracker seedTracker)
{
    /// <summary>
    /// The fingerprint of a tree, including the languages the seed is written in.
    /// </summary>
    internal static string ComputeFingerprint(SeedTree tree, SeedLanguages languages)
        => SeedTreeFingerprint.Compute(tree, languages.Master, languages.Additional);

    // No recorded fingerprint means "unknown" (never seeded, or seeded before fingerprints existed),
    // which is reported as unchanged rather than raising a false alarm.
    /// <summary>
    /// True when a definition has a recorded fingerprint that differs from its current tree. A definition with no recorded fingerprint is reported as unchanged.
    /// </summary>
    internal async Task<bool> HasDefinitionChangedAsync(string definitionId, SeedTree tree, SeedLanguages languages, CancellationToken ct)
    {
        string? recorded = await seedTracker.GetDefinitionFingerprintAsync(definitionId, ct).ConfigureAwait(false);
        return recorded is not null && !string.Equals(recorded, ComputeFingerprint(tree, languages), StringComparison.Ordinal);
    }
}

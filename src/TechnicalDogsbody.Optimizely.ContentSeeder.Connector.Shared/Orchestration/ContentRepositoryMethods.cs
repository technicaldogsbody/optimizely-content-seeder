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
/// Reflection handles for the generic <see cref="IContentRepository"/> methods, which can only be called
/// with a runtime <see cref="Type"/> through reflection.
/// </summary>
internal static class ContentRepositoryMethods
{
    // ── Cached reflection ─────────────────────────────────────────────────────
    // IContentRepository.GetDefault<T> and CreateLanguageBranch<T> are generic methods
    // resolved by LINQ over GetMethods(). Caching the open MethodInfo avoids re-running
    // that search on every block and page creation during a seed run.

    /// <summary>
    /// The GetDefault overload that takes a parent reference. Found once by reflection, because the content type is only known at run time.
    /// </summary>
    internal static readonly MethodInfo _getDefaultOneParam =
        typeof(IContentRepository)
            .GetMethods()
            .First(m => m.Name == nameof(IContentRepository.GetDefault)
                        && m.IsGenericMethodDefinition
                        && m.GetParameters() is { Length: 1 });

    /// <summary>
    /// The GetDefault overload that takes a parent reference and a culture. Found once by reflection, for the same reason.
    /// </summary>
    internal static readonly MethodInfo _getDefaultTwoParam =
        typeof(IContentRepository)
            .GetMethods()
            .First(m => m.Name == nameof(IContentRepository.GetDefault)
                        && m.IsGenericMethodDefinition
                        && m.GetParameters() is { Length: 2 } p
                        && p[1].ParameterType == typeof(CultureInfo));

    /// <summary>
    /// The CreateLanguageBranch overload that takes a culture. Found once by reflection, for the same reason.
    /// </summary>
    internal static readonly MethodInfo _createLanguageBranch =
        typeof(IContentRepository)
            .GetMethods()
            .First(m => m.Name == nameof(IContentRepository.CreateLanguageBranch)
                        && m.IsGenericMethodDefinition
                        && m.GetParameters() is { Length: 2 } p
                        && p[1].ParameterType == typeof(CultureInfo));
}

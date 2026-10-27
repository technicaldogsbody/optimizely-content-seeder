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
using static TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration.ContentProperties;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;

/// <summary>
/// Fills language branches: translates master-language content where the provider can, and generates content for
/// providers that cannot translate. A connector copies its own layouts into the branches (see <see cref="ISectionSeeder"/>).
/// </summary>
internal sealed class LanguageTranslator(
    IContentLoader contentLoader,
    IContentGenerationProvider contentGenerationProvider,
    ILogger logger)
{
    // Fills a new language branch. A provider that can translate gets the saved master-language content
    // to translate, so every language version says the same thing; any other provider (or a master that
    // can't be loaded) generates content for the language from scratch, as before.
    /// <summary>
    /// Fills a language branch. A provider that can translate translates the saved master; any other provider generates content for the language.
    /// </summary>
    internal async Task PopulateBranchAsync(
        object branch,
        ContentReference masterRef,
        string masterLanguage,
        IReadOnlyList<PropertyInfo> props,
        string language,
        Type contentType,
        IReadOnlyDictionary<string, (int Width, int Height)>? imageSizes,
        IReadOnlyDictionary<string, VideoSource> videos,
        CancellationToken ct)
    {
        if (contentGenerationProvider is IContentTranslationProvider translator
            && contentLoader.TryGet<IContentData>(
                masterRef.ToReferenceWithoutVersion(), Cultures.Resolve(masterLanguage, logger), out var master))
        {
            await translator.TranslateAsync(master, branch, props, masterLanguage, language, contentType, ct).ConfigureAwait(false);
            VideoApplier.CopyVideoTitles(master, branch, videos);
            return;
        }

        await contentGenerationProvider.PopulateAsync(branch, props, language, contentType, imageSizes, ct).ConfigureAwait(false);

        if (videos.Count > 0
            && contentLoader.TryGet<IContentData>(masterRef.ToReferenceWithoutVersion(), Cultures.Resolve(masterLanguage, logger), out var master2))
        {
            VideoApplier.CopyVideoTitles(master2, branch, videos);
        }
    }


    // A definition's configure callback is written for the master language, but it also runs for every language version.
    // Setting a property that isn't [CultureSpecific] (a date, a price, an image…) on a language version makes EPiServer
    // refuse to save it ("Cannot save non-culture specific property"), and the value belongs to the master anyway.
    // So the callback runs against a scratch copy, and only what a language version can own is copied back: its
    // [CultureSpecific] properties and its name.
    /// <summary>
    /// Runs a definition's configure callback for a language version. Only what a language version may own is copied back: its culture-specific properties and its name.
    /// </summary>
    internal static void ConfigureLanguageVersion(object branch, Action<object, int>? configure, int index, Type type)
    {
        if (configure is null)
            return;

        object scratch = ((ContentData)branch).CreateWritableClone();
        configure(scratch, index);

        foreach (var prop in GetContentProperties(type))
        {
            if (prop.GetCustomAttribute<CultureSpecificAttribute>() is null || prop.PropertyType == typeof(ContentArea))
                continue;

            object? configured = prop.GetValue(scratch);
            object? current = prop.GetValue(branch);
            bool changed = configured is XhtmlString html
                ? !string.Equals(html.ToString(), (current as XhtmlString)?.ToString(), StringComparison.Ordinal)
                : !Equals(configured, current);

            if (changed)
                prop.SetValue(branch, configured);
        }

        if (scratch is IContent scratchContent && branch is IContent branchContent
            && !string.Equals(scratchContent.Name, branchContent.Name, StringComparison.Ordinal))
        {
            branchContent.Name = scratchContent.Name;
        }
    }
}

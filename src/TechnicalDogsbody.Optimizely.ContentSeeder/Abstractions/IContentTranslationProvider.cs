using System.Reflection;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

/// <summary>
/// Optional capability of an <see cref="IContentGenerationProvider"/>: fill a language branch by
/// translating the master-language content instead of generating unrelated content from scratch.
/// The orchestrator uses it for additional languages when the registered provider implements it;
/// providers that don't (such as the default Bogus provider) are asked to generate per-language
/// content with <see cref="IContentGenerationProvider.PopulateAsync"/> as before.
/// </summary>
public interface IContentTranslationProvider
{
    /// <summary>
    /// Populates <paramref name="target"/> (a new language branch) from <paramref name="source"/>
    /// (the saved master-language content of the same item).
    /// </summary>
    /// <param name="source">The saved master-language content.</param>
    /// <param name="target">The writable language branch to fill.</param>
    /// <param name="properties">The properties that belong on this branch (language-specific ones for pages).</param>
    /// <param name="sourceLanguage">IETF tag of the master language, e.g. <c>en</c>.</param>
    /// <param name="targetLanguage">IETF tag of the language being created, e.g. <c>sv</c>.</param>
    /// <param name="contentType">The declared CLR type of the content (not the Castle proxy type).</param>
    /// <param name="ct">Cancellation token.</param>
    Task TranslateAsync(
        object source,
        object target,
        IEnumerable<PropertyInfo> properties,
        string sourceLanguage,
        string targetLanguage,
        Type? contentType = null,
        CancellationToken ct = default);
}

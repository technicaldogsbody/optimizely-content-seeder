using Bogus;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;

/// <summary>
/// Resolves Bogus generators for content property names and CLR types.
/// Implement this interface (or inherit from <see cref="DefaultBogusPropertyMap"/>)
/// to extend or replace the built-in heuristics.
/// Register a custom implementation via <c>builder.UseBogusPropertyMap&lt;T&gt;()</c>.
/// </summary>
public interface IBogusPropertyMap
{
    /// <summary>
    /// Returns a Faker generator for the given property name, or <c>null</c> if no match is found.
    /// </summary>
    /// <param name="propertyName">The property name to resolve (e.g. <c>"Title"</c>).</param>
    /// <param name="contentTypeName">
    /// Optional declared content type name (e.g. <c>"HeroBlock"</c>). When provided, a
    /// qualified key <c>"HeroBlock.Title"</c> is tried before the unqualified <c>"Title"</c> key,
    /// allowing per-content-type generator overrides.
    /// </param>
    /// <remarks>
    /// Resolution order:
    /// <list type="number">
    ///   <item>Qualified exact: <c>"{contentTypeName}.{propertyName}"</c></item>
    ///   <item>Unqualified exact: <c>"{propertyName}"</c></item>
    ///   <item>Unqualified suffix (qualified keys are excluded from suffix matching)</item>
    /// </list>
    /// </remarks>
    Func<Faker, object>? Resolve(string propertyName, string? contentTypeName = null);

    /// <summary>
    /// Returns a value generated purely from the CLR type.
    /// Used as the final fallback when <see cref="Resolve"/> finds no match.
    /// Returns <c>null</c> for types that are not supported.
    /// </summary>
    object? ResolveByType(Type type, Faker faker);
}

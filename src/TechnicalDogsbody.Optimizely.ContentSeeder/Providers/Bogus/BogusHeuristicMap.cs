using Bogus;
using Microsoft.Extensions.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;

/// <summary>
/// Static facade over <see cref="DefaultBogusPropertyMap"/> retained for test compatibility.
/// Production code should use <see cref="IBogusPropertyMap"/> via dependency injection.
/// </summary>
internal static class BogusHeuristicMap
{
    private static readonly IBogusPropertyMap _instance =
        new DefaultBogusPropertyMap(Microsoft.Extensions.Options.Options.Create(new BogusGeneratorMapOptions()));

    /// <summary>The built-in property name → generator dictionary.</summary>
    public static IReadOnlyDictionary<string, Func<Faker, object>> Map =>
        ((DefaultBogusPropertyMap)_instance).BuiltInMap;

    /// <summary>Resolves a generator by exact name then suffix match.</summary>
    public static Func<Faker, object>? Resolve(string propertyName, string? contentTypeName = null)
        => _instance.Resolve(propertyName, contentTypeName);

    /// <summary>Resolves a value from CLR type alone.</summary>
    public static object? ResolveByType(Type type, Faker faker)
        => _instance.ResolveByType(type, faker);
}

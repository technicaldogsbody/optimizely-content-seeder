using Bogus;
using Microsoft.Extensions.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;

/// <summary>
/// Default <see cref="IBogusPropertyMap"/> implementation.
/// Resolves generators by exact property name, then by suffix, then by CLR type.
/// <para>
/// To add generators for every definition without subclassing, configure <see cref="BogusGeneratorMapOptions"/>
/// via <c>builder.ExtendBogusPropertyMap(...)</c>. Additional entries are merged at startup
/// and override built-in entries with the same key. Rules for one definition go on <see cref="Definitions.ISeedDefinition.Generators"/>.
/// </para>
/// <para>
/// To replace the entire map, inherit from this class and override <see cref="BuiltInMap"/>,
/// or implement <see cref="IBogusPropertyMap"/> directly and register it via
/// <c>builder.UseBogusPropertyMap&lt;T&gt;()</c>.
/// </para>
/// </summary>
public class DefaultBogusPropertyMap : IBogusPropertyMap
{
    private readonly IReadOnlyDictionary<string, Func<Faker, object>> _effectiveMap;

    /// <summary>
    /// The built-in property-name map, extended with the entries in BogusGeneratorMapOptions.
    /// </summary>
    public DefaultBogusPropertyMap(IOptions<BogusGeneratorMapOptions> options)
    {
        var additional = options.Value.AdditionalGenerators;

        _effectiveMap = additional.Count == 0
            ? BuiltInMap
            : Merge(BuiltInMap, additional);
    }

    /// <summary>
    /// The built-in property name → generator dictionary.
    /// Override in a subclass to replace the default entries completely.
    /// </summary>
    public virtual IReadOnlyDictionary<string, Func<Faker, object>> BuiltInMap { get; } =
        new Dictionary<string, Func<Faker, object>>(StringComparer.OrdinalIgnoreCase)
        {
            // Text — Title / Heading
            ["Title"]        = f => f.Lorem.Sentence(4),
            ["Heading"]      = f => f.Lorem.Sentence(4),
            ["SubHeading"]   = f => f.Lorem.Sentence(4),
            ["Name"]         = f => f.Lorem.Sentence(4),
            ["Label"]        = f => f.Lorem.Sentence(4),

            // Text — Body / Introduction
            ["Introduction"] = f => f.Lorem.Paragraph(),
            ["Summary"]      = f => f.Lorem.Paragraph(),
            ["Description"]  = f => f.Lorem.Paragraph(),
            ["Body"]         = f => f.Lorem.Paragraph(),
            ["Excerpt"]      = f => f.Lorem.Paragraph(),
            ["Lead"]         = f => f.Lorem.Paragraph(),

            // Person
            ["Author"]       = f => f.Name.FullName(),
            ["ByLine"]       = f => f.Name.FullName(),
            ["FullName"]     = f => f.Name.FullName(),

            // Contact
            ["Email"]        = f => f.Internet.Email(),
            ["Phone"]        = f => f.Phone.PhoneNumber(),
            ["PhoneNumber"]  = f => f.Phone.PhoneNumber(),
            ["Telephone"]    = f => f.Phone.PhoneNumber(),

            // URLs
            ["Url"]          = f => f.Internet.Url(),
            ["Link"]         = f => f.Internet.Url(),
            ["Href"]         = f => f.Internet.Url(),
            ["CtaUrl"]       = f => f.Internet.Url(),
            ["LinkUrl"]      = f => f.Internet.Url(),

            // Slugs
            ["Slug"]         = f => string.Join("-", f.Lorem.Words(3)).ToLowerInvariant(),
            ["Alias"]        = f => string.Join("-", f.Lorem.Words(3)).ToLowerInvariant(),
            ["Path"]         = f => string.Join("-", f.Lorem.Words(3)).ToLowerInvariant(),
        };

    /// <inheritdoc />
    public virtual Func<Faker, object>? Resolve(string propertyName, string? contentTypeName = null)
    {
        // 1. Qualified exact: "TypeName.PropertyName" — type-specific override wins.
        if (contentTypeName is not null
            && _effectiveMap.TryGetValue($"{contentTypeName}.{propertyName}", out var qualified))
            return qualified;

        // 2. Unqualified exact.
        if (_effectiveMap.TryGetValue(propertyName, out var exact))
            return exact;

        // 3. Unqualified suffix — qualified keys (containing '.') are excluded so that
        //    "HeroBlock.Title" never accidentally suffix-matches an unrelated property name.
        foreach ((string key, var generator) in _effectiveMap)
        {
            if (!key.Contains('.') &&
                propertyName.EndsWith(key, StringComparison.OrdinalIgnoreCase))
                return generator;
        }

        return null;
    }

    /// <inheritdoc />
    public virtual object? ResolveByType(Type type, Faker faker)
    {
        if (type == typeof(string))
            return faker.Lorem.Sentence(4);

        if (type == typeof(DateTime) || type == typeof(DateTime?))
            return faker.Date.Past();

        if (type == typeof(DateTimeOffset) || type == typeof(DateTimeOffset?))
            return (DateTimeOffset)faker.Date.Past();

        if (type == typeof(bool) || type == typeof(bool?))
            return faker.Random.Bool();

        if (type == typeof(int) || type == typeof(int?))
            return faker.Random.Int(1, 1000);

        if (type == typeof(decimal) || type == typeof(decimal?))
            return faker.Finance.Amount();

        if (type == typeof(double) || type == typeof(double?))
            return (double)faker.Finance.Amount();

        if (type == typeof(float) || type == typeof(float?))
            return (float)faker.Finance.Amount();

        return null;
    }

    private static IReadOnlyDictionary<string, Func<Faker, object>> Merge(
        IReadOnlyDictionary<string, Func<Faker, object>> builtIn,
        IReadOnlyDictionary<string, Func<Faker, object>> additional)
    {
        var merged = new Dictionary<string, Func<Faker, object>>(builtIn, StringComparer.OrdinalIgnoreCase);
        foreach ((string key, var gen) in additional)
            merged[key] = gen; // additional entries win on collision
        return merged;
    }
}

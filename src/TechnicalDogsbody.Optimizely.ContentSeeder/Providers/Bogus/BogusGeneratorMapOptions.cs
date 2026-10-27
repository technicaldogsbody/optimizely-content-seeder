using Bogus;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;

/// <summary>
/// Holds additional property-name generators that are merged into <see cref="DefaultBogusPropertyMap"/>
/// at startup, for every seed definition. Entries here override built-in entries with the same key.
/// For a rule that belongs to one definition, use <see cref="Definitions.ISeedDefinition.Generators"/>.
/// Configure via <c>builder.ExtendBogusPropertyMap(...)</c>.
/// </summary>
public sealed class BogusGeneratorMapOptions
{
    /// <summary>
    /// Property name → Faker generator. Keys are matched case-insensitively.
    /// Both exact-name and suffix matching apply, consistent with the built-in map.
    /// </summary>
    public Dictionary<string, Func<Faker, object>> AdditionalGenerators { get; }
        = new(StringComparer.OrdinalIgnoreCase);
}

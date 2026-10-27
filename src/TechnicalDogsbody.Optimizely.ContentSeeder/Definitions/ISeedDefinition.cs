using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Defines a seed tree for a specific set of content to be created in Optimizely CMS.
/// Implement this interface to declare your content seeding strategy.
/// Register via <c>options.AddDefinition&lt;T&gt;()</c> inside <c>AddContentSeeder()</c>.
/// </summary>
public interface ISeedDefinition
{
    /// <summary>
    /// Stable unique identifier for this definition. Persisted in ContentSeederRecord.
    /// Must not change after first use in any environment.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Builds and returns the seed tree. Called fresh on each seed run.
    /// </summary>
    SeedTree Build();

    /// <summary>
    /// Bogus generator rules for this definition's content, or null for none. They decide the value of a property when
    /// Bogus generates it, and the placeholder an item keeps when an AI provider is in use. They are separate from
    /// <c>IAiSeedDefinition.WritingBrief</c>, which is what the AI writes from. Changing them needs a Reseed to apply.
    /// Precedence: this rule, then the global <c>ExtendBogusPropertyMap</c> entries, then the built-in heuristics.
    /// </summary>
    ContentGenerators? Generators => null;

    /// <summary>
    /// IETF tag for this seed's master language, or null to use <c>ContentSeeder:MasterLanguage</c> from options.
    /// Bound at seed time and part of the definition fingerprint.
    /// </summary>
    string? MasterLanguage => null;

    /// <summary>
    /// IETF tags for this seed's translation branches, or null to use <c>ContentSeeder:Languages</c> from options.
    /// An empty list means this seed has no translation branches, even when options name some.
    /// </summary>
    IReadOnlyList<string>? Languages => null;
}

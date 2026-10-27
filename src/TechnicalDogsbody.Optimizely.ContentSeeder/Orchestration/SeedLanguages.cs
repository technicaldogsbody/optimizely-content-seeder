using System.Globalization;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;

/// <summary>
/// The languages one seed definition is written in: the definition's own, where it sets them, otherwise the
/// <see cref="ContentSeederOptions"/> defaults.
/// </summary>
internal readonly record struct SeedLanguages(string Master, string[] Additional)
{
    /// <summary>
    /// The master language first, then the translation branches, in the order they are seeded.
    /// </summary>
    public IReadOnlyList<string> All => [Master, .. Additional];

    /// <summary>
    /// Resolves a definition's languages. A tag the definition sets that is not a valid culture throws, since the
    /// definition is code and the mistake should be caught at once. Tags from options keep their existing behaviour
    /// of falling back to English with a warning (see <see cref="Cultures.Resolve"/>).
    /// </summary>
    internal static SeedLanguages Resolve(ISeedDefinition definition, ContentSeederOptions options)
    {
        if (definition.MasterLanguage is { } master)
            Validate(master);
        if (definition.Languages is { } languages)
            foreach (string language in languages)
                Validate(language);

        return new SeedLanguages(
            definition.MasterLanguage ?? options.MasterLanguage ?? "en",
            (definition.Languages ?? options.Languages ?? []).ToArray());
    }

    private static void Validate(string language)
    {
        try
        {
            _ = CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException ex)
        {
            throw new InvalidOperationException(
                $"'{language}' is not a valid language tag. Use an IETF tag such as 'en' or 'sv-SE'.", ex);
        }
    }
}

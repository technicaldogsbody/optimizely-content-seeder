namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>The names of the kinds of item a dry run reports, as shown to people.</summary>
public static class DryRunKinds
{
    /// <summary>Pages, one version per language.</summary>
    public const string Pages = "Pages";

    /// <summary>Shared blocks, one version per language. Used when the CMS has no sections.</summary>
    public const string Blocks = "Blocks";

    /// <summary>Shared blocks and shared sections, one version per language.</summary>
    public const string BlocksAndSharedSections = "Blocks and shared sections";

    /// <summary>Structural section blueprints, created once for all languages.</summary>
    public const string SectionBlueprints = "Section blueprints";
}

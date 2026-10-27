namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Places a <see cref="SectionDefinition"/> into an Experience's top-level composition.
/// Order within <see cref="SeedNode.Sections"/> determines the outline order.
/// Each assignment stamps a fresh, independent instance from the referenced section's
/// Blueprint — placing the same SectionDefinition on multiple Experiences produces
/// independent instances, not one instance shared by ContentLink.
/// </summary>
public sealed class SectionSlotAssignment
{
    /// <summary>References <see cref="SectionDefinition.Id"/> (i.e. a blueprint to instance from).</summary>
    public string SectionId { get; }

    /// <summary>Optional display name for this occurrence, shown in the Visual Builder UI.</summary>
    public string? Name { get; }

    /// <summary>Optional Visual Builder Style display template key to apply to this placement.</summary>
    public string? DisplayTemplateKey { get; }

    /// <summary>Optional Visual Builder Style display settings to apply to this placement.</summary>
    public IReadOnlyDictionary<string, string>? DisplaySettings { get; }

    /// <summary>
    /// Places a section, by its id, into a page's Visual Builder layout, with optional display styling.
    /// </summary>
    public SectionSlotAssignment(
        string sectionId,
        string? name = null,
        string? displayTemplateKey = null,
        IReadOnlyDictionary<string, string>? displaySettings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);
        SectionId = sectionId;
        Name = name;
        DisplayTemplateKey = displayTemplateKey;
        DisplaySettings = displaySettings;
    }
}

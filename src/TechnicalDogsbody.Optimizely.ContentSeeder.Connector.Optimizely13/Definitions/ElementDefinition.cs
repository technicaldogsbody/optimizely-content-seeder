namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Specifies an element block to be created inline inside a <see cref="ColumnDefinition"/>.
/// Elements are never shared — each is created fresh and embedded directly into its
/// parent section's composition (never saved as its own standalone content item).
/// </summary>
public sealed class ElementDefinition
{
    /// <summary>CLR type of the element content type. Must be BlockData with [CompositionBehavior.ElementEnabledKey].</summary>
    public Type Type { get; }

    /// <summary>Optional display name for this occurrence, shown in the Visual Builder UI.</summary>
    public string? Name { get; }

    /// <summary>
    /// Optional callback invoked with (elementInstance, 0) after PopulateAsync and before composition.
    /// </summary>
    public Action<object, int>? Configure { get; }

    /// <summary>
    /// Explicit per-property image dimensions. Key is the property name;
    /// value is (Width, Height) in pixels.
    /// </summary>
    public IReadOnlyDictionary<string, (int Width, int Height)>? ImageSizes { get; }

    /// <summary>
    /// Where video properties get their value, keyed by property name (see <see cref="VideoSource"/>).
    /// Empty when none are declared.
    /// </summary>
    public IReadOnlyDictionary<string, VideoSource> Videos { get; }

    /// <summary>Optional Visual Builder Style display template key to apply to this element.</summary>
    public string? DisplayTemplateKey { get; }

    /// <summary>Optional Visual Builder Style display settings to apply to this element.</summary>
    public IReadOnlyDictionary<string, string>? DisplaySettings { get; }

    /// <summary>
    /// The named rule set this element uses (see <c>ForContentType&lt;T&gt;(name, ...)</c>), or null for the default rules.
    /// </summary>
    public string? Instance { get; }

    /// <summary>
    /// An element (a block) placed in a column of a structural section.
    /// </summary>
    public ElementDefinition(
        Type type,
        string? name = null,
        Action<object, int>? configure = null,
        IReadOnlyDictionary<string, (int Width, int Height)>? imageSizes = null,
        string? displayTemplateKey = null,
        IReadOnlyDictionary<string, string>? displaySettings = null,
        IReadOnlyDictionary<string, VideoSource>? videos = null,
        string? instance = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        Type = type;
        Name = name;
        Configure = configure;
        ImageSizes = imageSizes;
        DisplayTemplateKey = displayTemplateKey;
        DisplaySettings = displaySettings;
        Videos = videos ?? new Dictionary<string, VideoSource>();
        Instance = DefinitionInstance.Normalise(instance);
    }
}

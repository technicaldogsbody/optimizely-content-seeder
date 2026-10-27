using EPiServer.VisualBuilder;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Specifies a Visual Builder section placed on Experience pages via a <see cref="SectionSlotAssignment"/>.
/// <list type="bullet">
/// <item>A structural section (<c>SectionData</c> subtype) is created once as an Optimizely Blueprint and
/// placed inline on each Experience with a freshly built row/column/element composition — grid-layout
/// sections cannot be saved as standalone content.</item>
/// <item>A leaf section (plain <c>BlockData</c> with the section behaviour) is created once as ordinary
/// shared content, exactly like a <see cref="BlockDefinition"/>, and referenced from every page.</item>
/// </list>
/// </summary>
public sealed class SectionDefinition
{
    /// <summary>
    /// Stable unique identifier within the SeedTree. Shares one Id namespace with
    /// <see cref="BlockDefinition.Id"/> — a Block and a Section must never share an Id.
    /// Referenced by <see cref="SectionSlotAssignment.SectionId"/>.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// CLR type of the section content type. Either a SectionData subtype (structural,
    /// may declare <see cref="Rows"/>) or a BlockData type with
    /// [CompositionBehavior.SectionEnabledKey] (a leaf "block-as-section", no Rows).
    /// </summary>
    public Type Type { get; }

    /// <summary>
    /// Optional callback invoked with (sectionInstance, 0) after PopulateAsync and before the
    /// blueprint is created (structural) or the shared content is saved (leaf). Use for fixed
    /// overrides on the section's own properties.
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

    /// <summary>
    /// Row sources (explicit <see cref="RowDefinition"/>s and/or <see cref="RepeatingElementDefinition"/>
    /// generators) built into this section's composition on every placement, in declaration order.
    /// Must be empty unless <see cref="Type"/> derives from SectionData.
    /// </summary>
    public IReadOnlyList<ISectionRowSource> Rows { get; }

    /// <summary>
    /// The named rule set this section's own content uses (see <c>ForContentType&lt;T&gt;(name, ...)</c>), or null for the
    /// default rules. Its rows and elements use their own <c>instance</c> values.
    /// </summary>
    public string? Instance { get; }

    /// <summary>
    /// A section of a page. A structural section is a Visual Builder layout of rows. A shared section is a block used as a section.
    /// </summary>
    public SectionDefinition(
        string id,
        Type type,
        Action<object, int>? configure = null,
        IReadOnlyDictionary<string, (int Width, int Height)>? imageSizes = null,
        IReadOnlyList<ISectionRowSource>? rows = null,
        IReadOnlyDictionary<string, VideoSource>? videos = null,
        string? instance = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(type);

        if (rows is { Count: > 0 } && !typeof(SectionData).IsAssignableFrom(type))
            throw new InvalidOperationException(
                $"SectionDefinition '{id}' declares Rows but Type '{type.Name}' does not derive from " +
                "EPiServer.VisualBuilder.SectionData. Only structural sections can have nested rows/columns.");

        Id = id;
        Type = type;
        Configure = configure;
        ImageSizes = imageSizes;
        Rows = rows ?? [];
        Videos = videos ?? new Dictionary<string, VideoSource>();
        Instance = DefinitionInstance.Normalise(instance);
    }
}

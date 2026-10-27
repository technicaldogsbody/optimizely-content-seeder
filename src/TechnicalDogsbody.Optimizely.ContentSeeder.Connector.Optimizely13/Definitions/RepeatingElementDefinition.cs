using EPiServer.Core;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Generates a block of rows inside a structural <see cref="SectionDefinition"/> from a single
/// element type. For every column count from <see cref="MinColumns"/> to <see cref="MaxColumns"/>
/// (inclusive) one row is created containing that many columns, each holding one fresh element.
/// <c>minColumns: 1, maxColumns: 3</c> therefore yields three rows of 1, 2 and 3 columns.
/// </summary>
public sealed class RepeatingElementDefinition : ISectionRowSource
{
    /// <summary>CLR type of the element content type. Must derive from BlockData.</summary>
    public Type Type { get; }

    /// <summary>
    /// Optional base display name. Each element is named "{Name} {row}.{column}" (1-based);
    /// when null the elements are left unnamed.
    /// </summary>
    public string? Name { get; }

    /// <summary>Column count of the first generated row. Must be at least 1.</summary>
    public int MinColumns { get; }

    /// <summary>Column count of the last generated row. Must not be less than <see cref="MinColumns"/>.</summary>
    public int MaxColumns { get; }

    /// <summary>
    /// Optional callback invoked with (elementInstance, runningIndex) for every generated element,
    /// where the index is zero-based across the whole repeating definition.
    /// </summary>
    public Action<object, int>? Configure { get; }

    /// <summary>Explicit per-property image dimensions applied to every generated element.</summary>
    public IReadOnlyDictionary<string, (int Width, int Height)>? ImageSizes { get; }

    /// <summary>
    /// Where video properties get their value, keyed by property name (see <see cref="VideoSource"/>).
    /// Empty when none are declared.
    /// </summary>
    public IReadOnlyDictionary<string, VideoSource> Videos { get; }

    /// <summary>Optional Visual Builder Style display template key applied to every generated element.</summary>
    public string? DisplayTemplateKey { get; }

    /// <summary>Optional Visual Builder Style display settings applied to every generated element.</summary>
    public IReadOnlyDictionary<string, string>? DisplaySettings { get; }

    /// <summary>
    /// When true, the content is generated once and every element of this definition is a copy of it
    /// (<see cref="Configure"/> still runs for each, so it can vary them). Repeating elements are usually there
    /// to exercise a layout, not to be read, and with an AI provider every element is a request — so this
    /// saves tokens and time, and the copies are translated once too. Default: false (each element differs).
    /// </summary>
    public bool ReuseContent { get; }

    /// <summary>
    /// The named rule set every generated element uses (see <c>ForContentType&lt;T&gt;(name, ...)</c>), or null for the default rules.
    /// </summary>
    public string? Instance { get; }

    /// <summary>
    /// Generates rows of elements from one block type, one row for each column count from the minimum to the maximum.
    /// </summary>
    public RepeatingElementDefinition(
        Type type,
        string? name = null,
        int minColumns = 1,
        int maxColumns = 1,
        Action<object, int>? configure = null,
        IReadOnlyDictionary<string, (int Width, int Height)>? imageSizes = null,
        string? displayTemplateKey = null,
        IReadOnlyDictionary<string, string>? displaySettings = null,
        bool reuseContent = false,
        IReadOnlyDictionary<string, VideoSource>? videos = null,
        string? instance = null)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!typeof(BlockData).IsAssignableFrom(type))
            throw new InvalidOperationException(
                $"RepeatingElementDefinition Type '{type.Name}' does not derive from EPiServer.Core.BlockData.");
        if (minColumns < 1)
            throw new ArgumentOutOfRangeException(nameof(minColumns), minColumns, "minColumns must be at least 1.");
        if (maxColumns < minColumns)
            throw new ArgumentOutOfRangeException(nameof(maxColumns), maxColumns, "maxColumns must not be less than minColumns.");

        Type = type;
        Name = name;
        MinColumns = minColumns;
        MaxColumns = maxColumns;
        Configure = configure;
        ImageSizes = imageSizes;
        DisplayTemplateKey = displayTemplateKey;
        DisplaySettings = displaySettings;
        ReuseContent = reuseContent;
        Videos = videos ?? new Dictionary<string, VideoSource>();
        Instance = DefinitionInstance.Normalise(instance);
    }

    /// <inheritdoc />
    public void Accept(ISectionRowSourceVisitor visitor) => visitor.Visit(this);
}

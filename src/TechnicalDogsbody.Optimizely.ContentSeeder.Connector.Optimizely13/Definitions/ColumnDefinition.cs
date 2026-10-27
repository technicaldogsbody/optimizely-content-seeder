namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Specifies a column within a <see cref="RowDefinition"/>, containing an ordered list of elements.
/// Columns are structural only — never shared, always nested inline within their parent row.
/// </summary>
public sealed class ColumnDefinition
{
    /// <summary>Ordered list of elements to place in this column.</summary>
    public IReadOnlyList<ElementDefinition> Elements { get; }

    /// <summary>Optional display name for this occurrence, shown in the Visual Builder UI.</summary>
    public string? Name { get; }

    /// <summary>Optional Visual Builder Style display template key to apply to this column.</summary>
    public string? DisplayTemplateKey { get; }

    /// <summary>Optional Visual Builder Style display settings to apply to this column.</summary>
    public IReadOnlyDictionary<string, string>? DisplaySettings { get; }

    /// <summary>
    /// A column in a row, holding an ordered list of elements.
    /// </summary>
    public ColumnDefinition(
        IReadOnlyList<ElementDefinition>? elements = null,
        string? name = null,
        string? displayTemplateKey = null,
        IReadOnlyDictionary<string, string>? displaySettings = null)
    {
        Elements = elements ?? [];
        Name = name;
        DisplayTemplateKey = displayTemplateKey;
        DisplaySettings = displaySettings;
    }
}

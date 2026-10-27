namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Specifies a row within a <see cref="SectionDefinition"/>'s composition, containing an
/// ordered list of columns. Rows are structural only — never shared, always nested inline
/// within their parent section.
/// </summary>
public sealed class RowDefinition : ISectionRowSource
{
    /// <summary>Ordered list of columns to place in this row.</summary>
    public IReadOnlyList<ColumnDefinition> Columns { get; }

    /// <summary>Optional display name for this occurrence, shown in the Visual Builder UI.</summary>
    public string? Name { get; }

    /// <summary>Optional Visual Builder Style display template key to apply to this row.</summary>
    public string? DisplayTemplateKey { get; }

    /// <summary>Optional Visual Builder Style display settings to apply to this row.</summary>
    public IReadOnlyDictionary<string, string>? DisplaySettings { get; }

    /// <summary>
    /// A row of columns within a structural section.
    /// </summary>
    public RowDefinition(
        IReadOnlyList<ColumnDefinition>? columns = null,
        string? name = null,
        string? displayTemplateKey = null,
        IReadOnlyDictionary<string, string>? displaySettings = null)
    {
        Columns = columns ?? [];
        Name = name;
        DisplayTemplateKey = displayTemplateKey;
        DisplaySettings = displaySettings;
    }

    /// <inheritdoc />
    public void Accept(ISectionRowSourceVisitor visitor) => visitor.Visit(this);
}

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Declares a page type node (and its children) within a SeedTree. A CMS connector can derive from this to add its own
/// placements, such as Visual Builder sections.
/// </summary>
public class SeedNode
{
    /// <summary>CLR type of the page content type. Must implement IContent.</summary>
    public Type Type { get; }

    /// <summary>
    /// Number of independent instances to create. Each receives separate PopulateAsync + Configure calls.
    /// Zero is valid and produces no content or tracking records.
    /// </summary>
    public int Count { get; }

    /// <summary>ContentArea assignments applied to the master language version only.</summary>
    public IReadOnlyList<ContentAreaAssignment> ContentAreaAssignments { get; }

    /// <summary>
    /// Optional callback invoked with (pageInstance, instanceIndex) after PopulateAsync and before save.
    /// </summary>
    public Action<object, int>? Configure { get; }

    /// <summary>
    /// Explicit per-property image dimensions. Key is the property name (e.g. "HeroImage");
    /// value is (Width, Height) in pixels.
    /// Only reference properties listed here will have a media asset created. Unlisted properties are left null.
    /// </summary>
    public IReadOnlyDictionary<string, (int Width, int Height)>? ImageSizes { get; }

    /// <summary>
    /// Where video properties get their value, keyed by property name (see <see cref="VideoSource"/>).
    /// Empty when none are declared.
    /// </summary>
    public IReadOnlyDictionary<string, VideoSource> Videos { get; }

    /// <summary>Child page nodes created under each instance of this page.</summary>
    public IReadOnlyList<SeedNode> Children { get; }

    /// <summary>
    /// The named rule set this page uses (see <c>ForContentType&lt;T&gt;(name, ...)</c>), or null for the default rules.
    /// Applies to this page's own content only; children do not inherit it.
    /// </summary>
    public string? Instance { get; }

    /// <summary>
    /// A page to seed: its content type, how many copies to create, and the pages beneath it.
    /// </summary>
    internal SeedNode(
        Type type,
        int count = 1,
        IReadOnlyList<ContentAreaAssignment>? contentAreaAssignments = null,
        Action<object, int>? configure = null,
        IReadOnlyDictionary<string, (int Width, int Height)>? imageSizes = null,
        IReadOnlyList<SeedNode>? children = null,
        IReadOnlyDictionary<string, VideoSource>? videos = null,
        string? instance = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be >= 0.");

        Type = type;
        Count = count;
        ContentAreaAssignments = contentAreaAssignments ?? [];
        Configure = configure;
        ImageSizes = imageSizes;
        Children = children ?? [];
        Videos = videos ?? new Dictionary<string, VideoSource>();
        Instance = DefinitionInstance.Normalise(instance);
    }
}

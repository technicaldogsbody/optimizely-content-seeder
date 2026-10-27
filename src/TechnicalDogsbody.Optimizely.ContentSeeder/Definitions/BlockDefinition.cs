namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Specifies a shared block to be created and optionally configured.
/// All blocks in SeedTree.Blocks are created before any page nodes.
/// </summary>
public sealed class BlockDefinition
{
    /// <summary>Stable unique identifier within the SeedTree. Referenced by ContentAreaAssignment.BlockIds.</summary>
    public string Id { get; }

    /// <summary>CLR type of the block content type, as the CMS defines it.</summary>
    public Type Type { get; }

    /// <summary>
    /// Optional callback invoked with (blockInstance, 0) after PopulateAsync and before save.
    /// Use for fixed overrides on specific block instances.
    /// </summary>
    public Action<object, int>? Configure { get; }

    /// <summary>
    /// Explicit per-property image dimensions. Key is the property name (e.g. "BackgroundImage");
    /// value is (Width, Height) in pixels.
    /// Only <see cref="EPiServer.Core.ContentReference"/> properties listed here will have a
    /// media asset created. Unlisted properties are left null.
    /// </summary>
    public IReadOnlyDictionary<string, (int Width, int Height)>? ImageSizes { get; }

    /// <summary>
    /// Where video properties get their value, keyed by property name (see <see cref="VideoSource"/>).
    /// Empty when none are declared.
    /// </summary>
    public IReadOnlyDictionary<string, VideoSource> Videos { get; }

    /// <summary>
    /// The named rule set this block uses (see <c>ForContentType&lt;T&gt;(name, ...)</c>), or null for the default rules.
    /// </summary>
    public string? Instance { get; }

    /// <summary>
    /// Declares a shared block: its id, its content type, and optional configuration, image sizes and videos.
    /// </summary>
    public BlockDefinition(
        string id,
        Type type,
        Action<object, int>? configure = null,
        IReadOnlyDictionary<string, (int Width, int Height)>? imageSizes = null,
        IReadOnlyDictionary<string, VideoSource>? videos = null,
        string? instance = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(type);
        Id = id;
        Type = type;
        Configure = configure;
        ImageSizes = imageSizes;
        Videos = videos ?? new Dictionary<string, VideoSource>();
        Instance = DefinitionInstance.Normalise(instance);
    }
}

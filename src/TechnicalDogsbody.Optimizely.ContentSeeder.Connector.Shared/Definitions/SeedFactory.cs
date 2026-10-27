namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Entry points for writing a seed definition. Each returns the type the seeder expects, so a definition only needs
/// <c>SeedFactory</c> in scope. A connector adds its own entry points in a partial class (see the Optimizely 13 connector
/// for <c>Experience</c> pages and the sections they place).
/// </summary>
public static partial class SeedFactory
{
    /// <summary>A page to seed. See <see cref="SeedNode"/> for the arguments.</summary>
    public static SeedNode Page(
        Type type,
        int count = 1,
        IReadOnlyList<ContentAreaAssignment>? contentAreaAssignments = null,
        Action<object, int>? configure = null,
        IReadOnlyDictionary<string, (int Width, int Height)>? imageSizes = null,
        IReadOnlyList<SeedNode>? children = null,
        IReadOnlyDictionary<string, VideoSource>? videos = null,
        string? instance = null)
        => new(type, count, contentAreaAssignments, configure, imageSizes, children, videos, instance);

    /// <summary>The content for one seed definition: its shared blocks and page hierarchy. See <see cref="SeedTree"/>.</summary>
    public static SeedTree Tree(
        IReadOnlyList<BlockDefinition>? blocks = null,
        IReadOnlyList<SeedNode>? nodes = null)
        => new(blocks, nodes);
}

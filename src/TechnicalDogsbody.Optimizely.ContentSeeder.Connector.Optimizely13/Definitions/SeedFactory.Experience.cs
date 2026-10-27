namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// The Optimizely 13 entry points for Experience pages and the Visual Builder sections they place.
/// </summary>
public static partial class SeedFactory
{
    /// <summary>An Experience page to seed, with its Visual Builder sections. See <see cref="ExperienceSeedNode"/> for the arguments.</summary>
    public static ExperienceSeedNode Experience(
        Type type,
        int count = 1,
        IReadOnlyList<ContentAreaAssignment>? contentAreaAssignments = null,
        IReadOnlyList<SectionSlotAssignment>? sections = null,
        Action<object, int>? configure = null,
        IReadOnlyDictionary<string, (int Width, int Height)>? imageSizes = null,
        IReadOnlyList<SeedNode>? children = null,
        IReadOnlyDictionary<string, VideoSource>? videos = null,
        string? instance = null)
        => new(type, count, contentAreaAssignments, sections, configure, imageSizes, children, videos, instance);

    /// <summary>
    /// The content for one seed definition, including the Visual Builder section blueprints. Pass <c>null</c> for
    /// <paramref name="blocks"/> or <paramref name="nodes"/> when there are none. See <see cref="ExperienceSeedTree"/>.
    /// </summary>
    public static ExperienceSeedTree Tree(
        IReadOnlyList<BlockDefinition>? blocks,
        IReadOnlyList<SeedNode>? nodes,
        IReadOnlyList<SectionDefinition> sections)
        => new(blocks, nodes, sections);
}

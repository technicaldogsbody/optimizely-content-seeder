namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// A page node for an Optimizely Experience page, which can also place Visual Builder sections. Use this instead of
/// <see cref="SeedNode"/> for an <c>ExperienceData</c> page.
/// </summary>
public sealed class ExperienceSeedNode : SeedNode
{
    /// <summary>
    /// Visual Builder section placements applied to the master language version only.
    /// Valid only when <see cref="SeedNode.Type"/> derives from ExperienceData.
    /// </summary>
    public IReadOnlyList<SectionSlotAssignment> Sections { get; }

    /// <summary>
    /// A page to seed, with its Visual Builder sections. See <see cref="SeedNode"/> for the other arguments.
    /// </summary>
    internal ExperienceSeedNode(
        Type type,
        int count = 1,
        IReadOnlyList<ContentAreaAssignment>? contentAreaAssignments = null,
        IReadOnlyList<SectionSlotAssignment>? sections = null,
        Action<object, int>? configure = null,
        IReadOnlyDictionary<string, (int Width, int Height)>? imageSizes = null,
        IReadOnlyList<SeedNode>? children = null,
        IReadOnlyDictionary<string, VideoSource>? videos = null,
        string? instance = null)
        : base(type, count, contentAreaAssignments, configure, imageSizes, children, videos, instance)
    {
        Sections = sections ?? [];
    }
}

/// <summary>
/// The Visual Builder parts of a node tree, read without the caller having to cast.
/// </summary>
internal static class SectionAccess
{
    /// <summary>The section placements of a node: empty for a node that is not an <see cref="ExperienceSeedNode"/>.</summary>
    public static IReadOnlyList<SectionSlotAssignment> SectionsOf(this SeedNode node)
        => node is ExperienceSeedNode experience ? experience.Sections : [];

    /// <summary>The section blueprints of a tree: empty for a tree that is not an <see cref="ExperienceSeedTree"/>.</summary>
    public static IReadOnlyList<SectionDefinition> SectionsOf(this SeedTree tree)
        => tree is ExperienceSeedTree experience ? experience.Sections : [];
}

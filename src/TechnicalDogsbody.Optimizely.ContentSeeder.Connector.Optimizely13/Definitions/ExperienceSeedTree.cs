using EPiServer.VisualBuilder;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// A seed tree that also holds Visual Builder section blueprints. Section IDs share one namespace with Block IDs, and
/// placements are only valid on Experience pages (see <see cref="ExperienceSeedNode"/>).
/// </summary>
public sealed class ExperienceSeedTree : SeedTree
{
    /// <summary>
    /// Visual Builder section blueprint specifications. Section IDs share one namespace with
    /// Block IDs — a Block and a Section must never use the same ID. All sections are processed
    /// before SeedTree.Nodes.
    /// </summary>
    public IReadOnlyList<SectionDefinition> Sections { get; }

    /// <summary>
    /// The complete content for one seed definition: its shared blocks, sections and page hierarchy.
    /// </summary>
    internal ExperienceSeedTree(
        IReadOnlyList<BlockDefinition>? blocks = null,
        IReadOnlyList<SeedNode>? nodes = null,
        IReadOnlyList<SectionDefinition>? sections = null)
        : base(blocks, nodes)
    {
        Sections = sections ?? [];
    }

    /// <summary>
    /// Validates the tree, including its sections. Throws <see cref="InvalidOperationException"/> if a section ID is duplicated
    /// or collides with a block ID, if a section placement is on a node that is not an Experience page, or if a placement
    /// references an unknown section.
    /// </summary>
    public override void Validate()
    {
        base.Validate();

        var blockIds = Blocks.Select(block => block.Id).ToHashSet(StringComparer.Ordinal);
        var sectionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in Sections)
        {
            if (blockIds.Contains(section.Id) || !sectionIds.Add(section.Id))
                throw new InvalidOperationException(
                    $"Duplicate id '{section.Id}' found in SeedTree.Sections, or it collides with a BlockDefinition.Id. " +
                    "Block and Section IDs share one namespace and must all be unique.");
        }

        ValidateSlots(Nodes, sectionIds);
    }

    private static void ValidateSlots(IReadOnlyList<SeedNode> nodes, HashSet<string> sectionIds)
    {
        foreach (var node in nodes)
        {
            var sections = node.SectionsOf();
            if (sections.Count > 0 && !typeof(ExperienceData).IsAssignableFrom(node.Type))
                throw new InvalidOperationException(
                    $"SeedNode for type '{node.Type.Name}' declares Sections but does not derive from " +
                    "EPiServer.VisualBuilder.ExperienceData. Section placements are only valid on Experience pages.");

            foreach (var slot in sections)
            {
                if (!sectionIds.Contains(slot.SectionId))
                    throw new InvalidOperationException(
                        $"SectionSlotAssignment on '{node.Type.Name}' references unknown SectionId '{slot.SectionId}'. " +
                        "Ensure this ID is declared in SeedTree.Sections.");
            }

            ValidateSlots(node.Children, sectionIds);
        }
    }
}

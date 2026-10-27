namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Defines the complete content tree to be seeded: shared blocks and the page hierarchy. All blocks are created before any
/// page nodes. A CMS connector can derive from this to add its own parts, such as Visual Builder sections.
/// </summary>
public class SeedTree
{
    /// <summary>
    /// Shared block specifications. Block IDs must be unique within the tree.
    /// All blocks are processed before SeedTree.Nodes.
    /// </summary>
    public IReadOnlyList<BlockDefinition> Blocks { get; }

    /// <summary>Top-level page node hierarchy.</summary>
    public IReadOnlyList<SeedNode> Nodes { get; }

    /// <summary>
    /// The complete content for one seed definition: its shared blocks and page hierarchy.
    /// </summary>
    internal SeedTree(
        IReadOnlyList<BlockDefinition>? blocks = null,
        IReadOnlyList<SeedNode>? nodes = null)
    {
        Blocks = blocks ?? [];
        Nodes = nodes ?? [];
    }

    /// <summary>
    /// Validates the tree. Throws <see cref="InvalidOperationException"/> if any block IDs are duplicated, or if any
    /// assignment references an unknown block ID. A derived tree adds its own checks by overriding this.
    /// </summary>
    public virtual void Validate()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var block in Blocks)
        {
            if (!seen.Add(block.Id))
                throw new InvalidOperationException(
                    $"Duplicate BlockDefinition.Id '{block.Id}' found in SeedTree.Blocks. Each block ID must be unique.");
        }

        ValidateNodes(Nodes, seen);
    }

    private static void ValidateNodes(IReadOnlyList<SeedNode> nodes, HashSet<string> blockIds)
    {
        foreach (var node in nodes)
        {
            foreach (var assignment in node.ContentAreaAssignments)
            {
                foreach (string blockId in assignment.BlockIds)
                {
                    if (!blockIds.Contains(blockId))
                        throw new InvalidOperationException(
                            $"ContentAreaAssignment on '{node.Type.Name}.{assignment.PropertyName}' references unknown BlockId '{blockId}'. " +
                            "Ensure this ID is declared in SeedTree.Blocks.");
                }
            }

            ValidateNodes(node.Children, blockIds);
        }
    }
}

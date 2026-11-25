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
    /// Validates the tree. Throws <see cref="InvalidOperationException"/> if any block IDs or page keys are duplicated, a
    /// keyed page has a count other than 1, or any assignment references an unknown block ID or page key. A derived tree
    /// adds its own checks by overriding this.
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

        // Page keys share one namespace with block ids, so an id in an assignment always means exactly one item.
        var pageKeys = new HashSet<string>(StringComparer.Ordinal);
        CollectKeys(Nodes, seen, pageKeys);
        ValidateNodes(Nodes, seen, pageKeys);
        ValidateReferences(Nodes, seen, pageKeys);
    }

    private static void CollectKeys(IReadOnlyList<SeedNode> nodes, HashSet<string> blockIds, HashSet<string> pageKeys)
    {
        foreach (var node in nodes)
        {
            if (node.Key is { } key)
            {
                if (node.Count != 1)
                    throw new InvalidOperationException(
                        $"Page '{node.Type.Name}' has the key '{key}' but a count of {node.Count}. A keyed page must have a count of 1.");
                if (blockIds.Contains(key))
                    throw new InvalidOperationException(
                        $"Page key '{key}' on '{node.Type.Name}' is also a BlockDefinition.Id. Page keys and block IDs must be different.");
                if (!pageKeys.Add(key))
                    throw new InvalidOperationException(
                        $"Duplicate page key '{key}' found in the SeedTree. Each page key must be unique.");
            }

            CollectKeys(node.Children, blockIds, pageKeys);
        }
    }

    private static void ValidateReferences(IReadOnlyList<SeedNode> nodes, HashSet<string> blockIds, HashSet<string> pageKeys)
    {
        foreach (var node in nodes)
        {
            foreach (var reference in node.References)
            {
                if (!pageKeys.Contains(reference.Key) && !blockIds.Contains(reference.Key))
                    throw new InvalidOperationException(
                        $"ContentReferenceAssignment on '{node.Type.Name}.{reference.PropertyName}' references unknown key '{reference.Key}'. " +
                        "Give the target page that key, or declare a block with that ID in SeedTree.Blocks.");
            }

            ValidateReferences(node.Children, blockIds, pageKeys);
        }
    }

    private static void ValidateNodes(IReadOnlyList<SeedNode> nodes, HashSet<string> blockIds, HashSet<string> pageKeys)
    {
        foreach (var node in nodes)
        {
            foreach (var assignment in node.ContentAreaAssignments)
            {
                foreach (string blockId in assignment.BlockIds)
                {
                    if (!blockIds.Contains(blockId) && !pageKeys.Contains(blockId))
                        throw new InvalidOperationException(
                            $"ContentAreaAssignment on '{node.Type.Name}.{assignment.PropertyName}' references unknown BlockId '{blockId}'. " +
                            "Declare a block with this ID in SeedTree.Blocks, or give the target page this key.");
                }
            }

            ValidateNodes(node.Children, blockIds, pageKeys);
        }
    }
}

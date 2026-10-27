using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;

/// <summary>
/// Checks that every named rule set (<c>instance</c>) a seed tree uses is defined by its definition, for the type it is used on.
/// A name that is not defined would otherwise be ignored silently, and the item would get the default rules.
/// </summary>
internal static partial class InstanceNames
{
    internal static void Validate(SeedTree tree, ISeedDefinition definition)
    {
        foreach (var block in tree.Blocks)
            Check(block.Instance, block.Type, definition, $"block '{block.Id}'");

        ValidateSections(tree, definition);

        foreach (var node in tree.Nodes)
            CheckNode(node, definition);
    }

    // Checks the named rule sets a connector's sections and their elements use. Nothing when the CMS has no sections.
    static partial void ValidateSections(SeedTree tree, ISeedDefinition definition);

    private static void CheckNode(SeedNode node, ISeedDefinition definition)
    {
        Check(node.Instance, node.Type, definition, $"page {node.Type.Name}");
        foreach (var child in node.Children)
            CheckNode(child, definition);
    }

    private static void Check(string? instance, Type type, ISeedDefinition definition, string where)
    {
        if (instance is null)
            return;

        bool defined = definition.Generators?.HasInstance(type, instance) == true
                       || (definition is IAiSeedDefinition ai && ai.WritingBrief.HasInstance(type, instance));

        if (!defined)
            throw new InvalidOperationException(
                $"{where} uses the rule set '{instance}' for {type.Name}, but definition '{definition.Id}' defines no such set " +
                "for that type. Add it with ForContentType<T>(\"" + instance + "\", ...) on Generators or WritingBrief.");
    }
}

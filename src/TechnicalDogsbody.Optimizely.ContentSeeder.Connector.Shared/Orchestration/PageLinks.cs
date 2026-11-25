using EPiServer.Core;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;

/// <summary>
/// What the page walk learns for the reference pass: the page each key was seeded as, and the pages whose content
/// reference properties (or content areas) point at other seeded pages. References are applied once the whole tree exists, so a page can
/// point at a sibling or a page created after it.
/// </summary>
internal sealed class PageLinks(IEnumerable<string> blockIds)
{
    private readonly HashSet<string> _blockIds = new(blockIds, StringComparer.Ordinal);
    private readonly Dictionary<string, ContentReference> _keys = new(StringComparer.Ordinal);
    private readonly List<(SeedNode Node, ContentReference Page)> _pending = [];

    /// <summary>The pages that have references to apply, in the order they were seeded.</summary>
    internal IReadOnlyList<(SeedNode Node, ContentReference Page)> Pending => _pending;

    /// <summary>
    /// Records a page that was created or reused: its key, if it has one, and its references, if it has any.
    /// </summary>
    internal void Add(SeedNode node, ContentReference page)
    {
        if (node.Key is { } key)
            _keys[key] = page;

        if (node.References.Count > 0 || node.ContentAreaAssignments.Any(NamesPage))
            _pending.Add((node, page));
    }

    /// <summary>
    /// Whether a content area assignment names a page, so it must wait until the whole tree exists. One that names only
    /// blocks is applied when its page is created.
    /// </summary>
    internal bool NamesPage(ContentAreaAssignment assignment)
        => assignment.BlockIds.Any(id => !_blockIds.Contains(id));

    /// <summary>The page seeded for a key. Returns false when no page has that key.</summary>
    internal bool TryGetPage(string key, out ContentReference page)
        => _keys.TryGetValue(key, out page!);
}

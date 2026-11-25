namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Specifies which items should be added to a named ContentArea property: shared blocks by ID, and pages by their
/// <c>key</c>. An assignment that names a page is applied after the whole page tree exists, so it can hold a sibling or a
/// page declared later.
/// </summary>
public sealed class ContentAreaAssignment
{
    /// <summary>Name of the ContentArea property on the page type.</summary>
    public string PropertyName { get; }

    /// <summary>
    /// Ordered list of IDs to add. Each must be a block ID in the parent SeedTree.Blocks or a page key in the same tree.
    /// </summary>
    public IReadOnlyList<string> BlockIds { get; }

    /// <summary>
    /// Places the listed blocks and keyed pages, in order, into the named content area property of a page.
    /// </summary>
    public ContentAreaAssignment(string propertyName, IReadOnlyList<string> blockIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(blockIds);
        PropertyName = propertyName;
        BlockIds = blockIds;
    }
}

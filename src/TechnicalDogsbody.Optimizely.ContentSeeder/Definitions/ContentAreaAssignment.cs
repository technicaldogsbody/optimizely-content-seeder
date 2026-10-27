namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Specifies which block IDs should be added to a named ContentArea property.
/// </summary>
public sealed class ContentAreaAssignment
{
    /// <summary>Name of the ContentArea property on the page type.</summary>
    public string PropertyName { get; }

    /// <summary>
    /// Ordered list of block IDs to add. Each ID must exist in the parent SeedTree.Blocks.
    /// </summary>
    public IReadOnlyList<string> BlockIds { get; }

    /// <summary>
    /// Places the listed blocks, in order, into the named content area property of a page.
    /// </summary>
    public ContentAreaAssignment(string propertyName, IReadOnlyList<string> blockIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(blockIds);
        PropertyName = propertyName;
        BlockIds = blockIds;
    }
}

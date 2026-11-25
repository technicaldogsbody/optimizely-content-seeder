namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Points a content reference property of a page at other content in the same seed: a page given a <c>key</c>, or a shared
/// block by its id. References are applied after the whole page tree exists, so a page can point at a sibling, a child or
/// a page further down the tree.
/// </summary>
public sealed class ContentReferenceAssignment
{
    /// <summary>Name of the <c>ContentReference</c> (or <c>PageReference</c>) property on the page type.</summary>
    public string PropertyName { get; }

    /// <summary>
    /// The <c>key</c> of a page in the same <see cref="SeedTree"/>, or the id of a <see cref="BlockDefinition"/> in it.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Sets the named property to the content seeded for <paramref name="key"/>.
    /// </summary>
    public ContentReferenceAssignment(string propertyName, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        PropertyName = propertyName;
        Key = key;
    }
}

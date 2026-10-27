namespace TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

/// <summary>
/// Identifies a media asset a provider created, without referring to the CMS's own reference type.
/// The connector converts it to the CMS's reference when it sets a property.
/// </summary>
/// <param name="Id">The asset's identifier in the CMS. Empty when there is no asset.</param>
public readonly record struct ContentHandle(string Id)
{
    /// <summary>A handle with no asset behind it.</summary>
    public static ContentHandle Empty { get; } = new(string.Empty);

    /// <summary>True when the handle does not identify an asset.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(Id);
}

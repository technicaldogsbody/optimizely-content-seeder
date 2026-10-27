using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers;

/// <summary>
/// The default <see cref="IContentModelAdapter"/> when no CMS connector is registered. The model has no framework properties,
/// content areas or references, so every property is treated as plain data.
/// </summary>
public sealed class NullContentModelAdapter : IContentModelAdapter
{
    /// <inheritdoc/>
    public IReadOnlySet<string> FrameworkPropertyNames { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Type GetOriginalType(object content) => content.GetType();

    /// <inheritdoc/>
    public bool IsContentArea(Type propertyType) => false;

    /// <inheritdoc/>
    public bool IsContentReference(Type propertyType) => false;

    /// <inheritdoc/>
    public object FromAsset(Type propertyType, ContentHandle asset)
        => throw new InvalidOperationException("No CMS connector is registered, so there is no asset reference to create.");
}

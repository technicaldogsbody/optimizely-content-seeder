using EPiServer;
using EPiServer.Core;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers;

/// <summary>
/// Default <see cref="IContentModelAdapter"/> for Optimizely CMS. The framework properties are those of IContent and its base types.
/// </summary>
public sealed class OptimizelyContentModelAdapter : IContentModelAdapter
{
    /// <inheritdoc/>
    public IReadOnlySet<string> FrameworkPropertyNames { get; } = BuildFrameworkPropertyNames();

    /// <inheritdoc/>
    public Type GetOriginalType(object content) => content.GetOriginalType();

    /// <inheritdoc/>
    public bool IsContentArea(Type propertyType)
        => propertyType == typeof(ContentArea) ||
           (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(ContentArea));

    /// <inheritdoc/>
    public bool IsContentReference(Type propertyType) => propertyType == typeof(ContentReference);

    /// <inheritdoc/>
    public object FromAsset(Type propertyType, ContentHandle asset) => asset.ToReference();

    private static HashSet<string> BuildFrameworkPropertyNames()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in new[] { typeof(IContent), typeof(ContentData), typeof(PageData), typeof(BlockData) })
        {
            foreach (var prop in type.GetProperties())
                set.Add(prop.Name);
        }

        return set;
    }
}

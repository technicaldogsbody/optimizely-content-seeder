using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using EPiServer;
using EPiServer.Construction;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAnnotations;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers;

/// <summary>
/// Default <see cref="IContentModelAdapter"/> for Optimizely CMS. The framework properties are those of IContent and its base types.
/// </summary>
/// <remarks>
/// Local blocks and block list items are built with the CMS's own content data factory and builder, so each item has its property collection
/// and is saved with the page. Without them (in unit tests, say) an item is a plain instance of the block type.
/// </remarks>
public sealed class OptimizelyContentModelAdapter(
    IContentTypeRepository? contentTypeRepository,
    IContentDataFactory<BlockData>? blockFactory,
    IContentDataBuilder? contentDataBuilder) : IContentModelAdapter
{
    /// <summary>Creates an adapter that builds block list items as plain instances. The CMS uses the injected constructor.</summary>
    public OptimizelyContentModelAdapter() : this(null, null, null)
    {
    }

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

    /// <inheritdoc/>
    public bool TryGetBlockListItemType(Type propertyType, [NotNullWhen(true)] out Type? itemType)
    {
        itemType = null;
        if (!propertyType.IsGenericType || propertyType.GetGenericArguments() is not [var arg])
            return false;

        // IList<TBlock> (and any list interface a List<TBlock> satisfies) of a block type is a block list property.
        if (!typeof(BlockData).IsAssignableFrom(arg) || !propertyType.IsAssignableFrom(typeof(List<>).MakeGenericType(arg)))
            return false;

        itemType = arg;
        return true;
    }

    /// <inheritdoc/>
    public bool IsLocalBlock(Type propertyType)
        => typeof(BlockData).IsAssignableFrom(propertyType) && !propertyType.IsAbstract;

    /// <inheritdoc/>
    public bool IsCultureSpecific(PropertyInfo property) => property.GetCustomAttribute<CultureSpecificAttribute>() is not null;

    /// <inheritdoc/>
    public object CreateBlock(Type blockType)
    {
        if (contentTypeRepository is null || blockFactory is null || contentDataBuilder is null)
            return Activator.CreateInstance(blockType)!;

        var contentType = contentTypeRepository.Load(blockType)
            ?? throw new InvalidOperationException(
                $"ContentSeeder: No content type registered for block type '{blockType.FullName}'. " +
                "Ensure the type has a [ContentType] attribute and the application has initialised.");

        var block = blockFactory.CreateInstance(contentType);
        contentDataBuilder.AddProperties(block, new BuildingContext(contentType) { SetPropertyValues = true });
        return block;
    }

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

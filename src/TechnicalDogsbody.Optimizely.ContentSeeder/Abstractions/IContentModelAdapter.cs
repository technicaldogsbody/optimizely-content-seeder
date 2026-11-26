using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

/// <summary>
/// Describes the CMS's content model to the seeder: which properties belong to the CMS framework, and which property types
/// are content areas or references, so the seeder can treat them without referring to the CMS's own types.
/// </summary>
public interface IContentModelAdapter
{
    /// <summary>Names of properties the CMS defines on its base content types. The seeder never overwrites them.</summary>
    IReadOnlySet<string> FrameworkPropertyNames { get; }

    /// <summary>The content type of an instance, without any proxy type the CMS may have wrapped it in.</summary>
    Type GetOriginalType(object content);

    /// <summary>True for a content area property, which the orchestrator fills from its assignments.</summary>
    bool IsContentArea(Type propertyType);

    /// <summary>True for a property that refers to another piece of content, such as a media asset.</summary>
    bool IsContentReference(Type propertyType);

    /// <summary>Converts an asset handle into the value of a reference property of the given type.</summary>
    object FromAsset(Type propertyType, ContentHandle asset);

    /// <summary>
    /// True for a property that holds a list of local (inline) blocks, such as <c>IList&lt;FooterColumnBlock&gt;</c>.
    /// <paramref name="itemType"/> is the block type of the list's items.
    /// </summary>
    bool TryGetBlockListItemType(Type propertyType, [NotNullWhen(true)] out Type? itemType)
    {
        itemType = null;
        return false;
    }

    /// <summary>True for a property that holds a single local (inline) block, such as <c>HeroBlock Hero</c>.</summary>
    bool IsLocalBlock(Type propertyType) => false;

    /// <summary>
    /// True for a property whose value belongs to each language version. In a language version, only these properties
    /// of a local block are written; the others are shared from the master.
    /// </summary>
    bool IsCultureSpecific(PropertyInfo property) => true;

    /// <summary>Creates a new, empty local block of the given type, for a local block property or a block list.</summary>
    object CreateBlock(Type blockType) => Activator.CreateInstance(blockType)!;
}

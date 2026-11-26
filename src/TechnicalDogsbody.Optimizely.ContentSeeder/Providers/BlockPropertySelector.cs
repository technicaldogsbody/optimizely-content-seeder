using System.Reflection;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers;

/// <summary>Chooses the properties of a local block or block list item that a content provider may write.</summary>
internal static class BlockPropertySelector
{
    /// <summary>
    /// The writable properties of a block type, without the CMS's framework properties. With
    /// <paramref name="cultureSpecificOnly"/> (a local block in a language version), only the culture-specific ones.
    /// </summary>
    internal static List<PropertyInfo> WritableBlockProperties(this IContentModelAdapter model, Type blockType, bool cultureSpecificOnly)
        => blockType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanWrite && !model.FrameworkPropertyNames.Contains(p.Name)
                                && (!cultureSpecificOnly || model.IsCultureSpecific(p)))
                    .ToList();
}

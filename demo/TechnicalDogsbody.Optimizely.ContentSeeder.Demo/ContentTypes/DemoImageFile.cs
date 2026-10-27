using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAnnotations;
using EPiServer.Framework.DataAnnotations;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

/// <summary>
/// Concrete ImageData subtype used by the ContentSeeder demo.
/// The seeder discovers any registered ImageData subtype at runtime and uses it
/// to create placeholder media assets in the Optimizely media library.
/// </summary>
[ContentType(
    DisplayName = "Image",
    GUID = "a1b2c3d4-0001-0000-0000-000000000010",
    Description = "A demo image media file.",
    GroupName = "Demo")]
[MediaDescriptor(ExtensionString = "jpg,jpeg,png,gif,webp")]
public class DemoImageFile : ImageData
{
    [Display(Name = "Alt Text", Order = 10)]
    public virtual string? AltText { get; set; }
}

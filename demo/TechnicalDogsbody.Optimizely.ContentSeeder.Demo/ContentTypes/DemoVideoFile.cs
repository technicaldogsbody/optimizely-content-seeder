using System.ComponentModel.DataAnnotations;
using EPiServer.Core;
using EPiServer.DataAnnotations;
using EPiServer.Framework.DataAnnotations;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

/// <summary>
/// Concrete VideoData subtype used by the ContentSeeder demo.
/// The seeder discovers any registered VideoData subtype at runtime and uses it
/// to save the sample video into the Optimizely media library.
/// </summary>
[ContentType(
    DisplayName = "Video File",
    GUID = "a1b2c3d4-0001-0000-0000-000000000030",
    Description = "A demo video media file.",
    GroupName = "Demo")]
[MediaDescriptor(ExtensionString = "mp4,webm")]
public class DemoVideoFile : VideoData
{
    [Display(Name = "Description", Order = 10)]
    public virtual string? Description { get; set; }
}

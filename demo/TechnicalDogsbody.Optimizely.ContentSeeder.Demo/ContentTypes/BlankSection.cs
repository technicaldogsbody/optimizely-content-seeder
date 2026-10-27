using System.ComponentModel.DataAnnotations;
using EPiServer.DataAbstraction;
using EPiServer.DataAnnotations;
using EPiServer.VisualBuilder;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

[ContentType(
    DisplayName = "Blank Section",
    GUID = "a1b2c3d4-0001-0000-0000-000000000119",
    Description = "A single full-width row. Usable as a Visual Builder section.",
    GroupName = "Demo",
    CompositionBehaviors = [CompositionBehavior.SectionEnabledKey])]
public class BlankSection : SectionData
{
}

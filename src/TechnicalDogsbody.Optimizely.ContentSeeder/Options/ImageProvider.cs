namespace TechnicalDogsbody.Optimizely.ContentSeeder.Options;

/// <summary>Where seeded images come from. Images drawn in code are always the fallback if the chosen source fails.</summary>
public enum ImageProvider
{
    /// <summary>Random placeholder photos from picsum.photos.</summary>
    Picsum,

    /// <summary>Random doodle illustrations from doodleipsum.com.</summary>
    DoodleIpsum,

    /// <summary>Gradient placeholders drawn in code. Makes no network calls.</summary>
    Generated
}

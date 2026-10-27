namespace TechnicalDogsbody.Optimizely.ContentSeeder;

/// <summary>
/// Names the seeder registers under. Shared by the DI wiring and the code that looks them up, so they cannot drift apart.
/// </summary>
internal static class ContentSeederNames
{
    /// <summary>Name of the <c>HttpClient</c> used to download placeholder images and videos.</summary>
    public const string HttpClient = "ContentSeeder";

    /// <summary>Name of the Optimizely shell module that carries the admin menu and UI assets.</summary>
    public const string ShellModule = "TechnicalDogsbody.Optimizely.ContentSeeder";
}

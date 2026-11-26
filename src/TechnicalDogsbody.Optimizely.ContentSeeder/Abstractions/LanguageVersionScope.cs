namespace TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

/// <summary>
/// True while the seeder fills a language version (not the master) of an item. In a language version, a local block's
/// properties that are not culture-specific belong to the master, so the content providers write only the culture-specific
/// ones (see <see cref="IContentModelAdapter.IsCultureSpecific"/>). It flows through async calls like
/// <see cref="SeedInstanceScope"/>.
/// </summary>
public static class LanguageVersionScope
{
    private static readonly AsyncLocal<bool> Holder = new();

    /// <summary>True while a language version is being filled.</summary>
    public static bool IsActive => Holder.Value;

    /// <summary>Marks the current flow as filling a language version until the returned value is disposed.</summary>
    public static IDisposable Enter()
    {
        bool previous = Holder.Value;
        Holder.Value = true;
        return new Restore(previous);
    }

    private sealed class Restore(bool previous) : IDisposable
    {
        public void Dispose() => Holder.Value = previous;
    }
}

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

/// <summary>
/// The named rule set (<c>instance</c>) in force for the item being generated, or null for the default rules.
/// The seeder sets it around each item it creates, and the content providers read it when they generate that item.
/// It flows through async calls and into parallel jobs, and a value set in one job does not show in another.
/// </summary>
public static class SeedInstanceScope
{
    private static readonly AsyncLocal<string?> Holder = new();

    /// <summary>The name in force for the current flow, or null for the default rules.</summary>
    public static string? Current => Holder.Value;

    /// <summary>
    /// Sets the name for the current flow until the returned value is disposed, then restores the previous name.
    /// </summary>
    public static IDisposable Enter(string? instance)
    {
        string? previous = Holder.Value;
        Holder.Value = instance;
        return new Restore(previous);
    }

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => Holder.Value = previous;
    }
}

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Checks the name of a named rule set (the <c>instance</c> argument on the definition types).
/// </summary>
internal static class DefinitionInstance
{
    /// <summary>
    /// The name, trimmed, or null when none is given. A blank name throws, rather than silently selecting the default rules.
    /// </summary>
    internal static string? Normalise(string? instance)
    {
        if (instance is null)
            return null;

        ArgumentException.ThrowIfNullOrWhiteSpace(instance);
        return instance.Trim();
    }
}

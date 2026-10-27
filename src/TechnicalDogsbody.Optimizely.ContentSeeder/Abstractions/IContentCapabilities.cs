namespace TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

/// <summary>
/// What the connected CMS can do, for code in the core that must not name a connector's types. A connector that supports
/// Visual Builder sections registers one that says so; the default says no.
/// </summary>
public interface IContentCapabilities
{
    /// <summary>True when the CMS has sections (Visual Builder). The admin UI and the completion email then show them.</summary>
    bool SupportsSections { get; }
}

/// <summary>The capabilities a connector declares. Registered by the connector, or defaulted to no sections.</summary>
public sealed record ContentCapabilities(bool SupportsSections) : IContentCapabilities;

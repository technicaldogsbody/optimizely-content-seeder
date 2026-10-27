namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// The dry-run figures for one kind of seeded item: pages, blocks and shared sections, or section blueprints.
/// </summary>
/// <param name="Kind">The kind's name, for display.</param>
/// <param name="Planned">How many of this kind a full seed would have, across all languages.</param>
/// <param name="AlreadySeeded">How many of this kind are already complete.</param>
/// <param name="WouldCreate">How many of this kind a seed would create: planned, less those already seeded, never below zero.</param>
public sealed record DryRunKindCount(string Kind, int Planned, int AlreadySeeded, int WouldCreate);

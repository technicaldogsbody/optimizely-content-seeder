using TechnicalDogsbody.MicroMediator.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Application;

/// <summary>Returned by commands that have no value to report back.</summary>
public sealed record OperationCompleted;

/// <summary>Seeds all definitions, or one definition, skipping already-seeded content.</summary>
/// <param name="SeedDefinitionId">The definition to seed, or null for every definition.</param>
public sealed record SeedCommand(string? SeedDefinitionId = null) : IRequest<OperationCompleted>;

/// <summary>Deletes and re-seeds all definitions, or one definition.</summary>
/// <param name="SeedDefinitionId">The definition to reseed, or null for every definition.</param>
public sealed record ReseedCommand(string? SeedDefinitionId = null) : IRequest<OperationCompleted>;

/// <summary>Permanently deletes the content of all definitions, or one definition, and its tracking records.</summary>
/// <param name="SeedDefinitionId">The definition to delete, or null for every definition.</param>
public sealed record DeleteAllCommand(string? SeedDefinitionId = null) : IRequest<OperationCompleted>;

/// <summary>Calculates what a seed would do, for all definitions or one. Writes no content.</summary>
/// <param name="SeedDefinitionId">The definition to check, or null for every definition.</param>
public sealed record DryRunQuery(string? SeedDefinitionId = null) : IRequest<DryRunResult>;

/// <summary>IDs of seed definitions whose shape has changed since they were seeded.</summary>
public sealed record GetChangedDefinitionsQuery : IRequest<IReadOnlyList<string>>;

/// <summary>The ids of every registered seed definition, in registration order.</summary>
public sealed record GetDefinitionIdsQuery : IRequest<IReadOnlyList<string>>;

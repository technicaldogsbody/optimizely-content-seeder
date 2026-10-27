using Microsoft.Extensions.DependencyInjection;
using TechnicalDogsbody.MicroMediator.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Application;

// These handlers are singletons, and each call opens its own DI scope. The mediator is a singleton too, and it
// dispatches from the root provider. A handler that took scoped services in its constructor would capture the root's
// instances, so every command would share one database connection. Opening a scope per call keeps each unit of work
// on its own connection, whatever the environment's scope validation settings are.

/// <summary>
/// Runs a seed for all definitions, or one, then the seed-completed handlers. A failure is raised to the caller and the handlers do not run.
/// </summary>
internal sealed class SeedCommandHandler(IServiceScopeFactory scopes)
    : IRequestHandler<SeedCommand, OperationCompleted>
{
    /// <inheritdoc/>
    public async ValueTask<OperationCompleted> HandleAsync(SeedCommand request, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISeedOrchestrator>();

        if (request.SeedDefinitionId is { } id)
            await orchestrator.SeedAsync(id, cancellationToken).ConfigureAwait(false);
        else
            await orchestrator.SeedAsync(cancellationToken).ConfigureAwait(false);

        await SeedCompletion.RunAsync(scope.ServiceProvider.GetServices<ISeedCompletedHandler>(), cancellationToken).ConfigureAwait(false);
        return new OperationCompleted();
    }
}

/// <summary>
/// Deletes the seeded content for all definitions, or one, seeds it again, then runs the seed-completed handlers.
/// </summary>
internal sealed class ReseedCommandHandler(IServiceScopeFactory scopes)
    : IRequestHandler<ReseedCommand, OperationCompleted>
{
    /// <inheritdoc/>
    public async ValueTask<OperationCompleted> HandleAsync(ReseedCommand request, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISeedOrchestrator>();

        if (request.SeedDefinitionId is { } id)
            await orchestrator.ReseedAsync(id, cancellationToken).ConfigureAwait(false);
        else
            await orchestrator.ReseedAsync(cancellationToken).ConfigureAwait(false);

        await SeedCompletion.RunAsync(scope.ServiceProvider.GetServices<ISeedCompletedHandler>(), cancellationToken).ConfigureAwait(false);
        return new OperationCompleted();
    }
}

/// <summary>
/// Deletes the content of all definitions, or one, and its tracking records.
/// </summary>
internal sealed class DeleteAllCommandHandler(IServiceScopeFactory scopes)
    : IRequestHandler<DeleteAllCommand, OperationCompleted>
{
    /// <inheritdoc/>
    public async ValueTask<OperationCompleted> HandleAsync(DeleteAllCommand request, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISeedOrchestrator>();

        if (request.SeedDefinitionId is { } id)
            await orchestrator.DeleteAllAsync(id, cancellationToken).ConfigureAwait(false);
        else
            await orchestrator.DeleteAllAsync(cancellationToken).ConfigureAwait(false);

        return new OperationCompleted();
    }
}

/// <summary>
/// Reports what a seed would do for all definitions, or one, without writing any content.
/// </summary>
internal sealed class DryRunQueryHandler(IServiceScopeFactory scopes)
    : IRequestHandler<DryRunQuery, DryRunResult>
{
    /// <inheritdoc/>
    public async ValueTask<DryRunResult> HandleAsync(DryRunQuery request, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ISeedOrchestrator>();

        return request.SeedDefinitionId is { } id
            ? await orchestrator.DryRunAsync(id, cancellationToken).ConfigureAwait(false)
            : await orchestrator.DryRunAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Lists the seed definitions whose shape has changed since they were seeded.
/// </summary>
internal sealed class GetChangedDefinitionsQueryHandler(IServiceScopeFactory scopes)
    : IRequestHandler<GetChangedDefinitionsQuery, IReadOnlyList<string>>
{
    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<string>> HandleAsync(GetChangedDefinitionsQuery request, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISeedOrchestrator>().GetChangedDefinitionsAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Lists the ids of every registered seed definition.
/// </summary>
internal sealed class GetDefinitionIdsQueryHandler(IServiceScopeFactory scopes)
    : IRequestHandler<GetDefinitionIdsQuery, IReadOnlyList<string>>
{
    /// <inheritdoc/>
    public ValueTask<IReadOnlyList<string>> HandleAsync(GetDefinitionIdsQuery request, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        return new(scope.ServiceProvider.GetRequiredService<ISeedOrchestrator>().DefinitionIds);
    }
}

/// <summary>
/// Runs the seed-completed handlers after a successful seed.
/// </summary>
internal static class SeedCompletion
{
    // Handlers run in registration order, one at a time, so each sees the content the previous one left.
    /// <summary>
    /// Runs each handler in registration order, one at a time. Stops at the first handler that throws.
    /// </summary>
    internal static async Task RunAsync(IEnumerable<ISeedCompletedHandler> handlers, CancellationToken ct)
    {
        foreach (var handler in handlers)
            await handler.HandleAsync(ct).ConfigureAwait(false);
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using TechnicalDogsbody.MicroMediator.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Application;
using TechnicalDogsbody.Optimizely.ContentSeeder.Extensions;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Application;

/// <summary>
/// Regression tests for scope handling. The mediator is a singleton that dispatches from the root provider, so a
/// handler that takes scoped services directly would capture the root's instances and share them between calls.
/// </summary>
public class MediatorScopeTests
{
    [Fact]
    public async Task EachCommand_GetsItsOwnOrchestratorInsteadOfASharedRootInstance()
    {
        var resolved = new List<ISeedOrchestrator>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ISeedOrchestrator>(_ =>
        {
            var orchestrator = new Mock<ISeedOrchestrator>();
            orchestrator.Setup(o => o.SeedAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            resolved.Add(orchestrator.Object);
            return orchestrator.Object;
        });
        services.AddContentSeederMediator();

        // Default options, as in production: scope validation is off, which is what lets a root resolution succeed.
        var root = services.BuildServiceProvider();
        var mediator = root.GetRequiredService<IMediator>();

        await mediator.SendAsync(new SeedCommand());
        await mediator.SendAsync(new SeedCommand());

        Assert.Equal(2, resolved.Count);
        Assert.NotSame(resolved[0], resolved[1]);
    }
}

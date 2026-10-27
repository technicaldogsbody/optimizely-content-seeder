using Microsoft.Extensions.DependencyInjection;
using Moq;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Application;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Application;

public class SeedCompletedHandlerTests
{
    private readonly Mock<ISeedOrchestrator> _orchestrator = new();
    private readonly Mock<ISeedCompletedHandler> _completed = new();

    // Handlers open a scope per call, so they are tested through a real scope factory.
    private static IServiceScopeFactory ScopesFor(ISeedOrchestrator orchestrator, params ISeedCompletedHandler[] completed)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => orchestrator);
        foreach (var handler in completed)
            services.AddScoped(_ => handler);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public async Task SeedCommand_RunsCompletedHandlersAfterASuccessfulSeed()
    {
        var sut = new SeedCommandHandler(ScopesFor(_orchestrator.Object, _completed.Object));

        await sut.HandleAsync(new SeedCommand(), CancellationToken.None);

        _orchestrator.Verify(o => o.SeedAsync(It.IsAny<CancellationToken>()), Times.Once);
        _completed.Verify(h => h.HandleAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReseedCommand_RunsCompletedHandlersAfterASuccessfulReseed()
    {
        var sut = new ReseedCommandHandler(ScopesFor(_orchestrator.Object, _completed.Object));

        await sut.HandleAsync(new ReseedCommand(), CancellationToken.None);

        _completed.Verify(h => h.HandleAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SeedCommand_DoesNotRunCompletedHandlersWhenTheSeedFails()
    {
        _orchestrator.Setup(o => o.SeedAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
        var sut = new SeedCommandHandler(ScopesFor(_orchestrator.Object, _completed.Object));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.HandleAsync(new SeedCommand(), CancellationToken.None).AsTask());

        _completed.Verify(h => h.HandleAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SeedCommand_RunsEveryCompletedHandlerInOrder()
    {
        var order = new List<string>();
        var first = new Mock<ISeedCompletedHandler>();
        first.Setup(h => h.HandleAsync(It.IsAny<CancellationToken>())).Callback(() => order.Add("first")).Returns(Task.CompletedTask);
        var second = new Mock<ISeedCompletedHandler>();
        second.Setup(h => h.HandleAsync(It.IsAny<CancellationToken>())).Callback(() => order.Add("second")).Returns(Task.CompletedTask);
        var sut = new SeedCommandHandler(ScopesFor(_orchestrator.Object, first.Object, second.Object));

        await sut.HandleAsync(new SeedCommand(), CancellationToken.None);

        Assert.Equal(["first", "second"], order);
    }
}

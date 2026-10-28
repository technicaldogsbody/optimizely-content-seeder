using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAccess;
using Moq;
using TechnicalDogsbody.MicroMediator;
using TechnicalDogsbody.MicroMediator.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Extensions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;
using TechnicalDogsbody.Optimizely.ContentSeeder.UI;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.UI;

public class ContentSeederControllerTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private readonly Mock<ISeedTracker> _trackerMock = new();
    private readonly Mock<ISeedOrchestrator> _orchestratorMock = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock = new();
    private readonly Mock<IContentGenerationProvider> _textProviderMock = new();
    private readonly Mock<IImageContentFactory> _imageFactoryMock = new();
    private readonly SeedOperationMonitor _monitor = new();

    /// <summary>Waits for the background task started by the controller to finish.</summary>
    private async Task WaitForOperationToFinishAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (_monitor.Current.IsRunning)
            await Task.Delay(10, cts.Token);
    }

    private ContentSeederController CreateController(IContentGenerationProvider? textProvider = null, IImageContentFactory? imageFactory = null, ISectionSeeder? sections = null)
    {
        // Real MicroMediator wiring with the orchestrator mocked behind the handlers. ValidateScopes makes
        // a scoped handler resolved from the root provider fail the test.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_orchestratorMock.Object);
        services.AddSingleton(sections ?? new TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Orchestration.NoSectionSeeder());
        services.AddContentSeederMediator();
        var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(root);

        _scopeFactoryMock
            .Setup(f => f.CreateScope())
            .Returns(scopeMock.Object);

        _orchestratorMock
            .Setup(o => o.SeedAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _orchestratorMock
            .Setup(o => o.ReseedAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _orchestratorMock
            .Setup(o => o.DeleteAllAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _orchestratorMock
            .Setup(o => o.DryRunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DryRunResult([]));
        _orchestratorMock
            .Setup(o => o.GetChangedDefinitionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        _orchestratorMock.SetupGet(o => o.DefinitionIds).Returns(["demo-seed-v1"]);
        _trackerMock.Setup(t => t.GetDefinitionStatusesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _trackerMock.Setup(t => t.GetByDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var controller = new ContentSeederController(
            _trackerMock.Object,
            root.GetRequiredService<IMediator>(),
            textProvider ?? _textProviderMock.Object,
            imageFactory ?? _imageFactoryMock.Object,
            _scopeFactoryMock.Object,
            _monitor,
            new EditorContentGuard(new Mock<IContentRepository>().Object, _trackerMock.Object, []),
            NullLogger<ContentSeederController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    // ── GET /status ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetStatus_ReportsWhetherTheConnectorSupportsSections(bool supportsSections)
    {
        var sections = new Mock<ISectionSeeder>();
        sections.SetupGet(s => s.SupportsSections).Returns(supportsSections);
        var controller = CreateController(sections: sections.Object);
        _trackerMock.Setup(t => t.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentSeederStatus());

        var result = await controller.GetStatus(CancellationToken.None);

        var status = Assert.IsType<ContentSeederStatus>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(supportsSections, status.SupportsSections);
    }

    [Fact]
    public async Task GetStatus_ReturnsOkWithStatusAndCurrentOperation()
    {
        var trackerStatus = new ContentSeederStatus
        {
            TrackedPageCount = 5,
            TrackedBlockCount = 2,
            SeedOnStartup = false
        };

        var controller = CreateController();
        _trackerMock.Setup(t => t.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(trackerStatus);
        _monitor.TryBegin("Seed");

        var result = await controller.GetStatus(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var status = Assert.IsType<ContentSeederStatus>(ok.Value);
        Assert.Equal(5, status.TrackedPageCount);
        Assert.Equal(2, status.TrackedBlockCount);
        Assert.True(status.Operation.IsRunning);
        Assert.Equal("Seed", status.Operation.Name);
    }

    [Fact]
    public async Task GetStatus_IncludesChangedDefinitions()
    {
        var controller = CreateController();
        _trackerMock.Setup(t => t.GetStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContentSeederStatus());
        _orchestratorMock.Setup(o => o.GetChangedDefinitionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["demo-seed-v1"]);

        var result = await controller.GetStatus(CancellationToken.None);

        var status = Assert.IsType<ContentSeederStatus>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(["demo-seed-v1"], status.ChangedDefinitions);
    }

    [Fact]
    public async Task GetStatus_DescribesTheTextAndImageProviders()
    {
        // As<>() must be called before a mock's Object is first used.
        var text = new Mock<IContentGenerationProvider>();
        text.As<IDescribesProvider>().Setup(p => p.Describe()).Returns(new ProviderInfo("Ollama", "llama3.2:3b", IsAi: true));
        var images = new Mock<IImageContentFactory>();
        images.As<IDescribesProvider>().Setup(p => p.Describe()).Returns(new ProviderInfo("Picsum Photos"));
        var controller = CreateController(text.Object, images.Object);
        _trackerMock.Setup(t => t.GetStatusAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new ContentSeederStatus());

        var result = await controller.GetStatus(CancellationToken.None);

        var status = Assert.IsType<ContentSeederStatus>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(new ProviderInfo("Ollama", "llama3.2:3b", IsAi: true), status.Providers.Text);
        Assert.Equal("Picsum Photos", status.Providers.Images.Name);
    }

    [Fact]
    public async Task GetStatus_ProviderThatDoesNotDescribeItself_IsShownByTypeName()
    {
        var controller = CreateController();
        _trackerMock.Setup(t => t.GetStatusAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new ContentSeederStatus());

        var result = await controller.GetStatus(CancellationToken.None);

        var status = Assert.IsType<ContentSeederStatus>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.NotEmpty(status.Providers.Text.Name);
        Assert.False(status.Providers.Text.IsAi);
    }

    // ── POST /dryrun ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DryRun_ReturnsOkWithResult()
    {
        var dryRunResult = new DryRunResult(
            [new DryRunDefinitionResult("my-def", 10, 2)]);

        var controller = CreateController();

        // Override after CreateController so the default setup is replaced
        _orchestratorMock.Setup(o => o.DryRunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(dryRunResult);

        var result = await controller.DryRun(null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(dryRunResult, ok.Value);
    }

    // ── POST /seed, /reseed, /delete ──────────────────────────────────────────

    [Fact]
    public async Task Seed_ReturnsAcceptedAndRecordsSuccessfulOperation()
    {
        var controller = CreateController();

        var result = await controller.Seed(null, CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        await WaitForOperationToFinishAsync();
        Assert.Equal("Seed", _monitor.Current.Name);
        Assert.True(_monitor.Current.Succeeded);
        _orchestratorMock.Verify(o => o.SeedAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reseed_ReturnsAccepted()
    {
        var controller = CreateController();

        var result = await controller.Reseed(null, null, null, CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        await WaitForOperationToFinishAsync();
        Assert.Equal("Reseed", _monitor.Current.Name);
        _orchestratorMock.Verify(o => o.ReseedAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_ReturnsAccepted()
    {
        var controller = CreateController();

        var result = await controller.Delete(null, null, null, CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        await WaitForOperationToFinishAsync();
        Assert.Equal("Delete", _monitor.Current.Name);
        _orchestratorMock.Verify(o => o.DeleteAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Seed_WhenOrchestratorThrows_RecordsFailedOperation()
    {
        var controller = CreateController();
        _orchestratorMock.Setup(o => o.SeedAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await controller.Seed(null, CancellationToken.None);
        await WaitForOperationToFinishAsync();

        Assert.False(_monitor.Current.Succeeded);
        Assert.Equal("boom", _monitor.Current.Error);
        Assert.NotNull(_monitor.Current.CompletedUtc);
    }

    // ── 409 Conflict ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Seed_WhenAlreadyInProgress_ReturnsConflict()
    {
        var controller = CreateController();

        // Block the background operation so the slot stays held. Set up after CreateController,
        // which would otherwise replace this with an immediately-completing task.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _orchestratorMock.Setup(o => o.SeedAsync(It.IsAny<CancellationToken>()))
            .Returns(tcs.Task);

        var first = await controller.Seed(null, CancellationToken.None);
        Assert.IsType<AcceptedResult>(first);

        var second = await controller.Delete(null, null, null, CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(second);

        tcs.SetResult(true);
        await WaitForOperationToFinishAsync();
    }

    // ── definition-scoped operations ─────────────────────────────────────────

    [Fact]
    public async Task Seed_WithAKnownDefinition_SeedsOnlyThatDefinition()
    {
        var controller = CreateController();
        _orchestratorMock.Setup(o => o.SeedAsync("demo-seed-v1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await controller.Seed("demo-seed-v1", CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        await WaitForOperationToFinishAsync();
        Assert.Equal("Seed demo-seed-v1", _monitor.Current.Name);
        _orchestratorMock.Verify(o => o.SeedAsync("demo-seed-v1", It.IsAny<CancellationToken>()), Times.Once);
        _orchestratorMock.Verify(o => o.SeedAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Seed_WithAnUnknownDefinition_ReturnsBadRequestAndStartsNothing()
    {
        var controller = CreateController();

        var result = await controller.Seed("no-such-definition", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(_monitor.Current.IsRunning);
    }

    [Fact]
    public async Task Delete_WithAnUnknownDefinition_StillRuns()
    {
        // Content from a removed definition must remain deletable.
        var controller = CreateController();
        _orchestratorMock.Setup(o => o.DeleteAllAsync("removed-long-ago", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await controller.Delete("removed-long-ago", null, null, CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
    }
}

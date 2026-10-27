using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;
using System.Collections.Generic;
using System.Reflection;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.Security;
using EPiServer.VisualBuilder;
using Microsoft.Extensions.Logging.Abstractions;
using OptionsFactory = Microsoft.Extensions.Options.Options;
using Moq;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Infrastructure;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Orchestration;

public class SeedOrchestratorTests
{
    // ── test content types ────────────────────────────────────────────────────

    // PageData's identity setters normally write through to a property collection that a bare instance doesn't have.
    private sealed class FakePage : PageData
    {
        public override string Name { get; set; } = string.Empty;
        public override int ContentTypeID { get; set; }
        public override ContentReference ParentLink { get; set; } = ContentReference.EmptyReference;
        public override Guid ContentGuid { get; set; } = Guid.NewGuid();
        public override ContentReference ContentLink { get; set; } = ContentReference.EmptyReference;
    }
    private sealed class FakeBlock : BlockData { }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static Mock<IContentRepository> CreateContentRepositoryMock()
    {
        var mock = new Mock<IContentRepository>();

        // GetDefault returns a new instance of the right type
        mock.Setup(r => r.GetDefault<FakePage>(
                    It.IsAny<ContentReference>(),
                    It.IsAny<System.Globalization.CultureInfo>()))
            .Returns(() => new FakePage());

        // Save returns a ContentReference with a stable ID
        mock.Setup(r => r.Save(
                    It.IsAny<IContent>(),
                    It.IsAny<SaveAction>(),
                    It.IsAny<AccessLevel>()))
            .Returns(() => new ContentReference(Random.Shared.Next(100, 9999)));

        return mock;
    }

    private static Mock<ISeedTracker> CreateTrackerMock()
    {
        var mock = new Mock<ISeedTracker>();

        mock.Setup(t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mock.Setup(t => t.MarkCompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mock.Setup(t => t.GetByDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ContentSeederRecord>());

        mock.Setup(t => t.GetBlockByDefinitionIdAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentSeederRecord?)null);

        mock.Setup(t => t.TruncateAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return mock;
    }

    private static Mock<IContentTypeRepository> CreateContentTypeRepositoryMock()
    {
        var mock = new Mock<IContentTypeRepository>();
        mock.Setup(r => r.Load(It.IsAny<Type>()))
            .Returns(new ContentType { ID = 1 });
        return mock;
    }

    private static SeedOrchestrator CreateSut(
        IContentRepository? repo = null,
        ISeedTracker? tracker = null,
        IContentGenerationProvider? provider = null,
        ContentSeederOptions? opts = null,
        IEnumerable<ISeedDefinition>? definitions = null)
    {
        return new SeedOrchestrator(
            repo ?? CreateContentRepositoryMock().Object,
            Mock.Of<IContentLoader>(),
            CreateContentTypeRepositoryMock().Object,
            tracker ?? CreateTrackerMock().Object,
            provider ?? Mock.Of<IContentGenerationProvider>(
                p => p.PopulateAsync(It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(), It.IsAny<string>(), It.IsAny<Type>(), It.IsAny<IReadOnlyDictionary<string, (int Width, int Height)>>(), It.IsAny<CancellationToken>()) == Task.CompletedTask),
            Mock.Of<IImageContentFactory>(
                f => f.DeleteSeededImagesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()) == Task.CompletedTask),
            new NoSectionSeeder(),
            OptionsFactory.Create(opts ?? new ContentSeederOptions { MasterLanguage = "en" }),
            definitions ?? [],
            NullLogger<SeedOrchestrator>.Instance, new SeedDefinitionAccessor(), new NullVideoProvider(NullLogger<SeedOrchestrator>.Instance));
    }

    // ── empty tree ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeedAsync_EmptyDefinitionList_CompletesWithoutError()
    {
        var sut = CreateSut();

        var ex = await Record.ExceptionAsync(() => sut.SeedAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task SeedAsync_ZeroCountNode_ProducesNoTrackerInserts()
    {
        var trackerMock = CreateTrackerMock();

        var definition = new LambdaSeedDefinition("zero-count", () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 0)]));

        var sut = CreateSut(tracker: trackerMock.Object, definitions: [definition]);

        await sut.SeedAsync();

        trackerMock.Verify(
            t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── unknown BlockId ───────────────────────────────────────────────────────

    [Fact]
    public async Task SeedAsync_ContentAreaReferencesUnknownBlockId_ThrowsInvalidOperationException()
    {
        var node = new SeedNode(
            typeof(FakePage),
            count: 1,
            contentAreaAssignments:
            [
                new ContentAreaAssignment("HeroArea", ["non-existent-block"])
            ]);

        var definition = new LambdaSeedDefinition("bad-blockid", () =>
            new SeedTree(nodes: [node]));

        var sut = CreateSut(definitions: [definition]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SeedAsync());

        Assert.Contains("non-existent-block", ex.Message);
    }

    // ── cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SeedAsync_CancelledTokenBeforeFirstNode_ThrowsOperationCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var definition = new LambdaSeedDefinition("cancelled", () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 1)]));

        var sut = CreateSut(definitions: [definition]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.SeedAsync(cts.Token));
    }

    // ── DryRunAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DryRunAsync_NoExistingRecords_ReturnsAllItemsAsWouldCreate()
    {
        var definition = new LambdaSeedDefinition("dry-run-test", () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 3)]));

        var sut = CreateSut(definitions: [definition]);

        var result = await sut.DryRunAsync();

        Assert.Single(result.Definitions);
        Assert.Equal(3, result.Definitions[0].WouldCreate);
        Assert.Equal(0, result.Definitions[0].WouldSkip);
    }

    [Fact]
    public async Task DryRunAsync_WritesNoRecordsToTracker()
    {
        var trackerMock = CreateTrackerMock();

        var definition = new LambdaSeedDefinition("dry-run-no-write", () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 2)]));

        var sut = CreateSut(tracker: trackerMock.Object, definitions: [definition]);

        await sut.DryRunAsync();

        trackerMock.Verify(
            t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── DeleteAllAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAllAsync_CallsTruncateAsync()
    {
        var trackerMock = CreateTrackerMock();

        var sut = CreateSut(tracker: trackerMock.Object);

        await sut.DeleteAllAsync();

        trackerMock.Verify(t => t.TruncateAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── helper seed definition ────────────────────────────────────────────────

    private sealed class LambdaSeedDefinition(string id, Func<SeedTree> build) : ISeedDefinition
    {
        public string Id => id;
        public SeedTree Build() => build();
    }
}

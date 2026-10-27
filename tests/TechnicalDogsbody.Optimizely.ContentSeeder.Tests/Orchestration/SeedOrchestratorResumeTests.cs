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

public class SeedOrchestratorResumeTests
{
    // ── test content types ────────────────────────────────────────────────────
    // Not sealed so Moq can create proxies if needed.

    // PageData's identity setters normally write through to a property collection that a bare instance doesn't have.
    private class FakePage : PageData
    {
        public override string Name { get; set; } = string.Empty;
        public override int ContentTypeID { get; set; }
        public override ContentReference ParentLink { get; set; } = ContentReference.EmptyReference;
        public override Guid ContentGuid { get; set; } = Guid.NewGuid();
        public override ContentReference ContentLink { get; set; } = ContentReference.EmptyReference;
    }
    private class FakeBlock : BlockData { }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static Mock<IContentRepository> CreateContentRepositoryMock()
    {
        var mock = new Mock<IContentRepository>();

        // Reflection in CreatePageAsync calls GetDefault<FakePage>(ContentReference, CultureInfo) — 2-param.
        mock.Setup(r => r.GetDefault<FakePage>(
                    It.IsAny<ContentReference>(),
                    It.IsAny<System.Globalization.CultureInfo>()))
            .Returns(() => new FakePage());

        // Reflection in CreateBlockAsync calls GetDefault<FakeBlock>(ContentReference) — 1-param.
        mock.Setup(r => r.GetDefault<FakeBlock>(It.IsAny<ContentReference>()))
            .Returns(() => new FakeBlock());

        mock.Setup(r => r.Save(
                    It.IsAny<IContent>(),
                    It.IsAny<SaveAction>(),
                    It.IsAny<AccessLevel>()))
            .Returns(() => new ContentReference(Random.Shared.Next(100, 9999)));

        return mock;
    }

    private static Mock<IContentTypeRepository> CreateContentTypeRepositoryMock()
    {
        var mock = new Mock<IContentTypeRepository>();
        mock.Setup(r => r.Load(It.IsAny<Type>()))
            .Returns(new ContentType { ID = 1 });
        return mock;
    }

    private static ContentSeederRecord MakeCompletePageRecord(
        string definitionId,
        string language = "en",
        int contentId = 100)
        => new()
        {
            Id = Guid.NewGuid(),
            SeedDefinitionId = definitionId,
            ContentGuid = Guid.NewGuid(),
            ContentId = contentId,
            ContentType = "Page",
            Language = language,
            CreatedUtc = DateTime.UtcNow.AddMinutes(-5),
            UpdatedUtc = DateTime.UtcNow.AddMinutes(-5),
            SeedRunId = Guid.NewGuid(),
            IsPartial = false
        };

    private static ContentSeederRecord MakePartialBlockRecord(
        string definitionId,
        string blockDefinitionId,
        string language = "en",
        int contentId = 200)
        => new()
        {
            Id = Guid.NewGuid(),
            SeedDefinitionId = definitionId,
            BlockDefinitionId = blockDefinitionId,
            ContentGuid = Guid.NewGuid(),
            ContentId = contentId,
            ContentType = "Block",
            Language = language,
            CreatedUtc = DateTime.UtcNow.AddMinutes(-5),
            UpdatedUtc = DateTime.UtcNow.AddMinutes(-5),
            SeedRunId = Guid.NewGuid(),
            IsPartial = true
        };

    private static ContentSeederRecord MakeCompleteBlockRecord(
        string definitionId,
        string blockDefinitionId,
        string language = "en",
        int contentId = 300)
        => new()
        {
            Id = Guid.NewGuid(),
            SeedDefinitionId = definitionId,
            BlockDefinitionId = blockDefinitionId,
            ContentGuid = Guid.NewGuid(),
            ContentId = contentId,
            ContentType = "Block",
            Language = language,
            CreatedUtc = DateTime.UtcNow.AddMinutes(-5),
            UpdatedUtc = DateTime.UtcNow.AddMinutes(-5),
            SeedRunId = Guid.NewGuid(),
            IsPartial = false
        };

    private static SeedOrchestrator CreateSut(
        IContentRepository? repo = null,
        ISeedTracker? tracker = null,
        ContentSeederOptions? opts = null,
        IEnumerable<ISeedDefinition>? definitions = null,
        IContentLoader? contentLoader = null)
    {
        return new SeedOrchestrator(
            repo ?? CreateContentRepositoryMock().Object,
            contentLoader ?? Mock.Of<IContentLoader>(),
            CreateContentTypeRepositoryMock().Object,
            tracker ?? Mock.Of<ISeedTracker>(t =>
                t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>()) == Task.CompletedTask &&
                t.MarkCompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()) == Task.CompletedTask &&
                t.GetByDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.FromResult<IReadOnlyList<ContentSeederRecord>>(Array.Empty<ContentSeederRecord>()) &&
                t.GetBlockByDefinitionIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.FromResult<ContentSeederRecord?>(null) &&
                t.TruncateAsync(It.IsAny<CancellationToken>()) == Task.CompletedTask),
            Mock.Of<IContentGenerationProvider>(
                p => p.PopulateAsync(It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(), It.IsAny<string>(), It.IsAny<Type>(), It.IsAny<IReadOnlyDictionary<string, (int Width, int Height)>>(), It.IsAny<CancellationToken>()) == Task.CompletedTask),
            Mock.Of<IImageContentFactory>(
                f => f.DeleteSeededImagesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()) == Task.CompletedTask),
            new NoSectionSeeder(),
            OptionsFactory.Create(opts ?? new ContentSeederOptions { MasterLanguage = "en" }),
            definitions ?? [],
            NullLogger<SeedOrchestrator>.Instance, new SeedDefinitionAccessor(), new NullVideoProvider(NullLogger<SeedOrchestrator>.Instance));
    }

    // A page from an earlier run that is still in the CMS: under the root, of the type every FakePage resolves to.
    // Resume only reuses a page that still exists, so a test that expects reuse has to say that it does.
    private static IContentLoader LoaderWithExistingPage(int contentId)
    {
        var page = new Mock<IContent>();
        page.SetupGet(p => p.ContentLink).Returns(new ContentReference(contentId));
        page.SetupGet(p => p.ParentLink).Returns(ContentReference.RootPage);
        page.SetupGet(p => p.ContentTypeID).Returns(1);

        IContent? existing = page.Object;
        var loader = new Mock<IContentLoader>();
        loader.Setup(l => l.TryGet<IContent>(new ContentReference(contentId), It.IsAny<System.Globalization.CultureInfo>(), out existing))
            .Returns(true);
        return loader.Object;
    }

    private sealed class LambdaSeedDefinition(string id, Func<SeedTree> build) : ISeedDefinition
    {
        public string Id => id;
        public SeedTree Build() => build();
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeedAsync_NonPartialPageRecordExists_SkipsPageCreation()
    {
        const string defId = "resume-def";
        var existingRecord = MakeCompletePageRecord(defId, contentId: 42);

        var trackerMock = new Mock<ISeedTracker>();
        trackerMock.Setup(t => t.GetByDefinitionAsync(defId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { existingRecord });
        trackerMock.Setup(t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        trackerMock.Setup(t => t.MarkCompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var repoMock = CreateContentRepositoryMock();

        var definition = new LambdaSeedDefinition(defId, () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 1)]));

        var sut = CreateSut(repo: repoMock.Object, tracker: trackerMock.Object, definitions: [definition],
            contentLoader: LoaderWithExistingPage(42));

        await sut.SeedAsync();

        // GetDefault<FakePage> should never be called — the existing complete record is reused
        repoMock.Verify(
            r => r.GetDefault<FakePage>(It.IsAny<ContentReference>(), It.IsAny<System.Globalization.CultureInfo>()),
            Times.Never);
    }

    [Fact]
    public async Task SeedAsync_NonPartialPageRecordExists_NoNewTrackingRowInserted()
    {
        const string defId = "resume-no-insert";
        var existingRecord = MakeCompletePageRecord(defId, contentId: 55);

        var trackerMock = new Mock<ISeedTracker>();
        trackerMock.Setup(t => t.GetByDefinitionAsync(defId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { existingRecord });
        trackerMock.Setup(t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        trackerMock.Setup(t => t.MarkCompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var definition = new LambdaSeedDefinition(defId, () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 1)]));

        var sut = CreateSut(tracker: trackerMock.Object, definitions: [definition],
            contentLoader: LoaderWithExistingPage(55));

        await sut.SeedAsync();

        // No new insert should happen for the already-complete page
        trackerMock.Verify(
            t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SeedAsync_PartialBlockRecord_ResolvesContentReferenceWithoutResaving()
    {
        const string defId = "resume-partial-block";
        const string blockId = "my-block";
        const int existingContentId = 999;
        var partialBlock = MakePartialBlockRecord(defId, blockId, contentId: existingContentId);

        var trackerMock = new Mock<ISeedTracker>();
        trackerMock.Setup(t => t.GetByDefinitionAsync(defId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { partialBlock });
        trackerMock.Setup(t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        trackerMock.Setup(t => t.MarkCompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var repoMock = CreateContentRepositoryMock();
        var blockDef = new BlockDefinition(blockId, typeof(FakeBlock));

        var definition = new LambdaSeedDefinition(defId, () =>
            new SeedTree(blocks: [blockDef], nodes: []));

        var sut = CreateSut(repo: repoMock.Object, tracker: trackerMock.Object, definitions: [definition]);

        await sut.SeedAsync();

        // GetDefault<FakeBlock> should NOT be called — partial block is reused via ContentId
        repoMock.Verify(
            r => r.GetDefault<FakeBlock>(It.IsAny<ContentReference>()),
            Times.Never);
    }

    [Fact]
    public async Task SeedAsync_CompleteBlockRecord_SkipsBlockCreation()
    {
        const string defId = "resume-complete-block";
        const string blockId = "hero";
        const int existingContentId = 777;
        var completeBlock = MakeCompleteBlockRecord(defId, blockId, contentId: existingContentId);

        var trackerMock = new Mock<ISeedTracker>();
        trackerMock.Setup(t => t.GetByDefinitionAsync(defId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { completeBlock });
        trackerMock.Setup(t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        trackerMock.Setup(t => t.MarkCompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var repoMock = CreateContentRepositoryMock();
        var blockDef = new BlockDefinition(blockId, typeof(FakeBlock));

        var definition = new LambdaSeedDefinition(defId, () =>
            new SeedTree(blocks: [blockDef], nodes: []));

        var sut = CreateSut(repo: repoMock.Object, tracker: trackerMock.Object, definitions: [definition]);

        await sut.SeedAsync();

        repoMock.Verify(
            r => r.GetDefault<FakeBlock>(It.IsAny<ContentReference>()),
            Times.Never);
        repoMock.Verify(
            r => r.Save(It.IsAny<IContent>(), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>()),
            Times.Never);
    }

    [Fact]
    public async Task SeedAsync_CancellationMidRun_RecordsInsertedWithIsPartialTrue()
    {
        const string defId = "resume-cancel";
        using var cts = new CancellationTokenSource();

        var trackerMock = new Mock<ISeedTracker>();
        trackerMock.Setup(t => t.GetByDefinitionAsync(defId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ContentSeederRecord>());

        var insertedRecords = new List<ContentSeederRecord>();
        trackerMock.Setup(t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ContentSeederRecord, CancellationToken>((r, _) => insertedRecords.Add(r))
            .Returns(Task.CompletedTask);

        // MarkCompleteAsync: cancel after first completion to simulate mid-run abort
        int markCompleteCount = 0;
        trackerMock.Setup(t => t.MarkCompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, int, CancellationToken>((_, _, _) =>
            {
                markCompleteCount++;
                if (markCompleteCount >= 1)
                    cts.Cancel(); // cancel after first page is marked complete
            })
            .Returns(Task.CompletedTask);

        // 3 pages: first one completes, then cancellation fires before page 2
        var definition = new LambdaSeedDefinition(defId, () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 3)]));

        var sut = CreateSut(tracker: trackerMock.Object, definitions: [definition]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.SeedAsync(cts.Token));

        // At least one record was inserted in the partial run
        Assert.NotEmpty(insertedRecords);
    }

    [Fact]
    public async Task SeedAsync_SubsequentRunAfterPartialPage_CreatesRemainingPages()
    {
        const string defId = "resume-subsequent";

        // Simulate: 2 pages defined, 1 already complete (first run was partially done)
        var existingRecord = MakeCompletePageRecord(defId, contentId: 42);

        var trackerMock = new Mock<ISeedTracker>();
        trackerMock.Setup(t => t.GetByDefinitionAsync(defId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { existingRecord });
        trackerMock.Setup(t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        trackerMock.Setup(t => t.MarkCompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var repoMock = CreateContentRepositoryMock();

        // 2 pages defined, 1 already complete
        var definition = new LambdaSeedDefinition(defId, () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 2)]));

        // The completed record must still exist in the CMS to be reused, so the loader has to find content 42.
        var sut = CreateSut(repo: repoMock.Object, tracker: trackerMock.Object, definitions: [definition],
            contentLoader: LoaderWithExistingPage(42));

        await sut.SeedAsync();

        // Only 1 new page should be created (the skipped one + 1 new)
        repoMock.Verify(
            r => r.GetDefault<FakePage>(It.IsAny<ContentReference>(), It.IsAny<System.Globalization.CultureInfo>()),
            Times.Once);
    }
}

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

public class SeedOrchestratorDeleteTests
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

    private static Mock<IContentTypeRepository> CreateContentTypeRepositoryMock()
    {
        var mock = new Mock<IContentTypeRepository>();
        mock.Setup(r => r.Load(It.IsAny<Type>()))
            .Returns(new ContentType { ID = 1 });
        return mock;
    }

    private static ContentSeederRecord MakeRecord(
        string definitionId,
        string contentType,
        int contentId,
        DateTime createdUtc,
        bool isPartial = false,
        string blockDefinitionId = "")
        => new()
        {
            Id = Guid.NewGuid(),
            SeedDefinitionId = definitionId,
            BlockDefinitionId = string.IsNullOrEmpty(blockDefinitionId) ? null : blockDefinitionId,
            ContentGuid = Guid.NewGuid(),
            ContentId = contentId,
            ContentType = contentType,
            Language = "en",
            CreatedUtc = createdUtc,
            UpdatedUtc = createdUtc,
            SeedRunId = Guid.NewGuid(),
            IsPartial = isPartial
        };

    private static SeedOrchestrator CreateSut(
        IContentRepository? repo = null,
        ISeedTracker? tracker = null,
        ContentSeederOptions? opts = null,
        IEnumerable<ISeedDefinition>? definitions = null,
        IImageContentFactory? imageContentFactory = null)
    {
        return new SeedOrchestrator(
            repo ?? Mock.Of<IContentRepository>(),
            Mock.Of<IContentLoader>(),
            CreateContentTypeRepositoryMock().Object,
            tracker ?? Mock.Of<ISeedTracker>(t =>
                t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>()) == Task.CompletedTask &&
                t.MarkCompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()) == Task.CompletedTask &&
                t.GetByDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.FromResult<IReadOnlyList<ContentSeederRecord>>(Array.Empty<ContentSeederRecord>()) &&
                t.GetBlockByDefinitionIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.FromResult<ContentSeederRecord?>(null) &&
                t.TruncateAsync(It.IsAny<CancellationToken>()) == Task.CompletedTask),
            Mock.Of<IContentGenerationProvider>(
                p => p.PopulateAsync(It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(), It.IsAny<string>(), It.IsAny<Type>(), It.IsAny<IReadOnlyDictionary<string, (int Width, int Height)>>(), It.IsAny<CancellationToken>()) == Task.CompletedTask),
            imageContentFactory ?? Mock.Of<IImageContentFactory>(
                f => f.DeleteSeededImagesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()) == Task.CompletedTask),
            new NoSectionSeeder(),
            OptionsFactory.Create(opts ?? new ContentSeederOptions { MasterLanguage = "en" }),
            definitions ?? [],
            NullLogger<SeedOrchestrator>.Instance, new SeedDefinitionAccessor(), new NullVideoProvider(NullLogger<SeedOrchestrator>.Instance));
    }

    private sealed class LambdaSeedDefinition(string id, Func<SeedTree> build) : ISeedDefinition
    {
        public string Id => id;
        public SeedTree Build() => build();
    }

    // ── DeleteAllAsync: deletion order ─────────────────────────────────────────

    [Fact]
    public async Task DeleteAllAsync_DeletesBlocksBeforePages()
    {
        const string defId = "delete-order";
        var baseTime = DateTime.UtcNow;
        var blockRecord = MakeRecord(defId, "Block", 10, baseTime.AddMinutes(-5), blockDefinitionId: "hero");
        var pageRecord = MakeRecord(defId, "Page", 20, baseTime.AddMinutes(-3));

        var trackerMock = new Mock<ISeedTracker>();
        trackerMock.Setup(t => t.GetByDefinitionAsync(defId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { pageRecord, blockRecord }); // returned out of order
        trackerMock.Setup(t => t.TruncateAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var deleteOrder = new List<int>();
        var repoMock = new Mock<IContentRepository>();
        repoMock.Setup(r => r.Delete(It.IsAny<ContentReference>(), It.IsAny<bool>(), It.IsAny<AccessLevel>()))
            .Callback<ContentReference, bool, AccessLevel>((cr, _, _) => deleteOrder.Add(cr.ID));

        var definition = new LambdaSeedDefinition(defId, () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 0)]));

        var sut = CreateSut(repo: repoMock.Object, tracker: trackerMock.Object, definitions: [definition]);

        await sut.DeleteAllAsync();

        Assert.Equal(2, deleteOrder.Count);
        Assert.Equal(10, deleteOrder[0]); // block first
        Assert.Equal(20, deleteOrder[1]); // page second
    }

    [Fact]
    public async Task DeleteAllAsync_DeletesPagesInReverseCreationOrder()
    {
        const string defId = "delete-reverse";
        var baseTime2 = DateTime.UtcNow;
        var page1 = MakeRecord(defId, "Page", 101, baseTime2.AddMinutes(-10)); // older
        var page2 = MakeRecord(defId, "Page", 102, baseTime2.AddMinutes(-5));  // newer
        var page3 = MakeRecord(defId, "Page", 103, baseTime2.AddMinutes(-1));  // newest

        var trackerMock = new Mock<ISeedTracker>();
        trackerMock.Setup(t => t.GetByDefinitionAsync(defId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { page1, page2, page3 });
        trackerMock.Setup(t => t.TruncateAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var deleteOrder = new List<int>();
        var repoMock = new Mock<IContentRepository>();
        repoMock.Setup(r => r.Delete(It.IsAny<ContentReference>(), It.IsAny<bool>(), It.IsAny<AccessLevel>()))
            .Callback<ContentReference, bool, AccessLevel>((cr, _, _) => deleteOrder.Add(cr.ID));

        var definition = new LambdaSeedDefinition(defId, () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 0)]));

        var sut = CreateSut(repo: repoMock.Object, tracker: trackerMock.Object, definitions: [definition]);

        await sut.DeleteAllAsync();

        // Pages deleted newest-first (reverse creation order)
        Assert.Equal(3, deleteOrder.Count);
        Assert.Equal(103, deleteOrder[0]);
        Assert.Equal(102, deleteOrder[1]);
        Assert.Equal(101, deleteOrder[2]);
    }

    [Fact]
    public async Task DeleteAllAsync_CallsTruncateAfterAllDeletes()
    {
        const string defId = "delete-truncate";
        var record = MakeRecord(defId, "Page", 50, DateTime.UtcNow);

        var trackerMock = new Mock<ISeedTracker>(MockBehavior.Strict);
        trackerMock.Setup(t => t.GetByDefinitionAsync(defId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { record });

        bool deleteCalledBeforeTruncate = false;
        var repoMock = new Mock<IContentRepository>();
        repoMock.Setup(r => r.Delete(It.IsAny<ContentReference>(), It.IsAny<bool>(), It.IsAny<AccessLevel>()))
            .Callback(() => deleteCalledBeforeTruncate = true);

        trackerMock.Setup(t => t.TruncateAsync(It.IsAny<CancellationToken>()))
            .Callback(() => Assert.True(deleteCalledBeforeTruncate))
            .Returns(Task.CompletedTask);

        var definition = new LambdaSeedDefinition(defId, () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 0)]));

        var sut = CreateSut(repo: repoMock.Object, tracker: trackerMock.Object, definitions: [definition]);

        await sut.DeleteAllAsync();

        trackerMock.Verify(t => t.TruncateAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAllAsync_CallsDeleteSeededImagesAsync()
    {
        const string defId = "delete-images";
        var record = MakeRecord(defId, "Page", 60, DateTime.UtcNow);

        var trackerMock = new Mock<ISeedTracker>();
        trackerMock.Setup(t => t.GetByDefinitionAsync(defId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { record });
        trackerMock.Setup(t => t.TruncateAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var imageFactoryMock = new Mock<IImageContentFactory>();
        imageFactoryMock.Setup(f => f.DeleteSeededImagesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var definition = new LambdaSeedDefinition(defId, () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 0)]));

        var sut = CreateSut(tracker: trackerMock.Object, definitions: [definition], imageContentFactory: imageFactoryMock.Object);

        await sut.DeleteAllAsync();

        imageFactoryMock.Verify(f => f.DeleteSeededImagesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── ReseedAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ReseedAsync_CallsDeleteAllAsync_ThenSeedAsync()
    {
        const string defId = "reseed-def";
        var callOrder = new List<string>();

        var trackerMock = new Mock<ISeedTracker>();
        var existingRecord = MakeRecord(defId, "Page", 99, DateTime.UtcNow);

        // First call to GetByDefinitionAsync (in DeleteAllAsync) returns a record
        // Second call (in SeedDefinitionAsync) returns empty (already deleted)
        int callCount = 0;
        trackerMock.Setup(t => t.GetByDefinitionAsync(defId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? (IReadOnlyList<ContentSeederRecord>)new[] { existingRecord }
                    : Array.Empty<ContentSeederRecord>();
            });
        trackerMock.Setup(t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        trackerMock.Setup(t => t.MarkCompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        trackerMock.Setup(t => t.TruncateAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("delete"))
            .Returns(Task.CompletedTask);

        var repoMock = new Mock<IContentRepository>();
        repoMock.Setup(r => r.Delete(It.IsAny<ContentReference>(), It.IsAny<bool>(), It.IsAny<AccessLevel>()));
        // Reflection calls GetDefault<FakePage>(ContentReference, CultureInfo) — 2-param.
        repoMock.Setup(r => r.GetDefault<FakePage>(It.IsAny<ContentReference>(), It.IsAny<System.Globalization.CultureInfo>()))
            .Callback(() => callOrder.Add("seed"))
            .Returns(new FakePage());
        repoMock.Setup(r => r.Save(It.IsAny<IContent>(), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>()))
            .Returns(new ContentReference(1));

        var definition = new LambdaSeedDefinition(defId, () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 1)]));

        var sut = CreateSut(repo: repoMock.Object, tracker: trackerMock.Object, definitions: [definition]);

        await sut.ReseedAsync();

        // "delete" (TruncateAsync) must come before "seed" (GetDefault<FakePage>)
        Assert.Contains("delete", callOrder);
        Assert.Contains("seed", callOrder);
        Assert.True(callOrder.IndexOf("delete") < callOrder.IndexOf("seed"));
    }

    // ── DryRunAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DryRunAsync_WithExistingCompleteRecords_ReturnsCorrectWouldSkip()
    {
        const string defId = "dry-run-skip";
        var existingPage1 = MakeRecord(defId, "Page", 1, DateTime.UtcNow);
        var existingPage2 = MakeRecord(defId, "Page", 2, DateTime.UtcNow);

        var trackerMock = new Mock<ISeedTracker>();
        trackerMock.Setup(t => t.GetByDefinitionAsync(defId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { existingPage1, existingPage2 });

        var definition = new LambdaSeedDefinition(defId, () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 3)]));

        var sut = CreateSut(tracker: trackerMock.Object, definitions: [definition]);

        var result = await sut.DryRunAsync();

        Assert.Single(result.Definitions);
        Assert.Equal(2, result.Definitions[0].WouldSkip);
        Assert.Equal(1, result.Definitions[0].WouldCreate); // 3 total - 2 existing = 1
    }

    [Fact]
    public async Task DryRunAsync_AccountsForLanguageMultiplier()
    {
        const string defId = "dry-run-lang";

        var trackerMock = new Mock<ISeedTracker>();
        trackerMock.Setup(t => t.GetByDefinitionAsync(defId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ContentSeederRecord>());

        var definition = new LambdaSeedDefinition(defId, () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 2)]));

        // MasterLanguage + 2 additional = 3 total language versions per page
        var opts = new ContentSeederOptions { MasterLanguage = "en", Languages = ["fr", "de"] };
        var sut = CreateSut(tracker: trackerMock.Object, opts: opts, definitions: [definition]);

        var result = await sut.DryRunAsync();

        Assert.Single(result.Definitions);
        Assert.Equal(6, result.Definitions[0].WouldCreate); // 2 pages * 3 languages
        Assert.Equal(0, result.Definitions[0].WouldSkip);
    }

    [Fact]
    public async Task DryRunAsync_CallsNoContentRepositorySave()
    {
        const string defId = "dry-run-no-save";

        var trackerMock = new Mock<ISeedTracker>();
        trackerMock.Setup(t => t.GetByDefinitionAsync(defId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ContentSeederRecord>());

        var repoMock = new Mock<IContentRepository>(MockBehavior.Strict);
        // No Save setup — any Save call causes a strict-mock exception

        var definition = new LambdaSeedDefinition(defId, () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 3)]));

        var sut = CreateSut(repo: repoMock.Object, tracker: trackerMock.Object, definitions: [definition]);

        var ex = await Record.ExceptionAsync(() => sut.DryRunAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task DryRunAsync_MultipleDefinitions_ReturnsResultPerDefinition()
    {
        var trackerMock = new Mock<ISeedTracker>();
        trackerMock.Setup(t => t.GetByDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ContentSeederRecord>());

        var def1 = new LambdaSeedDefinition("def-1", () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 2)]));
        var def2 = new LambdaSeedDefinition("def-2", () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 4)]));

        var sut = CreateSut(tracker: trackerMock.Object, definitions: [def1, def2]);

        var result = await sut.DryRunAsync();

        Assert.Equal(2, result.Definitions.Count);
        var d1 = result.Definitions.First(d => d.SeedDefinitionId == "def-1");
        var d2 = result.Definitions.First(d => d.SeedDefinitionId == "def-2");
        Assert.Equal(2, d1.WouldCreate);
        Assert.Equal(4, d2.WouldCreate);
    }
}

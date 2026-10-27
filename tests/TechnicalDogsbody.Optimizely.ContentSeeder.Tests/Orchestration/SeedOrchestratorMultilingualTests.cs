using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;
using System.Collections.Generic;
using System.Globalization;
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

public class SeedOrchestratorMultilingualTests
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
    // The CMS hands back content that is IContent (blocks are saved through IContentRepository.Save(IContent)), so the stub
    // has to be IContent too. A bare BlockData fails the seeder's "as IContent" check.
    private class FakeBlock : BlockData, IContent
    {
        public string Name { get; set; } = string.Empty;
        public ContentReference ContentLink { get; set; } = ContentReference.EmptyReference;
        public ContentReference ParentLink { get; set; } = ContentReference.EmptyReference;
        public Guid ContentGuid { get; set; } = Guid.NewGuid();
        public new int ContentTypeID { get; set; }
        public bool IsDeleted { get; set; }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static Mock<IContentRepository> CreateContentRepositoryMock()
    {
        var mock = new Mock<IContentRepository>(MockBehavior.Strict);

        // Reflection in CreatePageAsync calls GetDefault<FakePage>(ContentReference, CultureInfo) — 2-param.
        mock.Setup(r => r.GetDefault<FakePage>(
                    It.IsAny<ContentReference>(),
                    It.IsAny<CultureInfo>()))
            .Returns(() => new FakePage());

        // Reflection in CreateBlockAsync calls GetDefault<FakeBlock>(ContentReference) — 1-param (no language).
        mock.Setup(r => r.GetDefault<FakeBlock>(It.IsAny<ContentReference>()))
            .Returns(() => new FakeBlock());

        // Shared blocks are created in a per-seed folder, so the folder is fetched as a new content folder.
        mock.Setup(r => r.GetDefault<ContentFolder>(It.IsAny<ContentReference>()))
            .Returns(() => new ContentFolder());

        // Reflection in CreatePageLanguageBranchAsync calls CreateLanguageBranch<FakePage>.
        mock.Setup(r => r.CreateLanguageBranch<FakePage>(
                    It.IsAny<ContentReference>(),
                    It.IsAny<CultureInfo>()))
            .Returns(() => new FakePage());

        // Reflection in CreateBlockLanguageBranchAsync calls CreateLanguageBranch<FakeBlock>.
        mock.Setup(r => r.CreateLanguageBranch<FakeBlock>(
                    It.IsAny<ContentReference>(),
                    It.IsAny<CultureInfo>()))
            .Returns(() => new FakeBlock());

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
        ContentSeederOptions? opts = null,
        IEnumerable<ISeedDefinition>? definitions = null,
        IContentGenerationProvider? provider = null)
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
            OptionsFactory.Create(opts ?? new ContentSeederOptions { MasterLanguage = "en", Languages = ["fr", "de"] }),
            definitions ?? [],
            NullLogger<SeedOrchestrator>.Instance, new SeedDefinitionAccessor(), new NullVideoProvider(NullLogger<SeedOrchestrator>.Instance));
    }

    private sealed class LambdaSeedDefinition(string id, Func<SeedTree> build) : ISeedDefinition
    {
        public string Id => id;
        public SeedTree Build() => build();
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeedAsync_WithTwoAdditionalLanguages_CallsCreateLanguageBranchForEachPageAndLanguage()
    {
        var repoMock = CreateContentRepositoryMock();

        var definition = new LambdaSeedDefinition("multi-lang", () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 1)]));

        var sut = CreateSut(repo: repoMock.Object, definitions: [definition]);

        await sut.SeedAsync();

        // CreateLanguageBranch<FakePage> called once for each of "fr" and "de"
        repoMock.Verify(
            r => r.CreateLanguageBranch<FakePage>(
                It.IsAny<ContentReference>(), It.IsAny<CultureInfo>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task SeedAsync_WithTwoLanguages_MasterCreatedBeforeBranches()
    {
        var callOrder = new List<string>();
        var repoMock = CreateContentRepositoryMock();

        repoMock.Setup(r => r.GetDefault<FakePage>(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>()))
            .Returns(() =>
            {
                callOrder.Add("master");
                return new FakePage();
            });

        repoMock.Setup(r => r.CreateLanguageBranch<FakePage>(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>()))
            .Returns(() =>
            {
                callOrder.Add("branch");
                return new FakePage();
            });

        var definition = new LambdaSeedDefinition("order-test", () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 1)]));

        var sut = CreateSut(repo: repoMock.Object, definitions: [definition]);

        await sut.SeedAsync();

        Assert.Equal("master", callOrder[0]);
        Assert.All(callOrder.Skip(1), s => Assert.Equal("branch", s));
    }

    [Fact]
    public async Task SeedAsync_WithTwoAdditionalLanguages_ProducesThreeTrackingRowsPerPage()
    {
        var trackerMock = CreateTrackerMock();
        var insertedRecords = new List<ContentSeederRecord>();

        trackerMock.Setup(t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ContentSeederRecord, CancellationToken>((r, _) => insertedRecords.Add(r))
            .Returns(Task.CompletedTask);

        var definition = new LambdaSeedDefinition("lang-track", () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 1)]));

        var sut = CreateSut(tracker: trackerMock.Object, definitions: [definition]);

        await sut.SeedAsync();

        var pageRecords = insertedRecords.Where(r => r.ContentType == "Page").ToList();
        Assert.Equal(3, pageRecords.Count); // en + fr + de
    }

    [Fact]
    public async Task SeedAsync_EachLanguageVersionHasCorrectLanguageInTrackingRecord()
    {
        var trackerMock = CreateTrackerMock();
        var insertedRecords = new List<ContentSeederRecord>();

        trackerMock.Setup(t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ContentSeederRecord, CancellationToken>((r, _) => insertedRecords.Add(r))
            .Returns(Task.CompletedTask);

        var definition = new LambdaSeedDefinition("lang-value", () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 1)]));

        var sut = CreateSut(tracker: trackerMock.Object, definitions: [definition]);

        await sut.SeedAsync();

        var pageLanguages = insertedRecords
            .Where(r => r.ContentType == "Page")
            .Select(r => r.Language)
            .OrderBy(l => l)
            .ToList();

        Assert.Equal(["de", "en", "fr"], pageLanguages);
    }

    [Fact]
    public async Task SeedAsync_AdditionalLanguageBranches_DoNotCallContentLoaderForContentArea()
    {
        // ContentLoader.Get<PageData> is only called when ContentArea assignments exist on the
        // master version. For language branches there are no ContentArea assignments, so
        // ContentLoader should never be called for a node without ContentArea assignments.
        var repoMock = CreateContentRepositoryMock();
        var contentLoaderMock = new Mock<IContentLoader>();

        // No ContentArea assignments on this node
        var definition = new LambdaSeedDefinition("no-ca", () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 1)]));

        var sut = new SeedOrchestrator(
            repoMock.Object,
            contentLoaderMock.Object,
            CreateContentTypeRepositoryMock().Object,
            CreateTrackerMock().Object,
            Mock.Of<IContentGenerationProvider>(
                p => p.PopulateAsync(It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(), It.IsAny<string>(), It.IsAny<Type>(), It.IsAny<IReadOnlyDictionary<string, (int Width, int Height)>>(), It.IsAny<CancellationToken>()) == Task.CompletedTask),
            Mock.Of<IImageContentFactory>(
                f => f.DeleteSeededImagesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()) == Task.CompletedTask),
            new NoSectionSeeder(),
            OptionsFactory.Create(new ContentSeederOptions { MasterLanguage = "en", Languages = ["fr"] }),
            [definition],
            NullLogger<SeedOrchestrator>.Instance, new SeedDefinitionAccessor(), new NullVideoProvider(NullLogger<SeedOrchestrator>.Instance));

        await sut.SeedAsync();

        // ContentLoader.Get should not be called because there are no ContentArea assignments
        contentLoaderMock.Verify(
            l => l.Get<PageData>(It.IsAny<ContentReference>()),
            Times.Never);
    }

    [Fact]
    public async Task SeedAsync_SingleLanguageMode_NoBranchesCreated()
    {
        var repoMock = CreateContentRepositoryMock();

        var definition = new LambdaSeedDefinition("single-lang", () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 2)]));

        // No Languages configured
        var opts = new ContentSeederOptions { MasterLanguage = "en", Languages = [] };

        var sut = CreateSut(repo: repoMock.Object, opts: opts, definitions: [definition]);

        await sut.SeedAsync();

        repoMock.Verify(
            r => r.CreateLanguageBranch<FakePage>(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>()),
            Times.Never);
    }

    [Fact]
    public async Task SeedAsync_UnknownLocale_FallsBackToEnWithoutThrowing()
    {
        var providerMock = new Mock<IContentGenerationProvider>();
        var capturedLanguages = new List<string>();

        providerMock
            .Setup(p => p.PopulateAsync(
                It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(), It.IsAny<string>(),
                It.IsAny<Type>(), It.IsAny<IReadOnlyDictionary<string, (int Width, int Height)>>(), It.IsAny<CancellationToken>()))
            .Callback<object, IEnumerable<PropertyInfo>, string, Type?, IReadOnlyDictionary<string, (int Width, int Height)>?, CancellationToken>(
                (_, _, lang, _, _, _) => capturedLanguages.Add(lang))
            .Returns(Task.CompletedTask);

        var definition = new LambdaSeedDefinition("unknown-locale", () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 1)]));

        // "xx-UNKNOWN" is not in LocaleMap — BogusContentGenerationProvider would fall back to "en"
        // At the orchestrator level the language tag is passed as-is to PopulateAsync
        var opts = new ContentSeederOptions { MasterLanguage = "en", Languages = ["xx-UNKNOWN"] };

        var sut = CreateSut(
            opts: opts,
            definitions: [definition],
            provider: providerMock.Object);

        var ex = await Record.ExceptionAsync(() => sut.SeedAsync());

        Assert.Null(ex);
        Assert.Contains("xx-UNKNOWN", capturedLanguages);
    }

    [Fact]
    public async Task SeedAsync_WithBlocks_CreatesLanguageBranchForEachBlockAndLanguage()
    {
        var repoMock = CreateContentRepositoryMock();

        var blockDef = new BlockDefinition("hero", typeof(FakeBlock));
        var definition = new LambdaSeedDefinition("block-ml", () =>
            new SeedTree(blocks: [blockDef], nodes: []));

        var sut = CreateSut(repo: repoMock.Object, definitions: [definition]);

        await sut.SeedAsync();

        // CreateLanguageBranch<FakeBlock> called once for each of "fr" and "de"
        repoMock.Verify(
            r => r.CreateLanguageBranch<FakeBlock>(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task SeedAsync_NullMasterLanguage_SeedsWithFallbackEnLocale()
    {
        var repoMock = CreateContentRepositoryMock();

        var definition = new LambdaSeedDefinition("null-master", () =>
            new SeedTree(nodes: [new SeedNode(typeof(FakePage), count: 1)]));

        var opts = new ContentSeederOptions { MasterLanguage = null, Languages = [] };

        var sut = CreateSut(repo: repoMock.Object, opts: opts, definitions: [definition]);

        var ex = await Record.ExceptionAsync(() => sut.SeedAsync());

        Assert.Null(ex);
        repoMock.Verify(
            r => r.GetDefault<FakePage>(It.IsAny<ContentReference>(), It.Is<CultureInfo>(c => c.Name == "en")),
            Times.Once);
    }
}

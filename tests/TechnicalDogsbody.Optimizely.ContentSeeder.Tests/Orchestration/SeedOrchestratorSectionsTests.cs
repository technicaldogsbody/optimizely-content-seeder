using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.Security;
using EPiServer.VisualBuilder;
using EPiServer.VisualBuilder.Compositions;
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

/// <summary>
/// Covers the Visual Builder composition support: section blueprint creation, per-placement
/// instancing, resume, and delete cleanup. See SectionDefinition/SectionSlotAssignment for the DSL.
/// </summary>
public class SeedOrchestratorSectionsTests
{
    // ── test content types ────────────────────────────────────────────────────
    // Not sealed so Moq can create proxies if needed.
    //
    // EPiServer.Core.BlockData/ExperienceData/SectionData do NOT implement IContent directly —
    // in the real runtime, GetDefault<T> returns a Castle dynamic proxy that composes IContent
    // on top at runtime. Test doubles need to implement it explicitly since no such proxy exists here.

    // PageData's Name/ContentTypeID/ParentLink/ContentGuid/ContentLink setters normally write
    // through to an internal PropertyDataCollection keyed by names like "PageName"/"PageTypeID",
    // populated by EPiServer's real content factory (GetDefault<T>) — absent on a bare
    // `new FakeExperience()`, which throws "Property '...' does not exist". All five setters are
    // virtual, so override them with plain backing fields to bypass that mechanism entirely.
    private class FakeExperience : ExperienceData
    {
        public override string Name { get; set; } = string.Empty;
        public override int ContentTypeID { get; set; }
        public override ContentReference ParentLink { get; set; } = ContentReference.EmptyReference;
        public override Guid ContentGuid { get; set; } = Guid.NewGuid();
        public override ContentReference ContentLink { get; set; } = ContentReference.EmptyReference;
    }

    private class FakeSection : SectionData, IContent
    {
        public string Name { get; set; } = string.Empty;
        public ContentReference ContentLink { get; set; } = ContentReference.EmptyReference;
        public ContentReference ParentLink { get; set; } = ContentReference.EmptyReference;
        public Guid ContentGuid { get; set; } = Guid.NewGuid();
        public bool IsDeleted { get; set; }
    }

    private class FakeElement : BlockData, IContent
    {
        public string Name { get; set; } = string.Empty;
        public ContentReference ContentLink { get; set; } = ContentReference.EmptyReference;
        public ContentReference ParentLink { get; set; } = ContentReference.EmptyReference;
        public Guid ContentGuid { get; set; } = Guid.NewGuid();
        public bool IsDeleted { get; set; }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static Mock<IContentTypeRepository> CreateContentTypeRepositoryMock()
    {
        var mock = new Mock<IContentTypeRepository>();
        mock.Setup(r => r.Load(It.IsAny<Type>()))
            .Returns(new ContentType { ID = 1 });
        return mock;
    }

    // Moq does not reliably disambiguate multiple Get<T>/GetDefault<T>-style generic setups on the
    // same mock when they all match via It.IsAny<ContentReference>() — the last-registered setup
    // can "win" regardless of the caller's actual closed generic type argument. Using distinct,
    // specific ContentReference values (rather than It.IsAny) for each closed generic setup avoids
    // the ambiguity entirely.
    private static readonly ContentReference PageRef = new(500);
    private static readonly ContentReference SectionInstanceRef = new(600);

    private static Mock<IContentRepository> CreateContentRepositoryMock()
    {
        var mock = new Mock<IContentRepository>();

        mock.Setup(r => r.GetDefault<FakeExperience>(
                    It.IsAny<ContentReference>(), It.IsAny<System.Globalization.CultureInfo>()))
            .Returns(() => new FakeExperience());
        mock.Setup(r => r.GetDefault<FakeSection>(It.IsAny<ContentReference>()))
            .Returns(() => new FakeSection());
        mock.Setup(r => r.GetDefault<FakeElement>(It.IsAny<ContentReference>()))
            .Returns(() => new FakeElement());
        mock.Setup(r => r.GetDefault<ContentFolder>(It.IsAny<ContentReference>()))
            .Returns(() => new ContentFolder());

        mock.Setup(r => r.Save(It.IsAny<IContent>(), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>()))
            .Returns(PageRef);

        return mock;
    }

    private static Mock<IContentLoader> CreateContentLoaderMock(IContentData? sharedSectionInstanceContent = null)
    {
        var mock = new Mock<IContentLoader>();

        mock.Setup(l => l.Get<ExperienceData>(PageRef))
            .Returns(() => new FakeExperience());

        // Only the shared/leaf path re-loads via IContentLoader; the inline/structural path uses the
        // CreateInlineBlockAsync return value directly.
        if (sharedSectionInstanceContent is not null)
        {
            mock.Setup(l => l.Get<IContentData>(SectionInstanceRef))
                .Returns(sharedSectionInstanceContent);
        }

        return mock;
    }

    private static Mock<ISeedTracker> CreateTrackerMock(IReadOnlyList<ContentSeederRecord>? existingRecords = null)
    {
        var mock = new Mock<ISeedTracker>();

        mock.Setup(t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(t => t.MarkCompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(t => t.GetByDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRecords ?? Array.Empty<ContentSeederRecord>());
        mock.Setup(t => t.GetBlockByDefinitionIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentSeederRecord?)null);
        mock.Setup(t => t.TruncateAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return mock;
    }

    private static Mock<IBlueprintRepository> CreateBlueprintRepositoryMock(Guid? blueprintId = null)
    {
        var mock = new Mock<IBlueprintRepository>();
        mock.Setup(r => r.CreateAsync(It.IsAny<IContentData>(), It.IsAny<CreateBlueprintOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Blueprint { ID = blueprintId ?? Guid.NewGuid(), Content = Mock.Of<IContent>() });
        mock.Setup(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<DeleteBlueprintOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    // Structural sections and their inline elements both come from CreateInlineBlockAsync; a fresh
    // instance is returned per call. Leaf sections never touch the factory (they are shared content).
    private static Mock<IBlueprintContentFactory> CreateBlueprintContentFactoryMock(BlockData? inlineBlockToReturn = null)
    {
        var mock = new Mock<IBlueprintContentFactory>();
        mock.Setup(f => f.CreateInlineBlockAsync(
                    It.IsAny<Guid>(), It.IsAny<ContentReference>(), It.IsAny<CreateBlueprintInlineBlockOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => inlineBlockToReturn ?? new FakeSection());
        return mock;
    }

    // Saving a shared leaf section (a FakeElement) returns SectionInstanceRef, which the loader mock
    // resolves back to the saved content.
    private static Mock<IContentRepository> CreateRepoMockWithSharedLeafSave()
    {
        var mock = CreateContentRepositoryMock();
        mock.Setup(r => r.Save(It.Is<IContent>(c => c is FakeElement), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>()))
            .Returns(SectionInstanceRef);
        return mock;
    }

    private static SeedOrchestrator CreateSut(
        IContentRepository? repo = null,
        IContentLoader? contentLoader = null,
        ISeedTracker? tracker = null,
        IBlueprintRepository? blueprintRepository = null,
        IBlueprintContentFactory? blueprintContentFactory = null,
        ICompositionMapper? compositionMapper = null,
        IEnumerable<ISeedDefinition>? definitions = null,
        ISeedDefinitionAccessor? definitionAccessor = null,
        IContentGenerationProvider? generationProvider = null,
        ContentSeederOptions? seederOptions = null)
    {
        var repository = repo ?? CreateContentRepositoryMock().Object;
        var loader = contentLoader ?? CreateContentLoaderMock().Object;
        var types = CreateContentTypeRepositoryMock().Object;
        var seedTracker = tracker ?? CreateTrackerMock().Object;
        var generation = generationProvider ?? Mock.Of<IContentGenerationProvider>(
            p => p.PopulateAsync(It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(), It.IsAny<string>(), It.IsAny<Type>(), It.IsAny<IReadOnlyDictionary<string, (int Width, int Height)>>(), It.IsAny<CancellationToken>()) == Task.CompletedTask);
        var options = seederOptions ?? new ContentSeederOptions { MasterLanguage = "en" };
        var videoProvider = new NullVideoProvider(NullLogger<SeedOrchestrator>.Instance);
        var sections = TestSectionSeeder.Create(
            repository, loader, types, generation,
            blueprintRepository ?? CreateBlueprintRepositoryMock().Object,
            blueprintContentFactory ?? CreateBlueprintContentFactoryMock().Object,
            compositionMapper ?? Mock.Of<ICompositionMapper>(),
            seedTracker, options, videoProvider, NullLogger<SeedOrchestrator>.Instance);

        return new SeedOrchestrator(
            repository,
            loader,
            types,
            seedTracker,
            generation,
            Mock.Of<IImageContentFactory>(
                f => f.DeleteSeededImagesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()) == Task.CompletedTask),
            sections,
            OptionsFactory.Create(options),
            definitions ?? [],
            NullLogger<SeedOrchestrator>.Instance,
            definitionAccessor ?? new SeedDefinitionAccessor(),
            videoProvider);
    }

    private sealed class LambdaSeedDefinition(string id, Func<SeedTree> build) : ISeedDefinition
    {
        public string Id => id;
        public SeedTree Build() => build();
    }

    private static ContentSeederRecord MakeRecord(
        string definitionId,
        string contentType,
        int contentId,
        Guid contentGuid,
        bool isPartial = false,
        string? blockDefinitionId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            SeedDefinitionId = definitionId,
            BlockDefinitionId = blockDefinitionId,
            ContentGuid = contentGuid,
            ContentId = contentId,
            ContentType = contentType,
            Language = "en",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            SeedRunId = Guid.NewGuid(),
            IsPartial = isPartial
        };

    // ── blueprint creation ────────────────────────────────────────────────────

    [Fact]
    public async Task SeedAsync_WithSectionDefinition_CreatesBlueprintWithMatchingName()
    {
        const string defId = "section-blueprint-name";
        var blueprintMock = CreateBlueprintRepositoryMock();

        var definition = new LambdaSeedDefinition(defId, () =>
            new ExperienceSeedTree(sections: [new SectionDefinition("hero", typeof(FakeSection))]));

        var sut = CreateSut(blueprintRepository: blueprintMock.Object, definitions: [definition]);

        await sut.SeedAsync();

        blueprintMock.Verify(r => r.CreateAsync(
            It.IsAny<IContentData>(),
            It.Is<CreateBlueprintOptions>(o => o.Name == "hero" && o.RequiredAccess == AccessLevel.NoAccess),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SeedAsync_SectionWithRowsButNoPlacement_CallsPopulateSectionOnceWithEmptyRoot()
    {
        const string defId = "section-with-rows-unplaced";
        var mapperMock = new Mock<ICompositionMapper>();

        var rows = new RowDefinition[]
        {
            new(columns: [new ColumnDefinition(elements: [new ElementDefinition(typeof(FakeElement))])])
        };

        var definition = new LambdaSeedDefinition(defId, () =>
            new ExperienceSeedTree(sections: [new SectionDefinition("hero", typeof(FakeSection), rows: rows)]));

        var sut = CreateSut(compositionMapper: mapperMock.Object, definitions: [definition]);

        await sut.SeedAsync();

        // No SeedNode places "hero" on an Experience, so only the blueprint-creation call (with an
        // empty root, purely to set Layout.Type="grid") happens — the row tree is never baked in.
        mapperMock.Verify(m => m.PopulateSection(It.IsAny<SectionData>(), It.IsAny<SectionNode>()), Times.Once);
    }

    [Fact]
    public async Task SeedAsync_StructuralSectionWithRows_BuildsRowsOnTheSectionNode()
    {
        const string defId = "section-with-rows-placed";
        Composition? capturedComposition = null;
        var mapperMock = new Mock<ICompositionMapper>();
        mapperMock.Setup(m => m.Populate(It.IsAny<ExperienceData>(), It.IsAny<Composition>()))
            .Callback<ExperienceData, Composition>((_, c) => capturedComposition = c);

        var rows = new ISectionRowSource[]
        {
            new RowDefinition(columns: [new ColumnDefinition(elements: [new ElementDefinition(typeof(FakeElement), name: "Body")])])
        };

        var definition = new LambdaSeedDefinition(defId, () =>
            new ExperienceSeedTree(
                sections: [new SectionDefinition("hero", typeof(FakeSection), rows: rows)],
                nodes: [new ExperienceSeedNode(typeof(FakeExperience), count: 1, sections: [new SectionSlotAssignment("hero")])]));

        var sut = CreateSut(compositionMapper: mapperMock.Object, definitions: [definition]);

        await sut.SeedAsync();

        // The composition mapper rebuilds an inline section's layout from SectionNode.Nodes, so the
        // rows must exist on the node that AddSection returned - not just on the SectionData.
        Assert.NotNull(capturedComposition);
        var section = Assert.IsType<SectionNode>(Assert.Single(capturedComposition!.Nodes));
        var row = Assert.IsType<StructureNode>(Assert.Single(section.Nodes));
        var column = Assert.IsType<StructureNode>(Assert.Single(row.Nodes));
        var element = Assert.IsType<ComponentNode>(Assert.Single(column.Nodes));
        Assert.Equal("Body", element.Name);

        // PopulateSection runs only at blueprint creation (empty root, to set Layout.Type="grid").
        mapperMock.Verify(m => m.PopulateSection(It.IsAny<SectionData>(), It.IsAny<SectionNode>()), Times.Once);
    }

    [Fact]
    public async Task SeedAsync_RepeatingElement_CreatesOneRowPerColumnCountFromMinToMax()
    {
        const string defId = "section-repeating";
        Composition? capturedComposition = null;
        var mapperMock = new Mock<ICompositionMapper>();
        mapperMock.Setup(m => m.Populate(It.IsAny<ExperienceData>(), It.IsAny<Composition>()))
            .Callback<ExperienceData, Composition>((_, c) => capturedComposition = c);

        var rows = new ISectionRowSource[]
        {
            new RepeatingElementDefinition(typeof(FakeElement), name: "Rep", minColumns: 1, maxColumns: 3)
        };

        var definition = new LambdaSeedDefinition(defId, () =>
            new ExperienceSeedTree(
                sections: [new SectionDefinition("hero", typeof(FakeSection), rows: rows)],
                nodes: [new ExperienceSeedNode(typeof(FakeExperience), count: 1, sections: [new SectionSlotAssignment("hero")])]));

        var sut = CreateSut(compositionMapper: mapperMock.Object, definitions: [definition]);

        await sut.SeedAsync();

        Assert.NotNull(capturedComposition);
        var section = Assert.IsType<SectionNode>(Assert.Single(capturedComposition!.Nodes));

        // minColumns 1 .. maxColumns 3 => three rows holding 1, 2 and 3 columns.
        Assert.Equal([1, 2, 3], section.Nodes.Cast<StructureNode>().Select(r => r.Nodes.Count).ToArray());

        // Each column holds exactly one element, named "{Name} {row}.{column}".
        var names = section.Nodes.Cast<StructureNode>()
            .SelectMany(r => r.Nodes.Cast<StructureNode>())
            .Select(c => Assert.IsType<ComponentNode>(Assert.Single(c.Nodes)).Name)
            .ToArray();
        Assert.Equal(["Rep 1.1", "Rep 2.1", "Rep 2.2", "Rep 3.1", "Rep 3.2", "Rep 3.3"], names);
    }

    [Fact]
    public async Task SeedAsync_RepeatingElement_PassesRunningIndexToConfigure()
    {
        const string defId = "section-repeating-configure";
        var indexes = new List<int>();

        var rows = new ISectionRowSource[]
        {
            new RepeatingElementDefinition(typeof(FakeElement), minColumns: 1, maxColumns: 2,
                configure: (_, i) => indexes.Add(i))
        };

        var definition = new LambdaSeedDefinition(defId, () =>
            new ExperienceSeedTree(
                sections: [new SectionDefinition("hero", typeof(FakeSection), rows: rows)],
                nodes: [new ExperienceSeedNode(typeof(FakeExperience), count: 1, sections: [new SectionSlotAssignment("hero")])]));

        var sut = CreateSut(definitions: [definition]);

        await sut.SeedAsync();

        Assert.Equal([0, 1, 2], indexes);
    }

    [Fact]
    public async Task SeedAsync_ExistingCompleteSectionBlueprintRecord_SkipsCreatingBlueprintAgain()
    {
        const string defId = "section-resume";
        var existing = MakeRecord(defId, ContentSeederRecord.ContentTypes.SectionBlueprint, 0, Guid.NewGuid(), blockDefinitionId: "hero");
        var trackerMock = CreateTrackerMock([existing]);
        var blueprintMock = CreateBlueprintRepositoryMock();

        var definition = new LambdaSeedDefinition(defId, () =>
            new ExperienceSeedTree(sections: [new SectionDefinition("hero", typeof(FakeSection))]));

        var sut = CreateSut(tracker: trackerMock.Object, blueprintRepository: blueprintMock.Object, definitions: [definition]);

        await sut.SeedAsync();

        blueprintMock.Verify(r => r.CreateAsync(
            It.IsAny<IContentData>(), It.IsAny<CreateBlueprintOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── per-placement instancing ──────────────────────────────────────────────

    [Fact]
    public async Task SeedAsync_SectionPlacedOnTwoExperienceInstances_CallsCreateInlineBlockAsyncTwice()
    {
        const string defId = "section-per-placement";
        var factoryMock = CreateBlueprintContentFactoryMock();

        var definition = new LambdaSeedDefinition(defId, () =>
            new ExperienceSeedTree(
                sections: [new SectionDefinition("hero", typeof(FakeSection))],
                nodes: [new ExperienceSeedNode(typeof(FakeExperience), count: 2, sections: [new SectionSlotAssignment("hero")])]));

        var sut = CreateSut(
            blueprintContentFactory: factoryMock.Object,
            definitions: [definition]);

        await sut.SeedAsync();

        // Two independent Experience instances, each placing the same SectionDefinition,
        // must each get their own freshly-materialized inline instance — not a shared one.
        factoryMock.Verify(f => f.CreateInlineBlockAsync(
            It.IsAny<Guid>(), It.IsAny<ContentReference>(), It.IsAny<CreateBlueprintInlineBlockOptions>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task SeedAsync_SectionSlotWithStructuralSectionData_CallsAddSectionNotAddComponent()
    {
        const string defId = "section-slot-structural";
        var factoryMock = CreateBlueprintContentFactoryMock(inlineBlockToReturn: new FakeSection());
        Composition? capturedComposition = null;
        var mapperMock = new Mock<ICompositionMapper>();
        mapperMock.Setup(m => m.Populate(It.IsAny<ExperienceData>(), It.IsAny<Composition>()))
            .Callback<ExperienceData, Composition>((_, c) => capturedComposition = c);

        var definition = new LambdaSeedDefinition(defId, () =>
            new ExperienceSeedTree(
                sections: [new SectionDefinition("hero", typeof(FakeSection))],
                nodes: [new ExperienceSeedNode(typeof(FakeExperience), count: 1, sections: [new SectionSlotAssignment("hero")])]));

        var sut = CreateSut(
            blueprintContentFactory: factoryMock.Object,
            compositionMapper: mapperMock.Object,
            definitions: [definition]);

        await sut.SeedAsync();

        Assert.NotNull(capturedComposition);
        Assert.Single(capturedComposition!.Nodes);
        Assert.IsType<SectionNode>(capturedComposition.Nodes[0]);
    }

    [Fact]
    public async Task SeedAsync_SectionSlotWithLeafBlockData_CallsAddComponentNotAddSection()
    {
        const string defId = "section-slot-leaf";
        // Leaf block-as-section types (not SectionData) are shared content created once and
        // re-loaded via IContentLoader - they never touch the blueprint factory.
        var factoryMock = CreateBlueprintContentFactoryMock();
        var contentLoaderMock = CreateContentLoaderMock(sharedSectionInstanceContent: new FakeElement());
        Composition? capturedComposition = null;
        var mapperMock = new Mock<ICompositionMapper>();
        mapperMock.Setup(m => m.Populate(It.IsAny<ExperienceData>(), It.IsAny<Composition>()))
            .Callback<ExperienceData, Composition>((_, c) => capturedComposition = c);

        var definition = new LambdaSeedDefinition(defId, () =>
            new ExperienceSeedTree(
                sections: [new SectionDefinition("hero", typeof(FakeElement))],
                nodes: [new ExperienceSeedNode(typeof(FakeExperience), count: 1, sections: [new SectionSlotAssignment("hero")])]));

        var sut = CreateSut(
            repo: CreateRepoMockWithSharedLeafSave().Object,
            contentLoader: contentLoaderMock.Object,
            blueprintContentFactory: factoryMock.Object,
            compositionMapper: mapperMock.Object,
            definitions: [definition]);

        await sut.SeedAsync();

        factoryMock.Verify(f => f.CreateInstanceAsync(
            It.IsAny<Guid>(), It.IsAny<ContentReference>(), It.IsAny<CreateBlueprintContentInstanceOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
        factoryMock.Verify(f => f.CreateInlineBlockAsync(
            It.IsAny<Guid>(), It.IsAny<ContentReference>(), It.IsAny<CreateBlueprintInlineBlockOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.NotNull(capturedComposition);
        Assert.Single(capturedComposition!.Nodes);
        Assert.IsType<ComponentNode>(capturedComposition.Nodes[0]);
    }

    [Fact]
    public async Task SeedAsync_LeafSectionPlacedOnTwoExperiences_CreatesAndPublishesSharedContentOnce()
    {
        const string defId = "section-leaf-shared";
        var repoMock = CreateRepoMockWithSharedLeafSave();
        var contentLoaderMock = CreateContentLoaderMock(sharedSectionInstanceContent: new FakeElement());
        var trackerMock = CreateTrackerMock();
        var insertedRecords = new List<ContentSeederRecord>();
        trackerMock.Setup(t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ContentSeederRecord, CancellationToken>((r, _) => insertedRecords.Add(r))
            .Returns(Task.CompletedTask);

        var definition = new LambdaSeedDefinition(defId, () =>
            new ExperienceSeedTree(
                sections: [new SectionDefinition("hero", typeof(FakeElement))],
                nodes: [new ExperienceSeedNode(typeof(FakeExperience), count: 2, sections: [new SectionSlotAssignment("hero")])]));

        var sut = CreateSut(
            repo: repoMock.Object,
            contentLoader: contentLoaderMock.Object,
            tracker: trackerMock.Object,
            definitions: [definition]);

        await sut.SeedAsync();

        // Two pages reference one shared item: saved (and published) exactly once and tracked once.
        repoMock.Verify(r => r.Save(It.Is<IContent>(c => c is FakeElement), SaveAction.Publish, AccessLevel.NoAccess), Times.Once);
        Assert.Single(insertedRecords, r => r.ContentType == ContentSeederRecord.ContentTypes.SectionInstance);
        // The shared item is referenced from both pages.
        contentLoaderMock.Verify(l => l.Get<IContentData>(SectionInstanceRef), Times.Exactly(2));
    }

    [Fact]
    public async Task SeedAsync_ExistingCompleteSharedLeafSection_IsReusedNotRecreated()
    {
        const string defId = "section-leaf-resume";
        var existing = MakeRecord(
            defId, ContentSeederRecord.ContentTypes.SectionInstance, SectionInstanceRef.ID, Guid.NewGuid(), blockDefinitionId: "hero");
        var trackerMock = CreateTrackerMock([existing]);
        var repoMock = CreateRepoMockWithSharedLeafSave();
        var contentLoaderMock = CreateContentLoaderMock(sharedSectionInstanceContent: new FakeElement());

        var definition = new LambdaSeedDefinition(defId, () =>
            new ExperienceSeedTree(
                sections: [new SectionDefinition("hero", typeof(FakeElement))],
                nodes: [new ExperienceSeedNode(typeof(FakeExperience), count: 1, sections: [new SectionSlotAssignment("hero")])]));

        var sut = CreateSut(
            repo: repoMock.Object,
            contentLoader: contentLoaderMock.Object,
            tracker: trackerMock.Object,
            definitions: [definition]);

        await sut.SeedAsync();

        repoMock.Verify(r => r.Save(It.Is<IContent>(c => c is FakeElement), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>()), Times.Never);
        contentLoaderMock.Verify(l => l.Get<IContentData>(SectionInstanceRef), Times.Once);
    }

    // ── definition accessor ───────────────────────────────────────────────────

    private sealed class RecordingAccessor : ISeedDefinitionAccessor
    {
        public List<ISeedDefinition> Seen { get; } = [];
        public ISeedDefinition? Current
        {
            get => Seen.LastOrDefault();
            set { if (value is not null) Seen.Add(value); }
        }
    }

    [Fact]
    public async Task SeedAsync_SetsCurrentDefinitionForContentGenerationProviders()
    {
        var accessor = new RecordingAccessor();
        var first = new LambdaSeedDefinition("first", () => new ExperienceSeedTree());
        var second = new LambdaSeedDefinition("second", () => new ExperienceSeedTree());
        var sut = CreateSut(definitions: [first, second], definitionAccessor: accessor);

        await sut.SeedAsync();

        Assert.Equal([first, second], accessor.Seen);
    }

    // ── parallel generation ───────────────────────────────────────────────────

    // Stands in for an AI provider: each item takes a moment, and the most in flight at once is recorded.
    private sealed class SlowAiProvider : IContentGenerationProvider, IDescribesProvider
    {
        private int _inFlight;
        public int MaxInFlight { get; private set; }
        public ProviderInfo Describe() => new("Slow AI", "test", IsAi: true);

        public async Task PopulateAsync(object content, IEnumerable<PropertyInfo> properties, string language, Type? contentType = null,
            IReadOnlyDictionary<string, (int Width, int Height)>? imageSizes = null, CancellationToken ct = default)
        {
            int now = Interlocked.Increment(ref _inFlight);
            lock (this) MaxInFlight = Math.Max(MaxInFlight, now);
            await Task.Delay(40, ct);
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private static ExperienceSeedTree RepeatingTree() => new(
        sections: [new SectionDefinition("grid", typeof(FakeSection), rows:
            [new RepeatingElementDefinition(typeof(FakeElement), name: "Rep", minColumns: 1, maxColumns: 4)])],
        nodes: [new ExperienceSeedNode(typeof(FakeExperience), count: 1, sections: [new SectionSlotAssignment("grid")])]);

    [Fact]
    public async Task SeedAsync_WithAnAiProvider_GeneratesElementsInParallel_UpToTheLimit()
    {
        var provider = new SlowAiProvider();
        var sut = CreateSut(
            definitions: [new LambdaSeedDefinition("parallel", RepeatingTree)],
            generationProvider: provider,
            seederOptions: new ContentSeederOptions { MasterLanguage = "en", MaxParallelGeneration = 3 });

        await sut.SeedAsync();

        Assert.Equal(3, provider.MaxInFlight);
    }

    [Fact]
    public async Task SeedAsync_WithParallelGenerationSetToOne_GeneratesOneElementAtATime()
    {
        var provider = new SlowAiProvider();
        var sut = CreateSut(
            definitions: [new LambdaSeedDefinition("serial", RepeatingTree)],
            generationProvider: provider,
            seederOptions: new ContentSeederOptions { MasterLanguage = "en", MaxParallelGeneration = 1 });

        await sut.SeedAsync();

        Assert.Equal(1, provider.MaxInFlight);
    }

    [Fact]
    public async Task SeedAsync_ParallelGeneration_KeepsElementsInDefinitionOrder()
    {
        Composition? captured = null;
        var mapperMock = new Mock<ICompositionMapper>();
        mapperMock.Setup(m => m.Populate(It.IsAny<ExperienceData>(), It.IsAny<Composition>()))
            .Callback<ExperienceData, Composition>((_, c) => captured = c);

        var sut = CreateSut(
            compositionMapper: mapperMock.Object,
            definitions: [new LambdaSeedDefinition("ordered", RepeatingTree)],
            generationProvider: new SlowAiProvider(),
            seederOptions: new ContentSeederOptions { MasterLanguage = "en", MaxParallelGeneration = 4 });

        await sut.SeedAsync();

        var section = Assert.IsType<SectionNode>(Assert.Single(captured!.Nodes));
        var names = section.Nodes.Cast<StructureNode>()
            .SelectMany(r => r.Nodes.Cast<StructureNode>())
            .Select(c => Assert.IsType<ComponentNode>(Assert.Single(c.Nodes)).Name)
            .ToArray();
        Assert.Equal(10, names.Length);
        Assert.Equal("Rep 1.1", names[0]);
        Assert.Equal("Rep 4.4", names[^1]);
    }

    [Fact]
    public async Task SeedAsync_RepeatingElementWithReuseContent_GeneratesOnce_AndCopiesIt()
    {
        var generated = new List<object>();
        var provider = new Mock<IContentGenerationProvider>();
        provider.Setup(p => p.PopulateAsync(
                It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(), It.IsAny<string>(), It.IsAny<Type>(),
                It.IsAny<IReadOnlyDictionary<string, (int Width, int Height)>>(), It.IsAny<CancellationToken>()))
            .Callback<object, IEnumerable<PropertyInfo>, string, Type, IReadOnlyDictionary<string, (int, int)>, CancellationToken>(
                (content, _, _, _, _, _) => generated.Add(content))
            .Returns(Task.CompletedTask);

        var configuredIndexes = new List<int>();
        Composition? captured = null;
        var mapperMock = new Mock<ICompositionMapper>();
        mapperMock.Setup(m => m.Populate(It.IsAny<ExperienceData>(), It.IsAny<Composition>()))
            .Callback<ExperienceData, Composition>((_, c) => captured = c);

        var rows = new ISectionRowSource[]
        {
            new RepeatingElementDefinition(typeof(FakeElement), name: "Rep", minColumns: 1, maxColumns: 3, reuseContent: true,
                configure: (_, i) => configuredIndexes.Add(i))
        };
        var definition = new LambdaSeedDefinition("reuse", () => new ExperienceSeedTree(
            sections: [new SectionDefinition("grid", typeof(FakeSection), rows: rows)],
            nodes: [new ExperienceSeedNode(typeof(FakeExperience), count: 1, sections: [new SectionSlotAssignment("grid")])]));

        var sut = CreateSut(compositionMapper: mapperMock.Object, definitions: [definition], generationProvider: provider.Object);

        await sut.SeedAsync();

        // Six elements (rows of 1, 2 and 3) from a single generated one.
        Assert.Single(generated, c => c is FakeElement);
        var section = Assert.IsType<SectionNode>(Assert.Single(captured!.Nodes));
        var components = section.Nodes.Cast<StructureNode>().SelectMany(r => r.Nodes.Cast<StructureNode>())
            .Select(c => Assert.IsType<ComponentNode>(Assert.Single(c.Nodes))).ToList();
        Assert.Equal(6, components.Count);
        Assert.Equal(6, components.Select(c => c.Component).Distinct().Count()); // separate instances, not one shared object
        // configure still runs for every element so it can vary the copies.
        Assert.Equal([0, 1, 2, 3, 4, 5], configuredIndexes.OrderBy(i => i).ToArray());
    }

    // ── definition change detection ───────────────────────────────────────────

    private static ExperienceSeedTree ShapeTree(int maxColumns) => new(
        sections: [new SectionDefinition("content", typeof(FakeSection), rows:
            [new RepeatingElementDefinition(typeof(FakeElement), minColumns: 1, maxColumns: maxColumns)])]);

    [Fact]
    public async Task SeedAsync_FreshRun_RecordsDefinitionFingerprint()
    {
        const string defId = "fingerprint-fresh";
        var trackerMock = CreateTrackerMock();
        var definition = new LambdaSeedDefinition(defId, () => ShapeTree(3));
        var sut = CreateSut(tracker: trackerMock.Object, definitions: [definition]);

        await sut.SeedAsync();

        trackerMock.Verify(t => t.SetDefinitionFingerprintAsync(
            defId, SeedTreeFingerprint.Compute(ShapeTree(3), "en", Array.Empty<string>()), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SeedAsync_OverExistingContent_DoesNotReRecordFingerprint()
    {
        const string defId = "fingerprint-existing";
        var existing = MakeRecord(defId, ContentSeederRecord.ContentTypes.SectionBlueprint, 0, Guid.NewGuid(), blockDefinitionId: "content");
        var trackerMock = CreateTrackerMock([existing]);
        var definition = new LambdaSeedDefinition(defId, () => ShapeTree(6));
        var sut = CreateSut(tracker: trackerMock.Object, definitions: [definition]);

        await sut.SeedAsync();

        // Seed only fills gaps, so a changed shape must keep showing as changed until a Reseed.
        trackerMock.Verify(t => t.SetDefinitionFingerprintAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetChangedDefinitionsAsync_ShapeDiffersFromRecorded_ReturnsDefinition()
    {
        const string defId = "fingerprint-changed";
        var trackerMock = CreateTrackerMock();
        trackerMock.Setup(t => t.GetDefinitionFingerprintAsync(defId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedTreeFingerprint.Compute(ShapeTree(3), "en", []));
        var definition = new LambdaSeedDefinition(defId, () => ShapeTree(6));
        var sut = CreateSut(tracker: trackerMock.Object, definitions: [definition]);

        var changed = await sut.GetChangedDefinitionsAsync();

        Assert.Equal([defId], changed);
    }

    [Fact]
    public async Task GetChangedDefinitionsAsync_ShapeMatchesRecorded_ReturnsEmpty()
    {
        const string defId = "fingerprint-same";
        var trackerMock = CreateTrackerMock();
        trackerMock.Setup(t => t.GetDefinitionFingerprintAsync(defId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedTreeFingerprint.Compute(ShapeTree(3), "en", []));
        var definition = new LambdaSeedDefinition(defId, () => ShapeTree(3));
        var sut = CreateSut(tracker: trackerMock.Object, definitions: [definition]);

        Assert.Empty(await sut.GetChangedDefinitionsAsync());
    }

    [Fact]
    public async Task GetChangedDefinitionsAsync_NoRecordedFingerprint_ReturnsEmpty()
    {
        var trackerMock = CreateTrackerMock();
        trackerMock.Setup(t => t.GetDefinitionFingerprintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var definition = new LambdaSeedDefinition("fingerprint-unknown", () => ShapeTree(3));
        var sut = CreateSut(tracker: trackerMock.Object, definitions: [definition]);

        Assert.Empty(await sut.GetChangedDefinitionsAsync());
    }

    [Fact]
    public async Task DryRunAsync_ChangedDefinition_IsFlagged()
    {
        const string defId = "fingerprint-dryrun";
        var trackerMock = CreateTrackerMock();
        trackerMock.Setup(t => t.GetDefinitionFingerprintAsync(defId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SeedTreeFingerprint.Compute(ShapeTree(3), "en", []));
        var definition = new LambdaSeedDefinition(defId, () => ShapeTree(6));
        var sut = CreateSut(tracker: trackerMock.Object, definitions: [definition]);

        var result = await sut.DryRunAsync();

        Assert.True(Assert.Single(result.Definitions).DefinitionChanged);
    }

    // ── delete cleanup ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAllAsync_DeletesSectionBlueprintsViaBlueprintRepository()
    {
        const string defId = "delete-blueprint";
        var blueprintId = Guid.NewGuid();
        var record = MakeRecord(defId, ContentSeederRecord.ContentTypes.SectionBlueprint, 0, blueprintId, blockDefinitionId: "hero");

        var trackerMock = CreateTrackerMock([record]);
        var blueprintMock = CreateBlueprintRepositoryMock();

        var definition = new LambdaSeedDefinition(defId, () => new ExperienceSeedTree());
        var sut = CreateSut(tracker: trackerMock.Object, blueprintRepository: blueprintMock.Object, definitions: [definition]);

        await sut.DeleteAllAsync();

        blueprintMock.Verify(r => r.DeleteAsync(
            blueprintId,
            It.Is<DeleteBlueprintOptions>(o => o.RequiredAccess == AccessLevel.NoAccess),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteAllAsync_DeletesSharedSectionInstancesLikeBlocks()
    {
        const string defId = "delete-section-instance";
        var record = MakeRecord(defId, ContentSeederRecord.ContentTypes.SectionInstance, 55, Guid.NewGuid(), blockDefinitionId: "hero");

        var trackerMock = CreateTrackerMock([record]);
        var repoMock = CreateContentRepositoryMock();
        var deletedIds = new List<int>();
        repoMock.Setup(r => r.Delete(It.IsAny<ContentReference>(), It.IsAny<bool>(), It.IsAny<AccessLevel>()))
            .Callback<ContentReference, bool, AccessLevel>((cr, _, _) => deletedIds.Add(cr.ID));

        var definition = new LambdaSeedDefinition(defId, () => new ExperienceSeedTree());
        var sut = CreateSut(repo: repoMock.Object, tracker: trackerMock.Object, definitions: [definition]);

        await sut.DeleteAllAsync();

        Assert.Contains(55, deletedIds);
    }

    // ── dry run ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task DryRunAsync_CountsAStructuralSectionOnceWhateverTheLanguages()
    {
        // A structural section is a blueprint, created once. Counting it per language made a definition that was fully
        // seeded still report items to create, one for each extra language.
        const string defId = "dry-run-structural";
        var definition = new LambdaSeedDefinition(defId, () =>
            new ExperienceSeedTree(sections: [new SectionDefinition("hero", typeof(FakeSection))]));
        var languages = new ContentSeederOptions { MasterLanguage = "en", Languages = ["fr", "de"] };

        var fresh = await CreateSut(definitions: [definition], seederOptions: languages).DryRunAsync();
        Assert.Equal(1, fresh.Definitions[0].WouldCreate);

        var blueprint = MakeRecord(defId, ContentSeederRecord.ContentTypes.SectionBlueprint, 0, Guid.NewGuid(), blockDefinitionId: "hero");
        var seeded = await CreateSut(tracker: CreateTrackerMock([blueprint]).Object, definitions: [definition], seederOptions: languages).DryRunAsync();
        Assert.Equal(0, seeded.Definitions[0].WouldCreate);
    }

    [Fact]
    public async Task DryRunAsync_ReportsEachKindWithItsPlannedAndExistingCounts()
    {
        const string defId = "dry-run-kinds";
        var definition = new LambdaSeedDefinition(defId, () => new ExperienceSeedTree(
            blocks: [new BlockDefinition("promo", typeof(FakeElement))],
            sections: [new SectionDefinition("hero", typeof(FakeSection))],
            nodes: [new ExperienceSeedNode(typeof(FakeExperience), count: 2)]));

        // One page already seeded, and the blueprint too.
        var existing = new[]
        {
            MakeRecord(defId, ContentSeederRecord.ContentTypes.Page, 0, Guid.NewGuid()),
            MakeRecord(defId, ContentSeederRecord.ContentTypes.SectionBlueprint, 0, Guid.NewGuid(), blockDefinitionId: "hero"),
        };
        var opts = new ContentSeederOptions { MasterLanguage = "en", Languages = ["fr"] };
        var sut = CreateSut(tracker: CreateTrackerMock(existing).Object, definitions: [definition], seederOptions: opts);

        var result = (await sut.DryRunAsync()).Definitions.Single();
        var kinds = result.Kinds.ToDictionary(k => k.Kind);

        Assert.Equal(["en", "fr"], result.Languages);
        Assert.Equal(new DryRunKindCount(DryRunKinds.Pages, Planned: 4, AlreadySeeded: 1, WouldCreate: 3), kinds[DryRunKinds.Pages]);
        Assert.Equal(new DryRunKindCount(DryRunKinds.BlocksAndSharedSections, Planned: 2, AlreadySeeded: 0, WouldCreate: 2), kinds[DryRunKinds.BlocksAndSharedSections]);
        Assert.Equal(new DryRunKindCount(DryRunKinds.SectionBlueprints, Planned: 1, AlreadySeeded: 1, WouldCreate: 0), kinds[DryRunKinds.SectionBlueprints]);
        Assert.Equal(5, result.WouldCreate);
        Assert.False(result.HasPartialRun);
    }

    [Fact]
    public async Task DryRunAsync_FlagsAnInterruptedRun()
    {
        const string defId = "dry-run-partial";
        var definition = new LambdaSeedDefinition(defId, () => new ExperienceSeedTree(nodes: [new ExperienceSeedNode(typeof(FakeExperience), count: 1)]));
        var partial = MakeRecord(defId, ContentSeederRecord.ContentTypes.Page, 0, Guid.NewGuid(), isPartial: true);
        var sut = CreateSut(tracker: CreateTrackerMock([partial]).Object, definitions: [definition]);

        var result = (await sut.DryRunAsync()).Definitions.Single();

        Assert.True(result.HasPartialRun);
    }
}

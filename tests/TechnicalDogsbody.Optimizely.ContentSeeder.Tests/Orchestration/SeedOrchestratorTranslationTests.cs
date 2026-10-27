using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;
using System.Globalization;
using System.Reflection;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.DataAnnotations;
using EPiServer.Security;
using EPiServer.VisualBuilder;
using EPiServer.VisualBuilder.Compositions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Infrastructure;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;
using Xunit;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Orchestration;

/// <summary>
/// Additional languages: translated from the saved master-language content when the provider can
/// translate, generated from scratch (as before) when it can't — for pages, blocks, shared sections, and
/// the Visual Builder composition of Experience pages.
/// </summary>
public class SeedOrchestratorTranslationTests
{
    // Same trick as the sections tests: override the PageData setters that normally write through an
    // internal property collection populated by EPiServer's real content factory.
    private class FakePage : PageData
    {
        public override string Name { get; set; } = string.Empty;
        public override int ContentTypeID { get; set; }
        public override ContentReference ParentLink { get; set; } = ContentReference.EmptyReference;
        public override Guid ContentGuid { get; set; } = Guid.NewGuid();
        public override ContentReference ContentLink { get; set; } = ContentReference.EmptyReference;

        [CultureSpecific]
        public virtual string? Heading { get; set; }
    }

    private class FakeExperience : ExperienceData
    {
        public override string Name { get; set; } = string.Empty;
        public override int ContentTypeID { get; set; }
        public override ContentReference ParentLink { get; set; } = ContentReference.EmptyReference;
        public override Guid ContentGuid { get; set; } = Guid.NewGuid();
        public override ContentReference ContentLink { get; set; } = ContentReference.EmptyReference;
    }

    private class FakeBlock : BlockData, IContent
    {
        public string Name { get; set; } = string.Empty;
        public ContentReference ContentLink { get; set; } = ContentReference.EmptyReference;
        public ContentReference ParentLink { get; set; } = ContentReference.EmptyReference;
        public Guid ContentGuid { get; set; } = Guid.NewGuid();
        public bool IsDeleted { get; set; }

        [CultureSpecific]
        public virtual string? Title { get; set; }

        // Not culture specific, so it is shared from the master and must never be written on a branch.
        public virtual ContentReference? Picture { get; set; }
    }

    // An inline element / section lives inside its page and has no identity of its own.
    private class FakeInlineElement : BlockData
    {
        public virtual string? Text { get; set; }
    }

    private class FakeInlineSection : SectionData
    {
    }

    private sealed class LambdaSeedDefinition(string id, Func<SeedTree> build) : ISeedDefinition
    {
        public string Id => id;
        public SeedTree Build() => build();
    }

    private static readonly ContentReference SavedRef = new(500);

    private sealed record Setup(
        Mock<IContentGenerationProvider> Provider,
        Mock<IContentTranslationProvider>? Translator,
        Mock<IContentRepository> Repository,
        Mock<ISeedTracker> Tracker,
        SeedOrchestrator Sut);

    private static Setup Build(
        SeedTree tree,
        bool canTranslate,
        IContentData? loadableMaster,
        ICompositionMapper? mapper = null,
        ExperienceData? loadableExperience = null,
        IReadOnlyList<ContentSeederRecord>? existingRecords = null)
    {
        var provider = new Mock<IContentGenerationProvider>();
        provider.Setup(p => p.PopulateAsync(
                It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(), It.IsAny<string>(), It.IsAny<Type?>(),
                It.IsAny<IReadOnlyDictionary<string, (int Width, int Height)>?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<IContentTranslationProvider>? translator = null;
        if (canTranslate)
        {
            translator = provider.As<IContentTranslationProvider>();
            translator.Setup(t => t.TranslateAsync(
                    It.IsAny<object>(), It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<Type?>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        var repo = new Mock<IContentRepository>();
        repo.Setup(r => r.GetDefault<FakePage>(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>())).Returns(() => new FakePage());
        repo.Setup(r => r.CreateLanguageBranch<FakePage>(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>())).Returns(() => new FakePage());
        repo.Setup(r => r.GetDefault<FakeExperience>(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>())).Returns(() => new FakeExperience());
        repo.Setup(r => r.CreateLanguageBranch<FakeExperience>(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>())).Returns(() => new FakeExperience());
        repo.Setup(r => r.GetDefault<FakeBlock>(It.IsAny<ContentReference>())).Returns(() => new FakeBlock());
        repo.Setup(r => r.GetDefault<ContentFolder>(It.IsAny<ContentReference>())).Returns(() => new ContentFolder());
        repo.Setup(r => r.CreateLanguageBranch<FakeBlock>(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>())).Returns(() => new FakeBlock());
        repo.Setup(r => r.Save(It.IsAny<IContent>(), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>())).Returns(SavedRef);

        var loader = new Mock<IContentLoader>();
        IContentData? master = loadableMaster;
        loader.Setup(l => l.TryGet<IContentData>(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>(), out master))
            .Returns(loadableMaster is not null);
        ExperienceData? experience = loadableExperience;
        loader.Setup(l => l.TryGet<ExperienceData>(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>(), out experience))
            .Returns(loadableExperience is not null);

        var tracker = new Mock<ISeedTracker>();
        tracker.Setup(t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tracker.Setup(t => t.MarkCompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tracker.Setup(t => t.GetByDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRecords ?? Array.Empty<ContentSeederRecord>());
        tracker.Setup(t => t.SetDefinitionFingerprintAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var types = new Mock<IContentTypeRepository>();
        types.Setup(r => r.Load(It.IsAny<Type>())).Returns(new ContentType { ID = 1 });

        var seederOptions = new ContentSeederOptions { MasterLanguage = "en", Languages = ["sv"] };
        var videoProvider = new NullVideoProvider(NullLogger<SeedOrchestrator>.Instance);
        var sections = TestSectionSeeder.Create(
            repo.Object, loader.Object, types.Object, provider.Object,
            Mock.Of<IBlueprintRepository>(), Mock.Of<IBlueprintContentFactory>(), mapper ?? Mock.Of<ICompositionMapper>(),
            tracker.Object, seederOptions, videoProvider, NullLogger<SeedOrchestrator>.Instance);

        var sut = new SeedOrchestrator(
            repo.Object, loader.Object, types.Object, tracker.Object, provider.Object,
            Mock.Of<IImageContentFactory>(f => f.DeleteSeededImagesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()) == Task.CompletedTask),
            sections,
            OptionsFactory.Create(seederOptions),
            [new LambdaSeedDefinition("def", () => tree)],
            NullLogger<SeedOrchestrator>.Instance, new SeedDefinitionAccessor(), videoProvider);

        return new Setup(provider, translator, repo, tracker, sut);
    }

    private static ExperienceSeedTree OnePage() => new(nodes: [new ExperienceSeedNode(typeof(FakePage), count: 1)]);

    private static ExperienceSeedTree OneBlock() => new(blocks: [new BlockDefinition("hero", typeof(FakeBlock))]);

    private static ExperienceSeedTree OneSharedSection() => new(sections: [new SectionDefinition("hero-section", typeof(FakeBlock))]);

    private static ExperienceSeedTree OneExperience() => new(nodes: [new ExperienceSeedNode(typeof(FakeExperience), count: 1)]);

    private static void VerifyPopulated(Mock<IContentGenerationProvider> provider, string language, Times times)
        => provider.Verify(p => p.PopulateAsync(
            It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(), language, It.IsAny<Type?>(),
            It.IsAny<IReadOnlyDictionary<string, (int Width, int Height)>?>(), It.IsAny<CancellationToken>()), times);

    // ── pages and blocks ─────────────────────────────────────────────────────

    [Fact]
    public async Task PageLanguageBranch_WithATranslatingProvider_IsTranslatedFromTheMaster()
    {
        var master = new FakePage { Heading = "Find your next adventure" };
        var setup = Build(OnePage(), canTranslate: true, loadableMaster: master);

        await setup.Sut.SeedAsync();

        setup.Translator!.Verify(t => t.TranslateAsync(
            It.Is<object>(o => ReferenceEquals(o, master)),
            It.IsAny<object>(),
            It.Is<IEnumerable<PropertyInfo>>(props => props.Any(p => p.Name == nameof(FakePage.Heading))),
            "en", "sv", typeof(FakePage), It.IsAny<CancellationToken>()), Times.Once);

        // The master is still generated; the Swedish branch is not generated independently.
        VerifyPopulated(setup.Provider, "en", Times.Once());
        VerifyPopulated(setup.Provider, "sv", Times.Never());
    }

    [Fact]
    public async Task PageLanguageBranch_WithAPlainProvider_IsGeneratedForTheLanguageAsBefore()
    {
        var setup = Build(OnePage(), canTranslate: false, loadableMaster: new FakePage());

        await setup.Sut.SeedAsync();

        VerifyPopulated(setup.Provider, "en", Times.Once());
        VerifyPopulated(setup.Provider, "sv", Times.Once());
    }

    [Fact]
    public async Task PageLanguageBranch_WhenTheMasterCannotBeLoaded_FallsBackToGenerating()
    {
        var setup = Build(OnePage(), canTranslate: true, loadableMaster: null);

        await setup.Sut.SeedAsync();

        setup.Translator!.Verify(t => t.TranslateAsync(
            It.IsAny<object>(), It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<Type?>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyPopulated(setup.Provider, "sv", Times.Once());
    }

    [Fact]
    public async Task BlockLanguageBranch_WithATranslatingProvider_IsTranslatedFromTheMaster()
    {
        var master = new FakeBlock { Title = "Find your next adventure" };
        var setup = Build(OneBlock(), canTranslate: true, loadableMaster: master);

        await setup.Sut.SeedAsync();

        setup.Translator!.Verify(t => t.TranslateAsync(
            It.Is<object>(o => ReferenceEquals(o, master)),
            It.IsAny<object>(),
            It.IsAny<IEnumerable<PropertyInfo>>(),
            "en", "sv", typeof(FakeBlock), It.IsAny<CancellationToken>()), Times.Once);
        VerifyPopulated(setup.Provider, "sv", Times.Never());
    }

    [Fact]
    public async Task BlockLanguageBranch_OnlyReceivesCultureSpecificProperties()
    {
        var master = new FakeBlock { Title = "Find your next adventure" };
        var setup = Build(OneBlock(), canTranslate: true, loadableMaster: master);
        var received = new List<string>();
        setup.Translator!
            .Setup(t => t.TranslateAsync(
                It.IsAny<object>(), It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<object, object, IEnumerable<PropertyInfo>, string, string, Type?, CancellationToken>(
                (_, _, props, _, _, _, _) => received.AddRange(props.Select(p => p.Name)));

        await setup.Sut.SeedAsync();

        Assert.Contains(nameof(FakeBlock.Title), received);
        Assert.DoesNotContain(nameof(FakeBlock.Picture), received);
    }

    [Fact]
    public async Task BlockLanguageVersion_KeepsConfigureChangesToCultureSpecificProperties_ButNotSharedOnes()
    {
        // configure is written for the master, but it also runs for each language. EPiServer refuses to save a shared
        // (non-culture-specific) property on a language version, so those changes must not reach it.
        var tree = new ExperienceSeedTree(blocks:
        [
            new BlockDefinition("hero", typeof(FakeBlock), configure: (block, _) =>
            {
                var b = (FakeBlock)block;
                b.Title = "Configured title";
                b.Name = "Configured name";
                b.Picture = new ContentReference(5);
            })
        ]);
        var setup = Build(tree, canTranslate: false, loadableMaster: new FakeBlock());
        var saved = new List<FakeBlock>();
        setup.Repository.Setup(r => r.Save(It.IsAny<IContent>(), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>()))
            .Callback<IContent, SaveAction, AccessLevel>((content, _, _) => { if (content is FakeBlock b) saved.Add(b); })
            .Returns(SavedRef);

        await setup.Sut.SeedAsync();

        Assert.Equal(2, saved.Count);
        var (master, branch) = (saved[0], saved[1]);

        Assert.Equal("Configured title", master.Title);
        Assert.Equal(new ContentReference(5), master.Picture);

        Assert.Equal("Configured title", branch.Title);   // culture specific: kept
        Assert.Equal("Configured name", branch.Name);      // the name is per language: kept
        Assert.Null(branch.Picture);                       // shared: left to the master
    }

    [Fact]
    public async Task BlockLanguageBranch_WithAPlainProvider_IsGeneratedForTheLanguageAsBefore()
    {
        var setup = Build(OneBlock(), canTranslate: false, loadableMaster: new FakeBlock());

        await setup.Sut.SeedAsync();

        VerifyPopulated(setup.Provider, "sv", Times.Once());
    }

    // ── shared sections ──────────────────────────────────────────────────────

    [Fact]
    public async Task SharedSection_GetsALanguageVersion_TrackedAsASectionInstance()
    {
        var inserted = new List<ContentSeederRecord>();
        var setup = Build(OneSharedSection(), canTranslate: true, loadableMaster: new FakeBlock { Title = "Hero" });
        setup.Tracker.Setup(t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ContentSeederRecord, CancellationToken>((r, _) => inserted.Add(r))
            .Returns(Task.CompletedTask);

        await setup.Sut.SeedAsync();

        setup.Repository.Verify(r => r.CreateLanguageBranch<FakeBlock>(It.IsAny<ContentReference>(), It.Is<CultureInfo>(c => c.Name == "sv")), Times.Once);
        Assert.Contains(inserted, r => r.ContentType == ContentSeederRecord.ContentTypes.SectionInstance && r.Language == "sv" && r.BlockDefinitionId == "hero-section");
        Assert.Contains(inserted, r => r.ContentType == ContentSeederRecord.ContentTypes.SectionInstance && r.Language == "en");
        setup.Translator!.Verify(t => t.TranslateAsync(
            It.IsAny<object>(), It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(), "en", "sv", typeof(FakeBlock), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SharedSection_WithACompleteLanguageVersion_IsNotCreatedAgain()
    {
        var existing = new[]
        {
            Record(ContentSeederRecord.ContentTypes.SectionInstance, "hero-section", "en", contentId: 600),
            Record(ContentSeederRecord.ContentTypes.SectionInstance, "hero-section", "sv", contentId: 600),
        };
        var setup = Build(OneSharedSection(), canTranslate: true, loadableMaster: new FakeBlock(), existingRecords: existing);

        await setup.Sut.SeedAsync();

        setup.Repository.Verify(r => r.CreateLanguageBranch<FakeBlock>(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>()), Times.Never);
    }

    private static ContentSeederRecord Record(string type, string definitionId, string language, int contentId) => new()
    {
        Id = Guid.NewGuid(),
        SeedDefinitionId = "def",
        BlockDefinitionId = definitionId,
        ContentGuid = Guid.NewGuid(),
        ContentId = contentId,
        ContentType = type,
        Language = language,
        CreatedUtc = DateTime.UtcNow,
        UpdatedUtc = DateTime.UtcNow,
        SeedRunId = Guid.NewGuid(),
        IsPartial = false,
    };

    // ── Visual Builder composition of Experience pages ───────────────────────

    // Master composition: [inline section "Content" > row > column > inline element "Body"] + [shared block "Hero"].
    private static (Composition Master, FakeInlineSection Section, FakeInlineElement Element, FakeBlock Shared) MasterComposition()
    {
        var section = new FakeInlineSection();
        var element = new FakeInlineElement { Text = "Hello" };
        var shared = new FakeBlock { ContentLink = new ContentReference(900), Title = "Shared hero" };

        var composition = new Composition();
        var sectionNode = composition.AddSection(section, "Content");
        sectionNode.WithStyles("dark", new Dictionary<string, string> { ["padding"] = "large" });
        sectionNode.AddRow("Row").AddColumn("Col").AddComponent(element, "Body");
        composition.AddComponent(shared, "Hero");
        return (composition, section, element, shared);
    }

    private static (Setup Setup, Composition? Captured, Mock<ICompositionMapper> Mapper) BuildExperience(bool canTranslate, Composition? master)
    {
        var mapper = new Mock<ICompositionMapper>();
        mapper.Setup(m => m.ToComposition(It.IsAny<ExperienceData>(), It.IsAny<bool>())).Returns(master!);

        var setup = Build(OneExperience(), canTranslate, loadableMaster: new FakeExperience(), mapper.Object, loadableExperience: new FakeExperience());
        return (setup, null, mapper);
    }

    [Fact]
    public async Task ExperienceLanguageBranch_GetsTheMastersLayout_WithInlineContentClonedAndTranslated()
    {
        var (master, section, element, shared) = MasterComposition();
        var (setup, _, mapper) = BuildExperience(canTranslate: true, master);
        Composition? captured = null;
        ExperienceData? populated = null;
        mapper.Setup(m => m.Populate(It.IsAny<ExperienceData>(), It.IsAny<Composition>()))
            .Callback<ExperienceData, Composition>((page, c) => { populated = page; captured = c; });

        await setup.Sut.SeedAsync();

        Assert.NotNull(captured);
        Assert.NotNull(populated);
        Assert.Equal(2, captured!.Nodes.Count);

        // Inline section: a clone, same structure, same name and styles.
        var sectionCopy = Assert.IsType<SectionNode>(captured.Nodes[0]);
        Assert.Equal("Content", sectionCopy.Name);
        Assert.IsType<FakeInlineSection>(sectionCopy.SectionData);
        Assert.NotSame(section, sectionCopy.SectionData);
        Assert.Equal("dark", sectionCopy.DisplayTemplateKey);
        Assert.Equal("large", sectionCopy.DisplaySettings["padding"]);

        var row = Assert.IsType<StructureNode>(Assert.Single(sectionCopy.Nodes));
        Assert.Equal(LayoutStructureType.Row, row.NodeType);
        var column = Assert.IsType<StructureNode>(Assert.Single(row.Nodes));
        Assert.Equal(LayoutStructureType.Column, column.NodeType);

        // Inline element: a clone, not the master's instance, and it was handed to the translator.
        var component = Assert.IsType<ComponentNode>(Assert.Single(column.Nodes));
        Assert.Equal("Body", component.Name);
        Assert.IsType<FakeInlineElement>(component.Component);
        Assert.NotSame(element, component.Component);
        setup.Translator!.Verify(t => t.TranslateAsync(
            It.Is<object>(o => ReferenceEquals(o, element)),
            It.Is<object>(o => ReferenceEquals(o, component.Component)),
            It.IsAny<IEnumerable<PropertyInfo>>(), "en", "sv", typeof(FakeInlineElement), It.IsAny<CancellationToken>()), Times.Once);

        // A shared block is referenced, not copied: it has language versions of its own.
        var sharedCopy = Assert.IsType<ComponentNode>(captured.Nodes[1]);
        Assert.Equal("Hero", sharedCopy.Name);
        Assert.Same(shared, sharedCopy.Component);
    }

    [Fact]
    public async Task ExperienceLanguageBranch_TranslatesIdenticalElementsOnce_AndCopiesTheResultToTheRest()
    {
        var composition = new Composition();
        var column = composition.AddSection(new FakeInlineSection(), "Content").AddRow("Row").AddColumn("Col");
        column.AddComponent(new FakeInlineElement { Text = "Same" }, "A");
        column.AddComponent(new FakeInlineElement { Text = "Same" }, "B");
        column.AddComponent(new FakeInlineElement { Text = "Other" }, "C");

        var (setup, _, mapper) = BuildExperience(canTranslate: true, composition);
        Composition? captured = null;
        mapper.Setup(m => m.Populate(It.IsAny<ExperienceData>(), It.IsAny<Composition>()))
            .Callback<ExperienceData, Composition>((_, c) => captured = c);
        setup.Translator!
            .Setup(t => t.TranslateAsync(
                It.IsAny<object>(), It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Type?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Callback<object, object, IEnumerable<PropertyInfo>, string, string, Type?, CancellationToken>((source, target, _, _, language, _, _) =>
            {
                if (target is FakeInlineElement element)
                    element.Text = $"{language}:{((FakeInlineElement)source).Text}";
            });

        await setup.Sut.SeedAsync();

        var section = Assert.IsType<SectionNode>(Assert.Single(captured!.Nodes));
        var row = Assert.IsType<StructureNode>(Assert.Single(section.Nodes));
        var col = Assert.IsType<StructureNode>(Assert.Single(row.Nodes));
        var texts = col.Nodes.Cast<ComponentNode>().Select(c => ((FakeInlineElement)c.Component).Text ?? string.Empty).ToArray();
        Assert.Equal(["sv:Same", "sv:Same", "sv:Other"], texts);

        // Two distinct texts, so two element translations — not three.
        setup.Translator.Verify(t => t.TranslateAsync(
            It.Is<object>(o => o is FakeInlineElement), It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(),
            "en", "sv", typeof(FakeInlineElement), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ExperienceLanguageBranch_KeepsTheMastersVideoUrlOnAClonedElement()
    {
        const string url = "https://youtu.be/aqz-KE-bpKQ";
        var composition = new Composition();
        composition.AddSection(new FakeInlineSection(), "Content").AddRow("Row").AddColumn("Col")
            .AddComponent(new FakeInlineElement { Text = url }, "Video");

        var (setup, _, mapper) = BuildExperience(canTranslate: false, composition);
        Composition? captured = null;
        mapper.Setup(m => m.Populate(It.IsAny<ExperienceData>(), It.IsAny<Composition>()))
            .Callback<ExperienceData, Composition>((_, c) => captured = c);
        // The placeholder provider rewrites text properties, which would swap the video for random text.
        setup.Provider
            .Setup(p => p.PopulateAsync(
                It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(), It.IsAny<string>(), It.IsAny<Type?>(),
                It.IsAny<IReadOnlyDictionary<string, (int Width, int Height)>?>(), It.IsAny<CancellationToken>()))
            .Callback<object, IEnumerable<PropertyInfo>, string, Type?, IReadOnlyDictionary<string, (int, int)>?, CancellationToken>(
                (content, _, _, _, _, _) => { if (content is FakeInlineElement e) e.Text = "random placeholder"; })
            .Returns(Task.CompletedTask);

        await setup.Sut.SeedAsync();

        var section = Assert.IsType<SectionNode>(Assert.Single(captured!.Nodes));
        var column = Assert.IsType<StructureNode>(Assert.Single(Assert.IsType<StructureNode>(Assert.Single(section.Nodes)).Nodes));
        var element = Assert.IsType<FakeInlineElement>(Assert.IsType<ComponentNode>(Assert.Single(column.Nodes)).Component);
        Assert.Equal(url, element.Text);
    }

    [Fact]
    public async Task ExperienceLanguageBranch_WithAPlainProvider_GeneratesTheClonedContentForTheLanguage()
    {
        var (master, _, element, _) = MasterComposition();
        var (setup, _, mapper) = BuildExperience(canTranslate: false, master);
        Composition? captured = null;
        mapper.Setup(m => m.Populate(It.IsAny<ExperienceData>(), It.IsAny<Composition>()))
            .Callback<ExperienceData, Composition>((_, c) => captured = c);

        await setup.Sut.SeedAsync();

        Assert.NotNull(captured);
        // The element is populated in the target language; the master's own copy is untouched.
        setup.Provider.Verify(p => p.PopulateAsync(
            It.Is<object>(o => o is FakeInlineElement && !ReferenceEquals(o, element)),
            It.IsAny<IEnumerable<PropertyInfo>>(), "sv", typeof(FakeInlineElement),
            It.IsAny<IReadOnlyDictionary<string, (int Width, int Height)>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExperienceLanguageBranch_WithAnEmptyMasterLayout_StillGetsALayout()
    {
        // A language version with no layout at all makes the outline tag helper throw when rendered.
        var (setup, _, mapper) = BuildExperience(canTranslate: true, new Composition());
        Composition? captured = null;
        mapper.Setup(m => m.Populate(It.IsAny<ExperienceData>(), It.IsAny<Composition>()))
            .Callback<ExperienceData, Composition>((_, c) => captured = c);

        await setup.Sut.SeedAsync();

        Assert.NotNull(captured);
        Assert.Empty(captured!.Nodes);
    }

    [Fact]
    public async Task ExperienceLanguageBranch_WhenTheMasterCannotBeLoaded_DoesNotThrow()
    {
        var mapper = new Mock<ICompositionMapper>();
        var setup = Build(OneExperience(), canTranslate: true, loadableMaster: new FakeExperience(), mapper.Object, loadableExperience: null);

        await setup.Sut.SeedAsync();

        mapper.Verify(m => m.Populate(It.IsAny<ExperienceData>(), It.IsAny<Composition>()), Times.Never);
    }
}

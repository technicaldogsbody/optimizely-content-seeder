using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;
using System.Globalization;
using System.Reflection;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.Security;
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
/// Content reference properties pointed at other seeded content by key: a sibling page created after the page that
/// points at it, a shared block, and values that are already set.
/// </summary>
public class SeedOrchestratorReferenceTests
{
    private class FakePageBase : PageData
    {
        public override string Name { get; set; } = string.Empty;
        public override int ContentTypeID { get; set; }
        public override ContentReference ParentLink { get; set; } = ContentReference.EmptyReference;
        public override Guid ContentGuid { get; set; } = Guid.NewGuid();
        public override ContentReference ContentLink { get; set; } = ContentReference.EmptyReference;    }

    private sealed class FakeHome : FakePageBase
    {
        public ContentReference? SiteSettings { get; set; }
        public string? Title { get; set; }
        public ContentArea? Related { get; set; }
    }

    private sealed class FakeSettings : FakePageBase { }

    private sealed class LambdaSeedDefinition(string id, Func<SeedTree> build) : ISeedDefinition
    {
        public string Id => id;
        public SeedTree Build() => build();
    }

    private delegate bool TryGetPage(ContentReference link, CultureInfo culture, out PageData page);

    private sealed record Harness(SeedOrchestrator Sut, Dictionary<int, IContent> Saved, List<IContent> Saves);

    private static Harness Build(SeedTree tree)
    {
        var saved = new Dictionary<int, IContent>();
        var saves = new List<IContent>();
        int nextId = 200;

        var repo = new Mock<IContentRepository>();
        repo.Setup(r => r.GetDefault<FakeHome>(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>()))
            .Returns((ContentReference p, CultureInfo _) => new FakeHome { ParentLink = p });
        repo.Setup(r => r.GetDefault<FakeSettings>(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>()))
            .Returns((ContentReference p, CultureInfo _) => new FakeSettings { ParentLink = p });
        repo.Setup(r => r.Save(It.IsAny<IContent>(), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>()))
            .Returns((IContent content, SaveAction _, AccessLevel _) =>
            {
                if (ContentReference.IsNullOrEmpty(content.ContentLink))
                    content.ContentLink = new ContentReference(nextId++);
                saved[content.ContentLink.ID] = content;
                saves.Add(content);
                return content.ContentLink;
            });

        var loader = new Mock<IContentLoader>();
        loader.Setup(l => l.TryGet(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>(), out It.Ref<PageData>.IsAny))
            .Returns(new TryGetPage((ContentReference link, CultureInfo _, out PageData page) =>
            {
                page = saved.TryGetValue(link.ID, out var content) ? (PageData)content : null!;
                return page is not null;
            }));

        var tracker = new Mock<ISeedTracker>();
        tracker.Setup(t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tracker.Setup(t => t.MarkCompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tracker.Setup(t => t.GetByDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        tracker.Setup(t => t.SetDefinitionFingerprintAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var types = new Mock<IContentTypeRepository>();
        types.Setup(r => r.Load(It.IsAny<Type>())).Returns(new ContentType { ID = 1 });

        var provider = new Mock<IContentGenerationProvider>();
        provider.Setup(p => p.PopulateAsync(
                It.IsAny<object>(), It.IsAny<IEnumerable<PropertyInfo>>(), It.IsAny<string>(), It.IsAny<Type?>(),
                It.IsAny<IReadOnlyDictionary<string, (int Width, int Height)>?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new SeedOrchestrator(
            repo.Object, loader.Object, types.Object, tracker.Object, provider.Object,
            Mock.Of<IImageContentFactory>(f => f.DeleteSeededImagesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()) == Task.CompletedTask),
            new NoSectionSeeder(),
            OptionsFactory.Create(new ContentSeederOptions { MasterLanguage = "en" }),
            [new LambdaSeedDefinition("def", () => tree)],
            NullLogger<SeedOrchestrator>.Instance, new SeedDefinitionAccessor(), new NullVideoProvider(NullLogger<SeedOrchestrator>.Instance));

        return new Harness(sut, saved, saves);
    }

    private static FakeHome HomeOf(Harness harness) => harness.Saved.Values.OfType<FakeHome>().Single();
    private static FakeSettings SettingsOf(Harness harness) => harness.Saved.Values.OfType<FakeSettings>().Single();

    [Fact]
    public async Task APageCanPointAtAChildCreatedAfterIt()
    {
        var tree = new SeedTree(nodes:
        [
            new SeedNode(typeof(FakeHome),
                references: [new ContentReferenceAssignment(nameof(FakeHome.SiteSettings), "settings")],
                children: [new SeedNode(typeof(FakeSettings), key: "settings")]),
        ]);
        var harness = Build(tree);

        await harness.Sut.SeedAsync();

        Assert.Equal(SettingsOf(harness).ContentLink.ID, HomeOf(harness).SiteSettings?.ID);
    }

    [Fact]
    public async Task APageCanPointAtASiblingDeclaredAfterIt()
    {
        var tree = new SeedTree(nodes:
        [
            new SeedNode(typeof(FakeHome), references: [new ContentReferenceAssignment(nameof(FakeHome.SiteSettings), "settings")]),
            new SeedNode(typeof(FakeSettings), key: "settings"),
        ]);
        var harness = Build(tree);

        await harness.Sut.SeedAsync();

        Assert.Equal(SettingsOf(harness).ContentLink.ID, HomeOf(harness).SiteSettings?.ID);
    }

    [Fact]
    public async Task AValueAlreadySet_IsKept()
    {
        var existing = new ContentReference(42);
        var tree = new SeedTree(nodes:
        [
            new SeedNode(typeof(FakeHome),
                configure: (page, _) => ((FakeHome)page).SiteSettings = existing,
                references: [new ContentReferenceAssignment(nameof(FakeHome.SiteSettings), "settings")]),
            new SeedNode(typeof(FakeSettings), key: "settings"),
        ]);
        var harness = Build(tree);

        await harness.Sut.SeedAsync();

        Assert.Equal(42, HomeOf(harness).SiteSettings?.ID);
        Assert.Equal(2, harness.Saves.Count); // the two pages, with no second save of the home page
    }

    // ── pages in content areas ───────────────────────────────────────────────

    [Fact]
    public async Task AContentAreaCanHoldASiblingPageDeclaredAfterIt()
    {
        var tree = new SeedTree(nodes:
        [
            new SeedNode(typeof(FakeHome), contentAreaAssignments: [new ContentAreaAssignment(nameof(FakeHome.Related), ["settings"])]),
            new SeedNode(typeof(FakeSettings), key: "settings"),
        ]);
        var harness = Build(tree);

        await harness.Sut.SeedAsync();

        var item = Assert.Single(HomeOf(harness).Related!.Items);
        Assert.Equal(SettingsOf(harness).ContentLink.ID, item.ContentLink.ID);
    }

    [Fact]
    public async Task AContentAreaAlreadyFilled_IsKept()
    {
        var tree = new SeedTree(nodes:
        [
            new SeedNode(typeof(FakeHome),
                configure: (page, _) =>
                {
                    var area = new ContentArea();
                    area.Items.Add(new ContentAreaItem { ContentLink = new ContentReference(42) });
                    ((FakeHome)page).Related = area;
                },
                contentAreaAssignments: [new ContentAreaAssignment(nameof(FakeHome.Related), ["settings"])]),
            new SeedNode(typeof(FakeSettings), key: "settings"),
        ]);
        var harness = Build(tree);

        await harness.Sut.SeedAsync();

        Assert.Equal(42, Assert.Single(HomeOf(harness).Related!.Items).ContentLink.ID);
        Assert.Equal(2, harness.Saves.Count);
    }

    [Fact]
    public async Task APageInAContentAreaThatIsNotAContentArea_FailsBeforeAnythingIsCreated()
    {
        var tree = new SeedTree(nodes:
        [
            new SeedNode(typeof(FakeHome), contentAreaAssignments: [new ContentAreaAssignment(nameof(FakeHome.Title), ["settings"])]),
            new SeedNode(typeof(FakeSettings), key: "settings"),
        ]);
        var harness = Build(tree);

        var ex = await Record.ExceptionAsync(() => harness.Sut.SeedAsync());

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains(nameof(FakeHome.Title), ex.Message);
        Assert.Empty(harness.Saves);
    }

    [Fact]
    public async Task AReferenceToANonReferenceProperty_FailsBeforeAnythingIsCreated()
    {
        var tree = new SeedTree(nodes:
        [
            new SeedNode(typeof(FakeHome), references: [new ContentReferenceAssignment(nameof(FakeHome.Title), "settings")]),
            new SeedNode(typeof(FakeSettings), key: "settings"),
        ]);
        var harness = Build(tree);

        var ex = await Record.ExceptionAsync(() => harness.Sut.SeedAsync());

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains(nameof(FakeHome.Title), ex.Message);
        Assert.Empty(harness.Saves);
    }
}

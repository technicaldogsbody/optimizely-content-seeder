using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;
using System.Globalization;
using System.Reflection;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.Security;
using EPiServer.VisualBuilder;
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
/// Seed over content that already exists. An existing page may only be reused for a node that wants the same type
/// under the same parent, so adding a node anywhere in the tree creates it (and its children) in the right place.
/// </summary>
public class SeedOrchestratorResumeMatchingTests
{
    // PageData's identity setters normally write through to a property collection that a bare instance doesn't have.
    private class FakePageBase : PageData
    {
        public override string Name { get; set; } = string.Empty;
        public override int ContentTypeID { get; set; }
        public override ContentReference ParentLink { get; set; } = ContentReference.EmptyReference;
        public override Guid ContentGuid { get; set; } = Guid.NewGuid();
        public override ContentReference ContentLink { get; set; } = ContentReference.EmptyReference;
    }

    private sealed class FakeStart : FakePageBase { }
    private sealed class FakeListing : FakePageBase { }
    private sealed class FakeLanding : FakePageBase { }
    private sealed class FakeArticle : FakePageBase { }

    private const int StartTypeId = 1, ListingTypeId = 2, LandingTypeId = 3, ArticleTypeId = 4;
    private const int StartId = 100, Landing1Id = 101, Landing2Id = 102, ArticleUnderStartId = 110;

    private sealed class LambdaSeedDefinition(string id, Func<SeedTree> build) : ISeedDefinition
    {
        public string Id => id;
        public SeedTree Build() => build();
    }

    private sealed record Created(Type Type, int ParentId, int Id);

    private static ContentSeederRecord PageRecord(int contentId, int minutes) => new()
    {
        Id = Guid.NewGuid(),
        SeedDefinitionId = "def",
        ContentGuid = Guid.NewGuid(),
        ContentId = contentId,
        ContentType = ContentSeederRecord.ContentTypes.Page,
        Language = "en",
        CreatedUtc = new DateTime(2026, 1, 1, 0, minutes, 0, DateTimeKind.Utc),
        UpdatedUtc = new DateTime(2026, 1, 1, 0, minutes, 0, DateTimeKind.Utc),
        SeedRunId = Guid.NewGuid(),
        IsPartial = false,
    };

    // An existing page as the CMS would return it: where it sits and what type it is.
    private static FakePageBase Existing<T>(int id, int parentId, int typeId) where T : FakePageBase, new() => new T
    {
        ContentLink = new ContentReference(id),
        ParentLink = new ContentReference(parentId),
        ContentTypeID = typeId,
    };

    private sealed record Harness(SeedOrchestrator Sut, List<Created> Created);

    private static Harness Build(SeedTree tree, IReadOnlyList<ContentSeederRecord> existing, params FakePageBase[] existingContent)
    {
        var created = new List<Created>();
        int nextId = 200;

        var repo = new Mock<IContentRepository>();
        repo.Setup(r => r.GetDefault<FakeStart>(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>()))
            .Returns((ContentReference p, CultureInfo _) => new FakeStart { ParentLink = p });
        repo.Setup(r => r.GetDefault<FakeListing>(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>()))
            .Returns((ContentReference p, CultureInfo _) => new FakeListing { ParentLink = p });
        repo.Setup(r => r.GetDefault<FakeLanding>(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>()))
            .Returns((ContentReference p, CultureInfo _) => new FakeLanding { ParentLink = p });
        repo.Setup(r => r.GetDefault<FakeArticle>(It.IsAny<ContentReference>(), It.IsAny<CultureInfo>()))
            .Returns((ContentReference p, CultureInfo _) => new FakeArticle { ParentLink = p });
        repo.Setup(r => r.Save(It.IsAny<IContent>(), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>()))
            .Returns((IContent content, SaveAction _, AccessLevel _) =>
            {
                var saved = new ContentReference(nextId++);
                content.ContentLink = saved;
                created.Add(new Created(content.GetType(), content.ParentLink.ID, saved.ID));
                return saved;
            });

        var loader = new Mock<IContentLoader>();
        foreach (var page in existingContent)
        {
            IContent? loaded = page;
            loader.Setup(l => l.TryGet<IContent>(page.ContentLink, It.IsAny<CultureInfo>(), out loaded)).Returns(true);
        }

        // Anything not set up above has been deleted from the CMS.
        IContent? none = null;
        loader.Setup(l => l.TryGet<IContent>(It.Is<ContentReference>(r => !existingContent.Any(p => p.ContentLink.ID == r.ID)), It.IsAny<CultureInfo>(), out none))
            .Returns(false);

        var tracker = new Mock<ISeedTracker>();
        tracker.Setup(t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tracker.Setup(t => t.MarkCompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tracker.Setup(t => t.GetByDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        tracker.Setup(t => t.SetDefinitionFingerprintAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var types = new Mock<IContentTypeRepository>();
        types.Setup(r => r.Load(typeof(FakeStart))).Returns(new ContentType { ID = StartTypeId });
        types.Setup(r => r.Load(typeof(FakeListing))).Returns(new ContentType { ID = ListingTypeId });
        types.Setup(r => r.Load(typeof(FakeLanding))).Returns(new ContentType { ID = LandingTypeId });
        types.Setup(r => r.Load(typeof(FakeArticle))).Returns(new ContentType { ID = ArticleTypeId });

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

        return new Harness(sut, created);
    }

    // What the first run seeded: a start page with an article and two landing pages beneath it.
    private static (IReadOnlyList<ContentSeederRecord> Records, FakePageBase[] Content) FirstRun() =>
    (
        [PageRecord(StartId, 0), PageRecord(ArticleUnderStartId, 1), PageRecord(Landing1Id, 2), PageRecord(Landing2Id, 3)],
        [
            Existing<FakeStart>(StartId, ContentReference.RootPage.ID, StartTypeId),
            Existing<FakeArticle>(ArticleUnderStartId, StartId, ArticleTypeId),
            Existing<FakeLanding>(Landing1Id, StartId, LandingTypeId),
            Existing<FakeLanding>(Landing2Id, StartId, LandingTypeId),
        ]
    );

    private static SeedNode ArticleUnderStart() => new(typeof(FakeArticle), count: 1);
    private static SeedNode Landings() => new(typeof(FakeLanding), count: 2);

    // ── the bug: a node added in the middle ──────────────────────────────────

    [Fact]
    public async Task ANodeAddedBeforeExistingOnes_IsCreated_AndItsChildrenGoUnderIt()
    {
        var (records, content) = FirstRun();
        var listing = new SeedNode(typeof(FakeListing), count: 1, children: [new SeedNode(typeof(FakeArticle), count: 3)]);
        var tree = new SeedTree(nodes: [new SeedNode(typeof(FakeStart), count: 1, children: [ArticleUnderStart(), listing, Landings()])]);
        var h = Build(tree, records, content);

        await h.Sut.SeedAsync();

        // The listing is new, under the start page. It is not one of the existing landing pages.
        var createdListing = Assert.Single(h.Created, c => c.Type == typeof(FakeListing));
        Assert.Equal(StartId, createdListing.ParentId);

        // Its three articles are under the listing, not under a landing page or the start page.
        var listingArticles = h.Created.Where(c => c.Type == typeof(FakeArticle)).ToList();
        Assert.Equal(3, listingArticles.Count);
        Assert.All(listingArticles, a => Assert.Equal(createdListing.Id, a.ParentId));

        // Everything that already existed was reused, not created again.
        Assert.DoesNotContain(h.Created, c => c.Type == typeof(FakeStart));
        Assert.DoesNotContain(h.Created, c => c.Type == typeof(FakeLanding));
    }

    [Fact]
    public async Task ANodeAddedAtTheStart_DoesNotShiftEveryNodeAfterIt()
    {
        var (records, content) = FirstRun();
        var tree = new SeedTree(nodes: [new SeedNode(typeof(FakeStart), count: 1, children:
        [
            new SeedNode(typeof(FakeListing), count: 1),   // new, ahead of everything
            ArticleUnderStart(),
            Landings(),
        ])]);
        var h = Build(tree, records, content);

        await h.Sut.SeedAsync();

        Assert.Equal([typeof(FakeListing)], h.Created.Select(c => c.Type).ToArray());
    }

    [Fact]
    public async Task SameTypeUnderADifferentParent_IsNotReused()
    {
        // The start page already has an article. A listing's articles are a different place, so they are still created.
        var (records, content) = FirstRun();
        var tree = new SeedTree(nodes: [new SeedNode(typeof(FakeStart), count: 1, children:
        [
            ArticleUnderStart(),
            new SeedNode(typeof(FakeListing), count: 1, children: [new SeedNode(typeof(FakeArticle), count: 2)]),
            Landings(),
        ])]);
        var h = Build(tree, records, content);

        await h.Sut.SeedAsync();

        Assert.Equal(2, h.Created.Count(c => c.Type == typeof(FakeArticle)));
        Assert.DoesNotContain(h.Created, c => c.Type == typeof(FakeArticle) && c.ParentId == StartId);
    }

    // ── ordinary resume still works ──────────────────────────────────────────

    [Fact]
    public async Task AnUnchangedTree_CreatesNothing()
    {
        var (records, content) = FirstRun();
        var tree = new SeedTree(nodes: [new SeedNode(typeof(FakeStart), count: 1, children: [ArticleUnderStart(), Landings()])]);
        var h = Build(tree, records, content);

        await h.Sut.SeedAsync();

        Assert.Empty(h.Created);
    }

    [Fact]
    public async Task AHigherCount_CreatesOnlyTheExtraPages()
    {
        var (records, content) = FirstRun();
        var tree = new SeedTree(nodes: [new SeedNode(typeof(FakeStart), count: 1, children:
            [ArticleUnderStart(), new SeedNode(typeof(FakeLanding), count: 3)])]);
        var h = Build(tree, records, content);

        await h.Sut.SeedAsync();

        var only = Assert.Single(h.Created);
        Assert.Equal(typeof(FakeLanding), only.Type);
        Assert.Equal(StartId, only.ParentId);
    }

    [Fact]
    public async Task APageDeletedInTheCms_IsCreatedAgain_NotHandedBackAsAReferenceToNothing()
    {
        var (records, content) = FirstRun();
        var withoutLanding2 = content.Where(c => c.ContentLink.ID != Landing2Id).ToArray();
        var tree = new SeedTree(nodes: [new SeedNode(typeof(FakeStart), count: 1, children: [ArticleUnderStart(), Landings()])]);
        var h = Build(tree, records, withoutLanding2);

        await h.Sut.SeedAsync();

        var recreated = Assert.Single(h.Created);
        Assert.Equal(typeof(FakeLanding), recreated.Type);
        Assert.Equal(StartId, recreated.ParentId);
    }

    [Fact]
    public async Task APartialRecord_IsNotReused()
    {
        var (records, content) = FirstRun();
        var partial = records.Select(r => r.ContentId == Landing2Id ? Partial(r) : r).ToList();
        var tree = new SeedTree(nodes: [new SeedNode(typeof(FakeStart), count: 1, children: [ArticleUnderStart(), Landings()])]);
        var h = Build(tree, partial, content);

        await h.Sut.SeedAsync();

        Assert.Equal(typeof(FakeLanding), Assert.Single(h.Created).Type);

        static ContentSeederRecord Partial(ContentSeederRecord r)
        {
            r.IsPartial = true;
            return r;
        }
    }

    [Fact]
    public async Task FreshSeed_WithNoExistingPages_CreatesTheWholeTree()
    {
        var tree = new SeedTree(nodes: [new SeedNode(typeof(FakeStart), count: 1, children:
            [new SeedNode(typeof(FakeListing), count: 1, children: [new SeedNode(typeof(FakeArticle), count: 2)])])]);
        var h = Build(tree, []);

        await h.Sut.SeedAsync();

        var start = Assert.Single(h.Created, c => c.Type == typeof(FakeStart));
        var listing = Assert.Single(h.Created, c => c.Type == typeof(FakeListing));
        Assert.Equal(start.Id, listing.ParentId);
        Assert.Equal(2, h.Created.Count(c => c.Type == typeof(FakeArticle) && c.ParentId == listing.Id));
    }
}

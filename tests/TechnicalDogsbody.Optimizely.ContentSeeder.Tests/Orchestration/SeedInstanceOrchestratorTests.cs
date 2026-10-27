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
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;
using OptionsFactory = Microsoft.Extensions.Options.Options;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Orchestration;

public class SeedInstanceOrchestratorTests
{
    // The seeder needs an IContent block to save; the identity members are plain properties on this fake.
    public class NamedBlock : BlockData, IContent
    {
        public virtual string? Title { get; set; }
        public string Name { get; set; } = string.Empty;
        public ContentReference ContentLink { get; set; } = ContentReference.EmptyReference;
        public ContentReference ParentLink { get; set; } = ContentReference.EmptyReference;
        public Guid ContentGuid { get; set; } = Guid.NewGuid();
        public new int ContentTypeID { get; set; }
        public bool IsDeleted { get; set; }
    }

    private sealed class Definition(SeedTree tree, ContentGenerators? generators = null) : ISeedDefinition
    {
        public string Id => "named-def";
        public SeedTree Build() => tree;
        public ContentGenerators? Generators => generators;
    }

    // Records the named set that is in force for each item the seeder asks it to generate.
    private sealed class RecordingProvider : IContentGenerationProvider
    {
        public List<(Type? Type, string? Instance)> Seen { get; } = [];

        public Task PopulateAsync(
            object content,
            IEnumerable<PropertyInfo> properties,
            string language,
            Type? contentType = null,
            IReadOnlyDictionary<string, (int Width, int Height)>? imageSizes = null,
            CancellationToken ct = default)
        {
            lock (Seen)
                Seen.Add((contentType, SeedInstanceScope.Current));
            return Task.CompletedTask;
        }
    }

    private static (SeedOrchestrator Sut, RecordingProvider Provider) Create(ISeedDefinition definition)
    {
        var repo = new Mock<IContentRepository>();
        repo.Setup(r => r.GetDefault<NamedBlock>(It.IsAny<ContentReference>())).Returns(() => new NamedBlock());
        repo.Setup(r => r.GetDefault<ContentFolder>(It.IsAny<ContentReference>())).Returns(() => new ContentFolder());
        repo.Setup(r => r.Save(It.IsAny<IContent>(), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>()))
            .Returns(new ContentReference(7));

        var tracker = new Mock<ISeedTracker>();
        tracker.Setup(t => t.GetByDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ContentSeederRecord>());
        tracker.Setup(t => t.InsertAsync(It.IsAny<ContentSeederRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tracker.Setup(t => t.MarkCompleteAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tracker.Setup(t => t.GetDefinitionFingerprintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        tracker.Setup(t => t.SetDefinitionFingerprintAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var types = new Mock<IContentTypeRepository>();
        types.Setup(r => r.Load(It.IsAny<Type>())).Returns(new ContentType { ID = 1 });

        var provider = new RecordingProvider();
        var sut = new SeedOrchestrator(
            repo.Object, Mock.Of<IContentLoader>(), types.Object, tracker.Object, provider,
            Mock.Of<IImageContentFactory>(),
            new NoSectionSeeder(),
            OptionsFactory.Create(new ContentSeederOptions { MasterLanguage = "en" }),
            [definition],
            NullLogger<SeedOrchestrator>.Instance, new SeedDefinitionAccessor(),
            new NullVideoProvider(NullLogger<SeedOrchestrator>.Instance));

        return (sut, provider);
    }

    [Fact]
    public async Task SharedBlockWithAnInstance_IsGeneratedWithThatNamedSet()
    {
        var tree = new SeedTree(blocks: [new BlockDefinition("hero", typeof(NamedBlock), instance: "alt")]);
        var generators = new ContentGenerators().ForContentType<NamedBlock>("alt", t => t.Property(x => x.Title, f => "named"));
        var (sut, provider) = Create(new Definition(tree, generators));

        await sut.SeedAsync();

        Assert.Contains(provider.Seen, s => s.Type == typeof(NamedBlock) && s.Instance == "alt");
    }

    [Fact]
    public async Task SharedBlockWithoutAnInstance_IsGeneratedWithTheDefaultRules()
    {
        var tree = new SeedTree(blocks: [new BlockDefinition("hero", typeof(NamedBlock))]);
        var (sut, provider) = Create(new Definition(tree));

        await sut.SeedAsync();

        Assert.Contains(provider.Seen, s => s.Type == typeof(NamedBlock) && s.Instance is null);
    }

    [Fact]
    public async Task InstanceThatTheDefinitionDoesNotDefine_FailsTheSeedBeforeAnyContentIsMade()
    {
        var tree = new SeedTree(blocks: [new BlockDefinition("hero", typeof(NamedBlock), instance: "missing")]);
        var (sut, provider) = Create(new Definition(tree));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SeedAsync());

        Assert.Contains("missing", ex.Message);
        Assert.Empty(provider.Seen);
    }
}

using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.VisualBuilder;
using EPiServer.VisualBuilder.Compositions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;
using OptionsFactory = Microsoft.Extensions.Options.Options;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Orchestration;

/// <summary>
/// Checks that an operation on one seed definition touches only that definition's tracking records and images, so two
/// definitions never override or delete each other's content.
/// </summary>
public class DefinitionScopeTests
{
    private readonly Mock<ISeedTracker> _tracker = new();
    private readonly Mock<IImageContentFactory> _images = new();

    private SeedOrchestrator CreateSut()
    {
        _tracker.Setup(t => t.GetByDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ContentSeederRecord>());
        _tracker.Setup(t => t.TruncateAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _tracker.Setup(t => t.DeleteDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _images.Setup(i => i.DeleteSeededImagesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var definitions = new ISeedDefinition[] { new EmptyDefinition("a"), new EmptyDefinition("b") };

        return new SeedOrchestrator(
            Mock.Of<IContentRepository>(),
            Mock.Of<IContentLoader>(),
            Mock.Of<IContentTypeRepository>(),
            _tracker.Object,
            Mock.Of<IContentGenerationProvider>(),
            _images.Object,
            new NoSectionSeeder(),
            OptionsFactory.Create(new ContentSeederOptions { MasterLanguage = "en" }),
            definitions,
            NullLogger<SeedOrchestrator>.Instance,
            new SeedDefinitionAccessor(),
            new NullVideoProvider(NullLogger<SeedOrchestrator>.Instance));
    }

    [Fact]
    public async Task DeleteAllForOneDefinition_DeletesOnlyThatDefinitionsRecordsAndImages()
    {
        var sut = CreateSut();

        await sut.DeleteAllAsync("b");

        _tracker.Verify(t => t.GetByDefinitionAsync("b", It.IsAny<CancellationToken>()), Times.Once);
        _tracker.Verify(t => t.GetByDefinitionAsync("a", It.IsAny<CancellationToken>()), Times.Never);
        _tracker.Verify(t => t.DeleteDefinitionAsync("b", It.IsAny<CancellationToken>()), Times.Once);
        _tracker.Verify(t => t.TruncateAsync(It.IsAny<CancellationToken>()), Times.Never);
        _images.Verify(i => i.DeleteSeededImagesAsync("b", It.IsAny<CancellationToken>()), Times.Once);
        _images.Verify(i => i.DeleteSeededImagesAsync(null, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteAllForEveryDefinition_ClearsTheWholeTrackingTableAndImages()
    {
        var sut = CreateSut();

        await sut.DeleteAllAsync();

        _tracker.Verify(t => t.TruncateAsync(It.IsAny<CancellationToken>()), Times.Once);
        _tracker.Verify(t => t.DeleteDefinitionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _images.Verify(i => i.DeleteSeededImagesAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SeedOneDefinition_NeverReadsAnotherDefinitionsRecords()
    {
        var sut = CreateSut();

        await sut.SeedAsync("a");

        _tracker.Verify(t => t.GetByDefinitionAsync("a", It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _tracker.Verify(t => t.GetByDefinitionAsync("b", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DryRunForOneDefinition_ReportsOnlyThatDefinition()
    {
        var sut = CreateSut();

        var result = await sut.DryRunAsync("a");

        var only = Assert.Single(result.Definitions);
        Assert.Equal("a", only.SeedDefinitionId);
    }

    [Fact]
    public async Task SeedUnknownDefinition_Throws()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(() => sut.SeedAsync("missing"));
    }

    [Fact]
    public async Task DeleteAllForAnUnregisteredDefinition_StillCleansUpItsRecords()
    {
        // A definition removed from the code can still have content in the CMS. Deleting it must still work.
        var sut = CreateSut();

        await sut.DeleteAllAsync("removed-long-ago");

        _tracker.Verify(t => t.DeleteDefinitionAsync("removed-long-ago", It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class EmptyDefinition(string id) : ISeedDefinition
    {
        public string Id => id;
        public SeedTree Build() => new();
    }
}

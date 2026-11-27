using EPiServer;
using EPiServer.Core;
using Moq;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Orchestration;

public sealed class EditorContentGuardTests
{
    [Fact]
    public async Task ReportAsync_IgnoresPartialPageRecordsWithoutAContentId()
    {
        const string definitionId = "partial-pages";
        var records = new[]
        {
            MakeRecord(definitionId, 0, isPartial: true),
            MakeRecord(definitionId, 10),
        };
        var tracker = new Mock<ISeedTracker>();
        tracker.Setup(t => t.GetByDefinitionAsync(definitionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var repository = new Mock<IContentRepository>(MockBehavior.Strict);
        repository.Setup(r => r.GetChildren<PageData>(new ContentReference(10)))
            .Returns([]);
        repository.Setup(r => r.GetChildren<PageData>(ContentReference.RootPage))
            .Returns([]);

        var definition = new Mock<ISeedDefinition>();
        definition.SetupGet(d => d.Id).Returns(definitionId);
        var sut = new EditorContentGuard(repository.Object, tracker.Object, [definition.Object]);

        var report = await sut.ReportAsync(definitionId, CancellationToken.None);

        Assert.Empty(report.Pages);
        Assert.Empty(report.Targets);
        repository.Verify(
            r => r.GetChildren<PageData>(It.IsAny<ContentReference>()),
            Times.Exactly(2));
        repository.Verify(
            r => r.GetChildren<PageData>(new ContentReference(10)),
            Times.Once);
    }

    private static ContentSeederRecord MakeRecord(string definitionId, int contentId, bool isPartial = false)
        => new()
        {
            Id = Guid.NewGuid(),
            SeedDefinitionId = definitionId,
            ContentId = contentId,
            ContentType = ContentSeederRecord.ContentTypes.Page,
            Language = "en",
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            SeedRunId = Guid.NewGuid(),
            IsPartial = isPartial,
        };
}

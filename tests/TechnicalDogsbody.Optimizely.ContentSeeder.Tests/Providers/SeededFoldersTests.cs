using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.Security;
using Moq;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Providers;

public class SeededFoldersTests
{
    [Fact]
    public void OwnerPath_NoDefinition_ReturnsBaseFolder()
    {
        Assert.Equal("Seeded", SeededFolders.OwnerPath("Seeded", null));
    }

    [Fact]
    public void OwnerPath_DefinitionId_IsJoinedUnderBase()
    {
        Assert.Equal("Seeded/demo-seed-v1", SeededFolders.OwnerPath("Seeded", "demo-seed-v1"));
    }

    [Fact]
    public void OwnerPath_IdWithCharactersUnsafeForAFolderName_AreReplacedWithDashes()
    {
        Assert.Equal("Seeded/my-seed-v1", SeededFolders.OwnerPath("Seeded/", "my seed/v1"));
    }

    [Fact]
    public void GetOrCreate_NoFoldersYet_CreatesEachSegmentUnderTheRoot()
    {
        var repo = new Mock<IContentRepository>();
        int nextId = 100;
        repo.Setup(r => r.GetDefault<ContentFolder>(It.IsAny<ContentReference>())).Returns(() => new ContentFolder());
        repo.Setup(r => r.Save(It.IsAny<IContent>(), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>()))
            .Returns(() => new ContentReference(++nextId));

        var folders = new SeededFolders(repo.Object, new Mock<IContentLoader>().Object);

        var result = folders.GetOrCreate(ContentReference.GlobalBlockFolder, "Seeded/demo-seed-v1");

        repo.Verify(r => r.GetDefault<ContentFolder>(ContentReference.GlobalBlockFolder), Times.Once);
        repo.Verify(r => r.Save(It.IsAny<IContent>(), SaveAction.Publish, AccessLevel.NoAccess), Times.Exactly(2));
        Assert.Equal(new ContentReference(102), result);
    }

    [Fact]
    public void GetOrCreate_CalledTwice_ReusesTheCachedFolder()
    {
        var repo = new Mock<IContentRepository>();
        repo.Setup(r => r.GetDefault<ContentFolder>(It.IsAny<ContentReference>())).Returns(() => new ContentFolder());
        repo.Setup(r => r.Save(It.IsAny<IContent>(), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>()))
            .Returns(new ContentReference(7));

        var folders = new SeededFolders(repo.Object, new Mock<IContentLoader>().Object);

        var first = folders.GetOrCreate(ContentReference.GlobalBlockFolder, "Seeded/x");
        var second = folders.GetOrCreate(ContentReference.GlobalBlockFolder, "Seeded/x");

        Assert.Equal(first, second);
        repo.Verify(r => r.GetDefault<ContentFolder>(It.IsAny<ContentReference>()), Times.Exactly(2));
    }

    [Fact]
    public void GetOrCreate_AfterClear_ReadsTheCmsAgain()
    {
        var repo = new Mock<IContentRepository>();
        repo.Setup(r => r.GetDefault<ContentFolder>(It.IsAny<ContentReference>())).Returns(() => new ContentFolder());
        repo.Setup(r => r.Save(It.IsAny<IContent>(), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>()))
            .Returns(new ContentReference(7));

        var folders = new SeededFolders(repo.Object, new Mock<IContentLoader>().Object);
        folders.GetOrCreate(ContentReference.GlobalBlockFolder, "Seeded/x");
        folders.Clear();
        folders.GetOrCreate(ContentReference.GlobalBlockFolder, "Seeded/x");

        repo.Verify(r => r.GetDefault<ContentFolder>(It.IsAny<ContentReference>()), Times.Exactly(4));
    }

    [Fact]
    public void Find_FolderMissing_ReturnsNullAndCreatesNothing()
    {
        var repo = new Mock<IContentRepository>();
        var folders = new SeededFolders(repo.Object, new Mock<IContentLoader>().Object);

        var result = folders.Find(ContentReference.GlobalBlockFolder, "Seeded/x");

        Assert.Null(result);
        repo.Verify(r => r.Save(It.IsAny<IContent>(), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>()), Times.Never);
    }
}

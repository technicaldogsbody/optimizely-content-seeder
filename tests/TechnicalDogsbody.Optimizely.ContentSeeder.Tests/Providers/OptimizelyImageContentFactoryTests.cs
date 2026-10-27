using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using System.Net;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.Framework.Blobs;
using EPiServer.Security;
using Microsoft.Extensions.Logging.Abstractions;
using OptionsFactory = Microsoft.Extensions.Options.Options;
using Moq;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Providers;

public class OptimizelyImageContentFactoryTests
{
    // Not sealed so Moq/reflection-based GetDefault<T> invocation behaves like a real content type.
    private class FakeImageData : ImageData { }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Mock<IContentTypeRepository> CreateContentTypeRepositoryMock()
    {
        var mock = new Mock<IContentTypeRepository>();
        mock.Setup(r => r.List()).Returns([new ContentType { ID = 1, ModelType = typeof(FakeImageData) }]);
        mock.Setup(r => r.Load(It.IsAny<Type>())).Returns(new ContentType { ID = 1 });
        return mock;
    }

    private static Mock<IBlobFactory> CreateBlobFactoryMock()
    {
        var blobMock = new Mock<Blob>(new Uri("blob:fake"));
        blobMock.Setup(b => b.OpenWrite()).Returns(new MemoryStream());

        var factoryMock = new Mock<IBlobFactory>();
        factoryMock.Setup(f => f.CreateBlob(It.IsAny<Uri>(), It.IsAny<string>())).Returns(blobMock.Object);
        return factoryMock;
    }

    private static Mock<IHttpClientFactory> CreateHttpClientFactoryMock(
        HttpStatusCode statusCode = HttpStatusCode.OK, byte[]? content = null)
    {
        var handler = new FakeHttpMessageHandler(statusCode, content ?? [1, 2, 3]);
        var client = new HttpClient(handler);

        var mock = new Mock<IHttpClientFactory>();
        mock.Setup(f => f.CreateClient(ContentSeederNames.HttpClient)).Returns(client);
        return mock;
    }

    private sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new ByteArrayContent(content)
            };
            return Task.FromResult(response);
        }
    }

    private static OptimizelyImageContentFactory CreateSut(
        IContentRepository repo,
        IContentLoader contentLoader,
        IBlobFactory? blobFactory = null,
        IContentTypeRepository? contentTypeRepository = null,
        IHttpClientFactory? httpClientFactory = null,
        ContentSeederOptions? options = null)
    {
        var writer = new SeededMediaWriter(
            repo,
            contentLoader,
            blobFactory ?? CreateBlobFactoryMock().Object,
            contentTypeRepository ?? CreateContentTypeRepositoryMock().Object,
            OptionsFactory.Create(options ?? new ContentSeederOptions()),
            new SeedDefinitionAccessor(), NullLogger<SeededMediaWriter>.Instance);

        return new OptimizelyImageContentFactory(
            writer,
            new PicsumImageSource(httpClientFactory ?? CreateHttpClientFactoryMock().Object, NullLogger<PicsumImageSource>.Instance),
            NullLogger<OptimizelyImageContentFactory>.Instance);
    }

    // ── folder creation ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateImageAsync_SeededFolderMissing_CreatesEachSegmentAndSavesImageUnderIt()
    {
        var contentLoaderMock = new Mock<IContentLoader>();
        contentLoaderMock.Setup(l => l.GetChildren<ContentFolder>(It.IsAny<ContentReference>()))
            .Returns([]); // no existing folders at any level

        var repoMock = new Mock<IContentRepository>();
        var savedFolderNames = new List<string>();
        repoMock.Setup(r => r.GetDefault<ContentFolder>(It.IsAny<ContentReference>()))
            .Returns(new ContentFolder());
        repoMock.Setup(r => r.GetDefault<FakeImageData>(It.IsAny<ContentReference>()))
            .Returns(new FakeImageData());

        int nextId = 100;
        repoMock.Setup(r => r.Save(It.IsAny<IContent>(), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>()))
            .Returns((IContent content, SaveAction _, AccessLevel _) =>
            {
                if (content is ContentFolder folder)
                    savedFolderNames.Add(folder.Name);
                return new ContentReference(nextId++);
            });

        var sut = CreateSut(repoMock.Object, contentLoaderMock.Object,
            options: new ContentSeederOptions { SeededImagesFolder = "Images/Seeded" });

        var result = await sut.CreateImageAsync(100, 100);

        Assert.False(result.IsEmpty);
        Assert.Equal(["Images", "Seeded"], savedFolderNames);
    }

    [Fact]
    public async Task CreateImageAsync_SeededFolderAlreadyExists_ReusesItWithoutCreatingFolders()
    {
        // The root the factory resolves under (SiteDefinition.Current?.GlobalAssetsRoot ??
        // ContentReference.GlobalBlockFolder) is environment-dependent, so the first segment
        // is matched with It.IsAny and only the deeper, factory-created link (10) is pinned.
        var imagesFolder = new ContentFolder { Name = "Images" };
        typeof(IContent).GetProperty(nameof(IContent.ContentLink))!.SetValue(imagesFolder, new ContentReference(10));
        var seededFolder = new ContentFolder { Name = "Seeded" };
        typeof(IContent).GetProperty(nameof(IContent.ContentLink))!.SetValue(seededFolder, new ContentReference(11));

        var contentLoaderMock = new Mock<IContentLoader>();
        contentLoaderMock.Setup(l => l.GetChildren<ContentFolder>(It.IsAny<ContentReference>()))
            .Returns([imagesFolder]);
        contentLoaderMock.Setup(l => l.GetChildren<ContentFolder>(new ContentReference(10)))
            .Returns([seededFolder]);

        var repoMock = new Mock<IContentRepository>(MockBehavior.Strict);
        repoMock.Setup(r => r.GetDefault<FakeImageData>(new ContentReference(11)))
            .Returns(new FakeImageData());
        repoMock.Setup(r => r.Save(It.IsAny<IContent>(), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>()))
            .Returns(new ContentReference(200));
        // No GetDefault<ContentFolder> setup — a strict mock call would fail the test if folder creation were attempted.

        var sut = CreateSut(repoMock.Object, contentLoaderMock.Object,
            options: new ContentSeederOptions { SeededImagesFolder = "Images/Seeded" });

        var result = await sut.CreateImageAsync(100, 100);

        Assert.Equal("200", result.Id);
    }

    [Fact]
    public async Task CreateImageAsync_CalledTwice_ResolvesFolderOnlyOnce()
    {
        var contentLoaderMock = new Mock<IContentLoader>();
        contentLoaderMock.Setup(l => l.GetChildren<ContentFolder>(It.IsAny<ContentReference>()))
            .Returns([]);

        var repoMock = new Mock<IContentRepository>();
        repoMock.Setup(r => r.GetDefault<ContentFolder>(It.IsAny<ContentReference>()))
            .Returns(new ContentFolder());
        repoMock.Setup(r => r.GetDefault<FakeImageData>(It.IsAny<ContentReference>()))
            .Returns(new FakeImageData());
        repoMock.Setup(r => r.Save(It.IsAny<IContent>(), It.IsAny<SaveAction>(), It.IsAny<AccessLevel>()))
            .Returns(new ContentReference(1));

        var sut = CreateSut(repoMock.Object, contentLoaderMock.Object,
            options: new ContentSeederOptions { SeededImagesFolder = "Images/Seeded" });

        await sut.CreateImageAsync(100, 100);
        await sut.CreateImageAsync(200, 200);

        // Two path segments resolved once each (not once per CreateImageAsync call).
        contentLoaderMock.Verify(l => l.GetChildren<ContentFolder>(It.IsAny<ContentReference>()), Times.Exactly(2));
    }

    // ── delete ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteSeededImagesAsync_FolderExists_DeletesItRecursively()
    {
        // The root the factory resolves under is environment-dependent (see comment in
        // CreateImageAsync_SeededFolderAlreadyExists_ReusesItWithoutCreatingFolders above).
        var imagesFolder = new ContentFolder { Name = "Images" };
        typeof(IContent).GetProperty(nameof(IContent.ContentLink))!.SetValue(imagesFolder, new ContentReference(10));
        var seededFolder = new ContentFolder { Name = "Seeded" };
        typeof(IContent).GetProperty(nameof(IContent.ContentLink))!.SetValue(seededFolder, new ContentReference(11));

        var contentLoaderMock = new Mock<IContentLoader>();
        contentLoaderMock.Setup(l => l.GetChildren<ContentFolder>(It.IsAny<ContentReference>()))
            .Returns([imagesFolder]);
        contentLoaderMock.Setup(l => l.GetChildren<ContentFolder>(new ContentReference(10)))
            .Returns([seededFolder]);

        var repoMock = new Mock<IContentRepository>();
        var deletedRefs = new List<ContentReference>();
        repoMock.Setup(r => r.Delete(It.IsAny<ContentReference>(), true, It.IsAny<AccessLevel>()))
            .Callback<ContentReference, bool, AccessLevel>((cr, _, _) => deletedRefs.Add(cr));

        var sut = CreateSut(repoMock.Object, contentLoaderMock.Object,
            options: new ContentSeederOptions { SeededImagesFolder = "Images/Seeded" });

        await sut.DeleteSeededImagesAsync(null);

        Assert.Single(deletedRefs);
        Assert.Equal(11, deletedRefs[0].ID);
    }

    [Fact]
    public async Task DeleteSeededImagesAsync_FolderDoesNotExist_DoesNotCallDelete()
    {
        var contentLoaderMock = new Mock<IContentLoader>();
        contentLoaderMock.Setup(l => l.GetChildren<ContentFolder>(It.IsAny<ContentReference>()))
            .Returns([]);

        var repoMock = new Mock<IContentRepository>(MockBehavior.Strict);
        // No Delete setup — a strict mock call would fail the test if Delete were invoked.

        var sut = CreateSut(repoMock.Object, contentLoaderMock.Object);

        var ex = await Record.ExceptionAsync(() => sut.DeleteSeededImagesAsync(null));

        Assert.Null(ex);
    }

    [Fact]
    public async Task DeleteSeededImagesAsync_RepositoryThrows_SwallowsExceptionAndDoesNotPropagate()
    {
        var seededFolder = new ContentFolder { Name = "Seeded" };
        typeof(IContent).GetProperty(nameof(IContent.ContentLink))!.SetValue(seededFolder, new ContentReference(11));

        var contentLoaderMock = new Mock<IContentLoader>();
        contentLoaderMock.Setup(l => l.GetChildren<ContentFolder>(It.IsAny<ContentReference>()))
            .Returns([seededFolder]);

        var repoMock = new Mock<IContentRepository>();
        repoMock.Setup(r => r.Delete(It.IsAny<ContentReference>(), It.IsAny<bool>(), It.IsAny<AccessLevel>()))
            .Throws(new InvalidOperationException("boom"));

        var sut = CreateSut(repoMock.Object, contentLoaderMock.Object,
            options: new ContentSeederOptions { SeededImagesFolder = "Seeded" });

        var ex = await Record.ExceptionAsync(() => sut.DeleteSeededImagesAsync(null));

        Assert.Null(ex);
    }

    [Fact]
    public async Task DeleteSeededImagesAsync_CancelledToken_ThrowsOperationCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = CreateSut(Mock.Of<IContentRepository>(), Mock.Of<IContentLoader>());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.DeleteSeededImagesAsync(null, cts.Token));
    }

    // ── existing behaviour baseline ──────────────────────────────────────────

    [Fact]
    public async Task CreateImageAsync_NoImageDataTypeRegistered_ReturnsEmptyReference()
    {
        var contentTypeRepoMock = new Mock<IContentTypeRepository>();
        contentTypeRepoMock.Setup(r => r.List()).Returns([]); // no ImageData subtype registered

        var sut = CreateSut(Mock.Of<IContentRepository>(), Mock.Of<IContentLoader>(),
            contentTypeRepository: contentTypeRepoMock.Object);

        var result = await sut.CreateImageAsync(100, 100);

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public async Task CreateImageAsync_HttpDownloadFails_ReturnsEmptyReference()
    {
        var httpFactoryMock = CreateHttpClientFactoryMock(HttpStatusCode.InternalServerError);

        var sut = CreateSut(Mock.Of<IContentRepository>(), Mock.Of<IContentLoader>(),
            httpClientFactory: httpFactoryMock.Object);

        var result = await sut.CreateImageAsync(100, 100);

        Assert.True(result.IsEmpty);
    }
}

using Moq;
using Xunit;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Providers;

public class FallbackImageSourceTests
{
    private static readonly ImageRequest Request = new(100, 100);

    private static Mock<IImageSource> Source(GeneratedImage? result)
    {
        var mock = new Mock<IImageSource>();
        mock.Setup(s => s.GetImageAsync(It.IsAny<ImageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    [Fact]
    public async Task GetImageAsync_PrimaryWorks_DoesNotCallFallback()
    {
        var primary = Source(new GeneratedImage([1], ".jpg"));
        var fallback = Source(new GeneratedImage([2], ".png"));

        var image = await new FallbackImageSource(primary.Object, fallback.Object).GetImageAsync(Request);

        Assert.Equal([1], image!.Bytes);
        fallback.Verify(s => s.GetImageAsync(It.IsAny<ImageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetImageAsync_PrimaryFails_UsesFallbackAndSkipsPrimaryAfterwards()
    {
        var primary = Source(null);
        var fallback = Source(new GeneratedImage([2], ".png"));
        var sut = new FallbackImageSource(primary.Object, fallback.Object);

        await sut.GetImageAsync(Request);
        var image = await sut.GetImageAsync(Request);

        Assert.Equal([2], image!.Bytes);
        primary.Verify(s => s.GetImageAsync(It.IsAny<ImageRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        fallback.Verify(s => s.GetImageAsync(It.IsAny<ImageRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetImageAsync_PrimaryIsTheFallback_CallsItOnce()
    {
        var only = Source(null);

        var image = await new FallbackImageSource(only.Object, only.Object).GetImageAsync(Request);

        Assert.Null(image);
        only.Verify(s => s.GetImageAsync(It.IsAny<ImageRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}

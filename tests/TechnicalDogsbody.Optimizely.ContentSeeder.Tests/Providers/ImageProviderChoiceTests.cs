using System.Reflection;
using EPiServer.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OptionsFactory = Microsoft.Extensions.Options.Options;
using Xunit;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Providers;

public class ImageProviderChoiceTests
{
    private sealed class Page
    {
        public ContentReference? HeroImage { get; set; }
        public ContentReference? Thumbnail { get; set; }
    }

    private static BogusContentGenerationProvider CreateSut(IImageContentFactory factory)
        => new(factory,
            new DefaultBogusPropertyMap(OptionsFactory.Create(new BogusGeneratorMapOptions())),
            OptionsFactory.Create(new ContentSeederOptions { MasterLanguage = "en" }),
            NullLogger<BogusContentGenerationProvider>.Instance,
            richText: new XhtmlRichTextConverter(),
            contentModel: new OptimizelyContentModelAdapter());

    private static IEnumerable<PropertyInfo> Props => typeof(Page).GetProperties();

    [Fact]
    public async Task Populate_PropertyWithAProvider_AsksTheFactoryForThatSource()
    {
        var factory = new Mock<IImageContentFactory>();
        factory.Setup(f => f.CreateImageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<ImageProvider?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentHandle.Empty);
        factory.Setup(f => f.CreateImageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentHandle.Empty);

        await CreateSut(factory.Object).PopulateAsync(
            new Page(), Props, "en", typeof(Page),
            new Dictionary<string, (int, int)> { [nameof(Page.HeroImage)] = (1200, 630), [nameof(Page.Thumbnail)] = (400, 300) },
            new Dictionary<string, ImageProvider> { [nameof(Page.HeroImage)] = ImageProvider.Generated });

        factory.Verify(f => f.CreateImageAsync(1200, 630, ImageProvider.Generated, It.IsAny<CancellationToken>()), Times.Once);
        // The property with no entry uses the default path, so the configured provider applies.
        factory.Verify(f => f.CreateImageAsync(400, 300, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FallbackImageSource_RequestNamesAProvider_UsesThatSourceNotTheDefault()
    {
        var picsum = new Mock<IImageSource>();
        var doodle = new Mock<IImageSource>();
        var generated = new Mock<IImageSource>();
        picsum.Setup(s => s.GetImageAsync(It.IsAny<ImageRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new GeneratedImage([1]));
        doodle.Setup(s => s.GetImageAsync(It.IsAny<ImageRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new GeneratedImage([2]));
        generated.Setup(s => s.GetImageAsync(It.IsAny<ImageRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new GeneratedImage([3]));

        var sut = new FallbackImageSource(
            provider => provider switch
            {
                ImageProvider.DoodleIpsum => doodle.Object,
                ImageProvider.Generated => generated.Object,
                _ => picsum.Object
            },
            ImageProvider.Picsum,
            generated.Object);

        Assert.Equal([1], (await sut.GetImageAsync(new ImageRequest(10, 10)))!.Bytes);
        Assert.Equal([2], (await sut.GetImageAsync(new ImageRequest(10, 10, ImageProvider.DoodleIpsum)))!.Bytes);
        Assert.Equal([3], (await sut.GetImageAsync(new ImageRequest(10, 10, ImageProvider.Generated)))!.Bytes);
    }

    [Fact]
    public async Task FallbackImageSource_OneProviderFails_OthersAreStillTried()
    {
        var picsum = new Mock<IImageSource>();
        var doodle = new Mock<IImageSource>();
        var generated = new Mock<IImageSource>();
        picsum.Setup(s => s.GetImageAsync(It.IsAny<ImageRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync((GeneratedImage?)null);
        doodle.Setup(s => s.GetImageAsync(It.IsAny<ImageRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new GeneratedImage([2]));
        generated.Setup(s => s.GetImageAsync(It.IsAny<ImageRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new GeneratedImage([3]));

        var sut = new FallbackImageSource(
            provider => provider == ImageProvider.DoodleIpsum ? doodle.Object : picsum.Object,
            ImageProvider.Picsum,
            generated.Object);

        // Picsum fails, so it falls back to generated, and is skipped from then on.
        Assert.Equal([3], (await sut.GetImageAsync(new ImageRequest(10, 10)))!.Bytes);
        Assert.Equal([3], (await sut.GetImageAsync(new ImageRequest(10, 10)))!.Bytes);
        picsum.Verify(s => s.GetImageAsync(It.IsAny<ImageRequest>(), It.IsAny<CancellationToken>()), Times.Once);

        // A different provider is unaffected.
        Assert.Equal([2], (await sut.GetImageAsync(new ImageRequest(10, 10, ImageProvider.DoodleIpsum)))!.Bytes);
    }

    [Fact]
    public void ImageHelper_CarriesSizesAndProviders_ThroughImageSizes()
    {
        var node = SeedFactory.Page(typeof(Page),
            imageSizes: SeedFactory.Image("HeroImage", 1200, 630, ImageProvider.Generated).Image("Thumbnail", 400, 300));

        Assert.Equal((1200, 630), node.ImageSizes!["HeroImage"]);
        Assert.Equal((400, 300), node.ImageSizes["Thumbnail"]);
        Assert.Equal(ImageProvider.Generated, node.ImageProviders!["HeroImage"]);
        Assert.False(node.ImageProviders.ContainsKey("Thumbnail"));
    }

    [Fact]
    public void ImageHelper_ExplicitImageProviders_WinOverTheHelper()
    {
        var node = SeedFactory.Page(typeof(Page),
            imageSizes: SeedFactory.Image("HeroImage", 1200, 630, ImageProvider.Generated),
            imageProviders: new Dictionary<string, ImageProvider> { ["HeroImage"] = ImageProvider.DoodleIpsum });

        Assert.Equal(ImageProvider.DoodleIpsum, node.ImageProviders!["HeroImage"]);
    }

    [Fact]
    public void VideoHelper_ChainsSources_AndIsAPlainVideosDictionary()
    {
        var node = SeedFactory.Page(typeof(Page),
            videos: SeedFactory.Video("VideoUrl", VideoSource.Urls("https://youtu.be/aqz-KE-bpKQ")).Video("Video", VideoSource.Uploaded()));

        Assert.Equal(2, node.Videos.Count);
        Assert.Equal(VideoSourceKind.UrlList, node.Videos["VideoUrl"].Kind);
        Assert.Equal(VideoSourceKind.Uploaded, node.Videos["Video"].Kind);
    }

    [Fact]
    public void PlainImageSizes_HaveNoProviders()
    {
        var node = SeedFactory.Page(typeof(Page), imageSizes: new Dictionary<string, (int, int)> { ["HeroImage"] = (1, 1) });

        Assert.Null(node.ImageProviders);
    }

    [Fact]
    public void Fingerprint_NoProviders_IsUnchanged_AndAProviderChangesIt()
    {
        var sizes = new Dictionary<string, (int, int)> { ["HeroImage"] = (1200, 630) };
        string none = SeedTreeFingerprint.Images(sizes);

        Assert.Equal(none, SeedTreeFingerprint.Images(sizes, null));
        Assert.Equal(none, SeedTreeFingerprint.Images(sizes, new Dictionary<string, ImageProvider>()));
        Assert.NotEqual(none, SeedTreeFingerprint.Images(sizes, new Dictionary<string, ImageProvider> { ["HeroImage"] = ImageProvider.Generated }));
        Assert.NotEqual(
            SeedTreeFingerprint.Images(sizes, new Dictionary<string, ImageProvider> { ["HeroImage"] = ImageProvider.Generated }),
            SeedTreeFingerprint.Images(sizes, new Dictionary<string, ImageProvider> { ["HeroImage"] = ImageProvider.DoodleIpsum }));
    }
}

using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Providers;

public class VideoUrlsTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=aqz-KE-bpKQ", "aqz-KE-bpKQ")]
    [InlineData("https://youtube.com/watch?feature=share&v=aqz-KE-bpKQ", "aqz-KE-bpKQ")]
    [InlineData("https://m.youtube.com/watch?v=aqz-KE-bpKQ&t=30s", "aqz-KE-bpKQ")]
    [InlineData("https://youtu.be/aqz-KE-bpKQ", "aqz-KE-bpKQ")]
    [InlineData("https://youtu.be/aqz-KE-bpKQ?si=abc", "aqz-KE-bpKQ")]
    [InlineData("https://www.youtube.com/embed/aqz-KE-bpKQ", "aqz-KE-bpKQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/aqz-KE-bpKQ", "aqz-KE-bpKQ")]
    [InlineData("https://www.youtube.com/shorts/aqz-KE-bpKQ", "aqz-KE-bpKQ")]
    [InlineData("https://www.youtube.com/live/aqz-KE-bpKQ", "aqz-KE-bpKQ")]
    public void YouTube_UrlForms_AreRecognised(string url, string id)
    {
        Assert.True(VideoUrls.TryParse(url, out var video));
        Assert.Equal(VideoSite.YouTube, video.Site);
        Assert.Equal(id, video.Id);
    }

    [Theory]
    [InlineData("https://vimeo.com/265010648", "265010648")]
    [InlineData("https://www.vimeo.com/265010648", "265010648")]
    [InlineData("https://vimeo.com/channels/staffpicks/1235421735", "1235421735")]
    [InlineData("https://vimeo.com/groups/motion/videos/265010648", "265010648")]
    [InlineData("https://player.vimeo.com/video/265010648", "265010648")]
    [InlineData("https://player.vimeo.com/video/265010648?h=abcdef", "265010648")]
    [InlineData("https://vimeo.com/265010648/abcdef1234", "265010648")]
    public void Vimeo_UrlForms_AreRecognised(string url, string id)
    {
        Assert.True(VideoUrls.TryParse(url, out var video));
        Assert.Equal(VideoSite.Vimeo, video.Site);
        Assert.Equal(id, video.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("https://example.com/watch?v=aqz-KE-bpKQ")]
    [InlineData("https://www.youtube.com/@BlenderOfficial")]
    [InlineData("https://www.youtube.com/channel/UCSMOQeBJ2RAnuFungnQOxLg")]
    [InlineData("https://www.youtube.com/watch")]
    [InlineData("https://www.youtube.com/watch?v=short")]
    [InlineData("https://vimeo.com/channels/staffpicks")]
    [InlineData("https://vimeo.com/yourown3dsoftware/videos")]
    [InlineData("ftp://vimeo.com/265010648")]
    public void NotAVideo_IsRejected(string? url)
    {
        Assert.False(VideoUrls.TryParse(url, out _));
    }

    [Fact]
    public void WatchAndEmbedUrls_AreBuiltFromTheId()
    {
        VideoUrls.TryParse("https://youtu.be/aqz-KE-bpKQ", out var youTube);
        VideoUrls.TryParse("https://player.vimeo.com/video/265010648", out var vimeo);

        Assert.Equal("https://www.youtube.com/watch?v=aqz-KE-bpKQ", youTube.WatchUrl);
        Assert.Equal("https://www.youtube-nocookie.com/embed/aqz-KE-bpKQ", youTube.EmbedUrl);
        Assert.Equal("https://vimeo.com/265010648", vimeo.WatchUrl);
        Assert.Equal("https://player.vimeo.com/video/265010648", vimeo.EmbedUrl);
    }
}

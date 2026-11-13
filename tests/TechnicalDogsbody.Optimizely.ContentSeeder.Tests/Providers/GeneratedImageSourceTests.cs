using Xunit;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Providers;

public class GeneratedImageSourceTests
{
    [Fact]
    public async Task GetImageAsync_ReturnsPngWithRequestedDimensions()
    {
        var image = await new GeneratedImageSource().GetImageAsync(new ImageRequest(320, 200));

        Assert.NotNull(image);
        Assert.Equal(".png", image.Extension);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], image.Bytes[..8]);
        // IHDR width/height are the first two big-endian ints after the 8-byte signature + 8-byte chunk header.
        Assert.Equal(320, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(image.Bytes.AsSpan(16)));
        Assert.Equal(200, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(image.Bytes.AsSpan(20)));
    }
}

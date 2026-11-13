using TechnicalDogsbody.Optimizely.ContentSeeder.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

/// <summary>What an image is for.</summary>
/// <param name="Width">Image width in pixels.</param>
/// <param name="Height">Image height in pixels.</param>
/// <param name="Provider">The source this image asked for, or null to use the configured default (<c>ContentSeeder:ImageProvider</c>).</param>
public sealed record ImageRequest(int Width, int Height, ImageProvider? Provider = null);

/// <summary>Encoded image data produced by an <see cref="IImageSource"/>.</summary>
/// <param name="Bytes">The encoded image.</param>
/// <param name="Extension">File extension including the dot, e.g. ".jpg" or ".png".</param>
public sealed record GeneratedImage(byte[] Bytes, string Extension = ".jpg");

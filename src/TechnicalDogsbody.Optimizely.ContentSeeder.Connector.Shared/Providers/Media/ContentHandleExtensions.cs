using System.Globalization;
using EPiServer.Core;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

/// <summary>
/// Converts between the neutral <see cref="ContentHandle"/> and Optimizely's <see cref="ContentReference"/>.
/// </summary>
internal static class ContentHandleExtensions
{
    /// <summary>Returns the handle for a reference, or <see cref="ContentHandle.Empty"/> when it is null or empty.</summary>
    public static ContentHandle ToHandle(this ContentReference? reference)
        => reference is null || ContentReference.IsNullOrEmpty(reference)
            ? ContentHandle.Empty
            : new ContentHandle(reference.ID.ToString(CultureInfo.InvariantCulture));

    /// <summary>Returns the reference for a handle. The handle must not be empty.</summary>
    public static ContentReference ToReference(this ContentHandle handle)
        => new(int.Parse(handle.Id, CultureInfo.InvariantCulture));
}

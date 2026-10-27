using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using EPiServer;
using EPiServer.Core;
using EPiServer.DataAbstraction;
using EPiServer.DataAccess;
using EPiServer.DataAnnotations;
using EPiServer.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Infrastructure;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Media;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;

/// <summary>
/// Chooses how to save seeded content: published, or as a draft when a required property is empty.
/// </summary>
internal sealed class SaveActions(ILogger logger)
{
    // Chooses how to save an item: Publish, or Save (a draft) when a [Required] property is empty, since a required field
    // that is blank cannot be published. The item is still created, so the seed carries on.
    /// <summary>
    /// Chooses whether an item is published or saved as a draft. A required property that is empty means a draft, since it cannot be published. The choice is logged.
    /// </summary>
    internal SaveAction ActionFor(object content, IEnumerable<PropertyInfo> properties, string description)
    {
        var empty = properties
            .Where(p => p.CanRead && Attribute.IsDefined(p, typeof(System.ComponentModel.DataAnnotations.RequiredAttribute))
                        && IsEmpty(p.GetValue(content)))
            .Select(p => p.Name)
            .ToList();

        if (empty.Count == 0)
            return SaveAction.Publish;

        logger.LogWarning(
            "ContentSeeder: {Item} has an empty required field ({Fields}), so it is saved as a draft and not published.",
            description, string.Join(", ", empty));
        return SaveAction.Save;
    }

    private static bool IsEmpty(object? value) => value switch
    {
        null => true,
        string text => string.IsNullOrWhiteSpace(text),
        XhtmlString html => string.IsNullOrWhiteSpace(html.ToString()),
        ContentReference reference => ContentReference.IsNullOrEmpty(reference),
        _ => false,
    };
}

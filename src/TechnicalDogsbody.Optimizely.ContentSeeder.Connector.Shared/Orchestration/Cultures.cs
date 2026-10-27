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
/// Turns a language tag into a <see cref="CultureInfo"/>, falling back to English for an invalid tag.
/// </summary>
internal static class Cultures
{
    /// <summary>
    /// Returns the culture for a language tag. An invalid tag logs a warning and falls back to English.
    /// </summary>
    internal static CultureInfo Resolve(string language, ILogger logger)
    {
        try
        {
            return new CultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            logger.LogWarning(
                "Language tag '{Language}' is not a valid culture identifier. Falling back to 'en'.",
                language);
            return CultureInfo.GetCultureInfo("en");
        }
    }
}

using TechnicalDogsbody.Optimizely.ContentSeeder.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;
using Xunit;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Providers;

public class BogusLocaleResolutionTests
{
    private sealed class ListLogger : ILogger<BogusContentGenerationProvider>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static BogusContentGenerationProvider Provider(ListLogger logger, string? masterLanguage = "en", Action<ContentSeederOptions>? configure = null)
    {
        var options = new ContentSeederOptions { MasterLanguage = masterLanguage };
        configure?.Invoke(options);
        return new BogusContentGenerationProvider(
            Mock.Of<IImageContentFactory>(),
            new DefaultBogusPropertyMap(OptionsFactory.Create(new BogusGeneratorMapOptions())),
            OptionsFactory.Create(options),
            logger, richText: new XhtmlRichTextConverter(), contentModel: new OptimizelyContentModelAdapter());
    }

    [Theory]
    [InlineData("sv", "sv")]
    [InlineData("en-GB", "en_GB")]
    [InlineData("zh", "zh_CN")]
    [InlineData("zh-Hant", "zh_TW")]
    public void MappedTag_UsesItsOwnLocale(string tag, string expected)
    {
        var logger = new ListLogger();

        Assert.Equal(expected, Provider(logger).ResolveLocale(tag));
        Assert.Empty(logger.Entries);
    }

    [Theory]
    [InlineData("ja-JP", "ja")]
    [InlineData("ar-AE", "ar")]
    [InlineData("zh-Hans", "zh_CN")]
    [InlineData("zh-Hans-CN", "zh_CN")]
    [InlineData("fr-BE", "fr")]
    public void UnmappedRegion_FallsBackToItsLanguage_BeforeAnythingElse(string tag, string expected)
    {
        var logger = new ListLogger();

        Assert.Equal(expected, Provider(logger).ResolveLocale(tag));
        // Using the base language is expected, not a warning.
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public void UnknownLanguage_FallsBackToTheMastersLocale()
    {
        var logger = new ListLogger();
        var provider = Provider(logger, masterLanguage: "sv");

        Assert.Equal("sv", provider.ResolveLocale("xx-YY"));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("master language"));
    }

    [Fact]
    public void UnknownLanguage_WithAnUnmappedMasterToo_FallsBackToEnglish()
    {
        var logger = new ListLogger();
        var provider = Provider(logger, masterLanguage: "xx");

        Assert.Equal("en", provider.ResolveLocale("yy"));
    }

    [Fact]
    public void UnknownLanguage_WithNoMasterConfigured_FallsBackToEnglish()
    {
        var logger = new ListLogger();

        Assert.Equal("en", Provider(logger, masterLanguage: null).ResolveLocale("xx"));
    }

    [Fact]
    public void UnmappedLanguage_IsReportedOncePerRun_NotOncePerContentItem()
    {
        var logger = new ListLogger();
        var provider = Provider(logger);

        for (int i = 0; i < 50; i++)
            provider.ResolveLocale("xx-YY");
        for (int i = 0; i < 50; i++)
            provider.ResolveLocale("ja-JP");

        Assert.Single(logger.Entries, e => e.Message.Contains("xx-YY"));
        Assert.Single(logger.Entries, e => e.Message.Contains("ja-JP"));
    }

    [Fact]
    public void ConsumerEntry_OverridesTheDefault_AndAddsNewLanguages()
    {
        var logger = new ListLogger();
        var provider = Provider(logger, configure: o =>
        {
            o.LocaleMap["en-GB"] = "en_AU";
            o.LocaleMap["da"] = "sv";
        });

        Assert.Equal("en_AU", provider.ResolveLocale("en-GB"));
        Assert.Equal("sv", provider.ResolveLocale("da-DK"));
    }

    [Fact]
    public void EveryDefaultLocaleMapEntry_IsALocaleBogusActuallyHas()
    {
        var bogusLocales = Bogus.Database.GetAllLocales().ToHashSet(StringComparer.Ordinal);

        // A value Bogus doesn't know silently produces English, which defeats the point of the mapping.
        var invalid = new ContentSeederOptions().LocaleMap.Where(kv => !bogusLocales.Contains(kv.Value)).ToList();

        Assert.Empty(invalid);
    }
}

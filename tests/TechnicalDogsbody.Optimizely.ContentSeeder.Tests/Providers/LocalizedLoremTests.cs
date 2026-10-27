using System.Text.RegularExpressions;
using Bogus;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Providers;

public partial class LocalizedLoremTests
{
    [GeneratedRegex("[A-Za-z]")]
    private static partial Regex LatinLetter();

    [GeneratedRegex(@"[一-鿿]")]
    private static partial Regex Han();

    public static TheoryData<string> OurLocales()
    {
        var data = new TheoryData<string>();
        foreach (string locale in LocalizedLorem.Locales)
            data.Add(locale);
        return data;
    }

    [Theory]
    [MemberData(nameof(OurLocales))]
    public void Locale_ProducesLoremInItsOwnScript_NotLatin(string locale)
    {
        LocalizedLorem.Ensure(locale);

        var faker = new Faker(locale);
        for (int i = 0; i < 20; i++)
        {
            string text = faker.Lorem.Paragraph();
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotMatch(LatinLetter(), text);
        }
    }

    [Theory]
    [InlineData("zh_CN")]
    [InlineData("zh_TW")]
    public void Chinese_ProducesHanCharacters(string locale)
    {
        LocalizedLorem.Ensure(locale);

        Assert.Matches(Han(), new Faker(locale).Lorem.Sentence(4));
    }

    [Fact]
    public void ChineseVariants_UseTheirOwnCharacterSet()
    {
        LocalizedLorem.Ensure("zh_CN");
        LocalizedLorem.Ensure("zh_TW");

        // "们" is Simplified only; "們" is Traditional only. Over many words each set shows only its own.
        string simplified = string.Join(" ", new Faker("zh_CN").Lorem.Words(200));
        string traditional = string.Join(" ", new Faker("zh_TW").Lorem.Words(200));

        Assert.DoesNotContain('們', simplified);
        Assert.DoesNotContain('们', traditional);
    }

    [Theory]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("ru")]
    public void LocalesBogusAlreadyCovers_AreNotTouched(string locale)
    {
        // Not ours to provide, and Ensure must be a harmless no-op for them.
        Assert.DoesNotContain(locale, LocalizedLorem.Locales);
        LocalizedLorem.Ensure(locale);

        Assert.DoesNotMatch(LatinLetter(), new Faker(locale).Lorem.Sentence(5));
    }

    [Fact]
    public void LatinScriptLocales_KeepBogusLatinLorem()
    {
        LocalizedLorem.Ensure("sv");

        Assert.Matches(LatinLetter(), new Faker("sv").Lorem.Sentence(5));
    }

    [Fact]
    public void Ensure_IsSafeToCallRepeatedlyAndConcurrently()
    {
        Parallel.For(0, 50, _ => LocalizedLorem.Ensure("zh_CN"));

        Assert.Matches(Han(), new Faker("zh_CN").Lorem.Sentence(3));
    }
}

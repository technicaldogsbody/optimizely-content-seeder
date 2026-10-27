using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Ai;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Providers;

public class AiTextFormattingTests
{
    [Fact]
    public void ToHtml_SplitsParagraphsAndKeepsAllowedTags()
    {
        string html = AiTextFormatting.ToHtml("First <b>bold</b> & more.\n\nSecond.", maxParagraphs: 5);

        Assert.Equal("<p>First <b>bold</b> &amp; more.</p><p>Second.</p>", html);
    }

    [Theory]
    [InlineData("<script>alert(1)</script>", "<p>alert(1)</p>")]
    [InlineData("<strong onclick=\"x()\">Hi</strong>", "<p><strong>Hi</strong></p>")]
    [InlineData("<span class=\"c\">Hi</span>", "<p>Hi</p>")]
    [InlineData("1 < 2 and 3 > 2", "<p>1 &lt; 2 and 3 &gt; 2</p>")]
    [InlineData("Tom &amp; Jerry", "<p>Tom &amp; Jerry</p>")]
    public void ToHtml_RemovesUnknownOrUnsafeMarkupAndKeepsItsText(string reply, string expected)
    {
        Assert.Equal(expected, AiTextFormatting.ToHtml(reply, maxParagraphs: 1));
    }

    [Theory]
    [InlineData("<a href=\"https://example.com/x?a=1\">Go</a>", "<p><a href=\"https://example.com/x?a=1\">Go</a></p>")]
    [InlineData("<a href=\"mailto:hi@example.com\">Mail</a>", "<p><a href=\"mailto:hi@example.com\">Mail</a></p>")]
    [InlineData("<a href=\"/about\">About</a>", "<p><a href=\"/about\">About</a></p>")]
    [InlineData("<a href=\"javascript:alert(1)\">Bad</a>", "<p><a>Bad</a></p>")]
    [InlineData("<a href=\"data:text/html,x\">Bad</a>", "<p><a>Bad</a></p>")]
    [InlineData("<a href=\"JaVaScRiPt:alert(1)\">Bad</a>", "<p><a>Bad</a></p>")]
    public void ToHtml_OnlyKeepsSafeHrefs(string reply, string expected)
    {
        Assert.Equal(expected, AiTextFormatting.ToHtml(reply, maxParagraphs: 1));
    }

    [Fact]
    public void ToHtml_KeepsListsAsBlocksWithoutWrappingThemInAParagraph()
    {
        string html = AiTextFormatting.ToHtml("<ul><li>One</li><li>Two</li></ul>", maxParagraphs: 2);

        Assert.Equal("<ul><li>One</li><li>Two</li></ul>", html);
    }

    [Fact]
    public void ToHtml_BreaksWithinAParagraphBecomeLineBreaks()
    {
        Assert.Equal("<p>Line one<br />Line two</p>", AiTextFormatting.ToHtml("Line one\nLine two", maxParagraphs: 1));
    }

    [Fact]
    public void StripTags_RemovesMarkupFromShortText()
    {
        Assert.Equal("Tom & Jerry", AiTextFormatting.StripTags("<em>Tom</em> &amp; Jerry"));
    }

    [Fact]
    public void ToHtml_KeepsNonEnglishCharactersAsWritten()
    {
        Assert.Equal("<p>Ärlig öl 日本語</p>", AiTextFormatting.ToHtml("Ärlig öl 日本語", maxParagraphs: 1));
    }

    [Fact]
    public void ToHtml_StopsAtTheParagraphLimit()
    {
        Assert.Equal("<p>One.</p>", AiTextFormatting.ToHtml("One.\n\nTwo.\n\nThree.", maxParagraphs: 1));
    }

    [Fact]
    public void Exceeds_ChecksWordsAndCharactersIndependently()
    {
        var byWords = new AiTextField("Title", AiTextFieldKind.ShortText, null, MaxWords: 2, MaxCharacters: null);
        var byChars = new AiTextField("Title", AiTextFieldKind.ShortText, null, MaxWords: null, MaxCharacters: 5);

        Assert.True(AiTextFormatting.Exceeds(byWords, "one two three"));
        Assert.False(AiTextFormatting.Exceeds(byWords, "one two"));
        Assert.True(AiTextFormatting.Exceeds(byChars, "too long"));
        Assert.False(AiTextFormatting.Exceeds(byChars, "short"));
    }

    [Fact]
    public void TrimToLimit_CutsToWordLimitAndTrimsDanglingPunctuation()
    {
        var field = new AiTextField("Title", AiTextFieldKind.ShortText, null, MaxWords: 3, MaxCharacters: null);

        Assert.Equal("one two three", AiTextFormatting.TrimToLimit("one two three four five", field));
        Assert.Equal("one two", AiTextFormatting.TrimToLimit("one two, three", new AiTextField("T", AiTextFieldKind.ShortText, null, MaxWords: 2, MaxCharacters: null)));
    }

    [Fact]
    public void TrimToLimit_CutsAtAWordBoundaryWithinCharacterLimit()
    {
        var field = new AiTextField("Title", AiTextFieldKind.ShortText, null, MaxWords: null, MaxCharacters: 10);

        Assert.Equal("hello big", AiTextFormatting.TrimToLimit("hello big world", field));
    }

    [Fact]
    public void ToHtml_RemovesParagraphAndLineBreakTagsTheModelAddsAnyway()
    {
        // The model wrapped its paragraphs in <p> tags. Those are structure, so they must not show as text.
        string html = AiTextFormatting.ToHtml("<p>First line.</p><p>Second<br/>line.</p>", maxParagraphs: 5);

        Assert.Equal("<p>First line.</p><p>Second<br />line.</p>", html);
    }
}

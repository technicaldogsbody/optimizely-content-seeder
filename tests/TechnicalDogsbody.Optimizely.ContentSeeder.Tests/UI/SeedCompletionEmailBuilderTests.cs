using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.UI;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.UI;

public class SeedCompletionEmailBuilderTests
{
    private static readonly DateTime Completed = new(2026, 10, 27, 14, 5, 0, DateTimeKind.Utc);

    private static ContentSeederStatus Status(bool sections) => new()
    {
        TrackedPageCount = 12,
        TrackedBlockCount = 7,
        TrackedSectionCount = sections ? 3 : 0,
        SupportsSections = sections,
    };

    private static readonly SeedDefinitionStatus[] Definitions =
    [
        new("demo-seed-v1", 9, 4, 3, Completed, false),
        new("demo12-seed-v1", 3, 1, 0, null, false),
    ];

    [Theory]
    [InlineData("Seed", "seeded")]
    [InlineData("Reseed", "reseeded")]
    public void Subject_NamesTheOperation(string operation, string verb)
    {
        var email = SeedCompletionEmailBuilder.Build(operation, Completed, Status(true), Definitions, "https://site/cms");

        Assert.Equal($"Content Seeder: {verb} at 14:05 UTC", email.Subject);
    }

    [Fact]
    public void Html_ShowsTheTotalsAndEachDefinition()
    {
        var email = SeedCompletionEmailBuilder.Build("Seed", Completed, Status(true), Definitions, "https://site/cms");

        Assert.Contains(">12<", email.Html);
        Assert.Contains("demo-seed-v1", email.Html);
        Assert.Contains("demo12-seed-v1", email.Html);
        Assert.Contains("never", email.Html);
    }

    [Fact]
    public void Html_HidesSections_WhenTheCmsHasNone()
    {
        var email = SeedCompletionEmailBuilder.Build("Seed", Completed, Status(false), Definitions, "https://site/cms");

        Assert.DoesNotContain("Sections", email.Html);
        Assert.DoesNotContain("Sections", email.Text);
    }

    [Fact]
    public void Html_ShowsSections_WhenTheCmsHasThem()
    {
        var email = SeedCompletionEmailBuilder.Build("Seed", Completed, Status(true), Definitions, "https://site/cms");

        Assert.Contains("Sections", email.Html);
    }

    [Fact]
    public void Html_EncodesDefinitionNames()
    {
        var hostile = new SeedDefinitionStatus("<script>x</script>", 1, 0, 0, null, false);

        var email = SeedCompletionEmailBuilder.Build("Seed", Completed, Status(false), [hostile], "https://site/cms");

        Assert.DoesNotContain("<script>", email.Html);
        Assert.Contains("&lt;script&gt;", email.Html);
    }

    [Fact]
    public void LeavesOutTheLink_WhenNoAdminUrlIsSet()
    {
        var email = SeedCompletionEmailBuilder.Build("Seed", Completed, Status(true), Definitions, string.Empty);

        Assert.DoesNotContain("Open Content Seeder", email.Html);
        Assert.DoesNotContain("Open Content Seeder", email.Text);
    }

    [Fact]
    public void Footer_LinksToTechnicalDogsbody()
    {
        var email = SeedCompletionEmailBuilder.Build("Seed", Completed, Status(true), Definitions, "https://site/cms");

        Assert.Contains("href=\"https://www.technicaldogsbody.com\"", email.Html);
        Assert.Contains("https://www.technicaldogsbody.com", email.Text);
    }

    [Fact]
    public void TextPart_ListsTheSameTotalsAsTheHtml()
    {
        var email = SeedCompletionEmailBuilder.Build("Seed", Completed, Status(true), Definitions, "https://site/cms");

        Assert.Contains("Pages: 12", email.Text);
        Assert.Contains("Blocks: 7", email.Text);
        Assert.Contains("Sections: 3", email.Text);
        Assert.Contains("demo-seed-v1: 9 pages", email.Text);
    }
}

using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Demo12.ContentTypes;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo12.Seeding;

/// <summary>
/// The CMS 12 demo's seed definition: shared blocks, a start page with content areas, articles with a listing page, and
/// products. CMS 12 has no Visual Builder, so there are no sections or Experience pages here.
/// </summary>
public sealed class Demo12SeedDefinition : IAiSeedDefinition
{
    public string Id => "demo12-seed-v1";

    public ContentGenerators Generators { get; } = new ContentGenerators()
        .ForContentType<ArticleListingPage>(t => t
            .Property(x => x.Title, f => "The Leaf & Loam journal"))
        .ForContentType<ArticlePage>(t => t
            .Property(x => x.Title, f => f.Lorem.Sentence(4, 2).TrimEnd('.'))
            .Property(x => x.Author, f => f.Name.FullName())
            .RichText(x => x.Body, o => o.Tags("strong", "em", "a").Probability(0.1).Paragraphs(6)))
        .ForContentType<ProductPage>(t => t
            .Property(x => x.ProductName, f => f.Commerce.ProductAdjective() + " " + f.Commerce.Product())
            .Property(x => x.Description, f => f.Commerce.ProductDescription()));

    // The brief an AI provider writes to (the default Bogus provider ignores it). It is a different fictional business from the
    // two CMS 13 demos, so the three show that a brief is per definition: here, a houseplant nursery.
    public AiWritingBrief WritingBrief { get; } = new AiWritingBrief
    {
        Prompt = "Leaf & Loam, a fictional UK houseplant nursery selling houseplants, pots and plant-care supplies, "
               + "plus guides to watering, repotting and keeping plants healthy indoors.",
        ToneOfVoice = "Warm, calm and a little wry, like a knowledgeable nursery owner talking a customer through their first plant. Plain English.",
        Audience = "Flat dwellers and first-time plant owners, through to keen collectors with a windowsill full of cuttings.",
        Instructions = "British spelling. Sentence case for headings. No exclamation marks. Prefer common names, with the Latin name in brackets where it helps.",
        AvoidPhrases = ["plant parent", "urban jungle", "green fingers", "game-changer"],
        MaxParagraphs = 3,
    }
    // ── pages ──
    // The title comes from its generator rule (a fixed value), so the AI does not write it.
    .ForContentType<ArticleListingPage>(t => t
        .UseGenerator(x => x.Title)
        .Property(x => x.Introduction, "One or two sentences introducing a journal of plant-care notes.", maxWords: 30))
    .ForContentType<ArticlePage>(t => t
        .Prompt("Plant-care guides and growing notes.")
        .Property(x => x.Title, "A specific headline.", maxCharacters: 70)
        .Property(x => x.Introduction, "One or two sentences summarising the guide.", maxWords: 35)
        .Property(x => x.Author, "A realistic full name.")
        // Rich text may use simple HTML. A string property never gets markup, which is why only the body is asked for it.
        .Property(x => x.Body, "Three to four paragraphs. Include one <h2> subheading, a short <ul> list of tips, and <strong> on a key phrase."))
    .ForContentType<ProductPage>(t => t
        .Property(x => x.ProductName, "A plausible plant or pot name, e.g. a fern, a succulent or a glazed ceramic planter.")
        .Property(x => x.Description, "Two or three sentences about what makes it easy to look after."))
    // ── shared blocks ──
    .ForContentType<HeroBlock>(t => t
        .Property(x => x.Heading, "A punchy headline.", maxWords: 6)
        .Property(x => x.SubHeading, "One short sentence.", maxWords: 14)
        .Property(x => x.CtaText, "Two or three words.", maxWords: 3))
    .ForContentType<PromoBlock>(t => t
        .Property(x => x.Title, "A short offer headline.", maxWords: 6)
        .Property(x => x.Body, "One or two sentences about the offer."))
    .ForContentType<TeaserBlock>(t => t
        .Property(x => x.Heading, "A short headline.", maxWords: 6)
        .Property(x => x.Summary, "One sentence."));

    public SeedTree Build()
    {
        var blocks = new List<BlockDefinition>
        {
            new("hero", typeof(HeroBlock), imageSizes: Image(nameof(HeroBlock.BackgroundImage), 1920, 600)),
            new("promo1", typeof(PromoBlock), imageSizes: Image(nameof(PromoBlock.Image), 600, 400)),
            new("promo2", typeof(PromoBlock), imageSizes: Image(nameof(PromoBlock.Image), 600, 400)),
            new("promo3", typeof(PromoBlock), imageSizes: Image(nameof(PromoBlock.Image), 600, 400)),
            new("teaser1", typeof(TeaserBlock), imageSizes: Image(nameof(TeaserBlock.ThumbnailImage), 400, 300)),
            new("teaser2", typeof(TeaserBlock), imageSizes: Image(nameof(TeaserBlock.ThumbnailImage), 400, 300)),
        };

        var articlePage = SeedFactory.Page(
            type: typeof(ArticlePage),
            count: 3,
            configure: (page, i) =>
            {
                var article = (ArticlePage)page;
                article.Name = $"Article {i + 1}";
                article.PublishedDate = DateTime.UtcNow.AddDays(-7 * i);
            },
            imageSizes: Image(nameof(ArticlePage.HeroImage), 1200, 630));

        var productPage = SeedFactory.Page(
            type: typeof(ProductPage),
            count: 3,
            contentAreaAssignments:
            [
                new ContentAreaAssignment(nameof(ProductPage.TeaserArea), ["teaser1", "teaser2"])
            ],
            imageSizes: Image(nameof(ProductPage.ProductImage), 800, 600),
            children:
            [
                SeedFactory.Page(
                    type: typeof(ArticlePage),
                    count: 1,
                    configure: (page, i) => ((ArticlePage)page).Name = $"Care guide {i + 1}",
                    imageSizes: Image(nameof(ArticlePage.HeroImage), 1200, 630))
            ]);

        // 15 articles beneath the listing: at 6 per page that is three pages, so the pager is exercised.
        var articleListing = SeedFactory.Page(
            type: typeof(ArticleListingPage),
            count: 1,
            configure: (page, _) =>
            {
                var listing = (ArticleListingPage)page;
                listing.Name = "Articles";
                listing.PageSize = 6;
            },
            children:
            [
                SeedFactory.Page(
                    type: typeof(ArticlePage),
                    count: 15,
                    configure: (page, i) =>
                    {
                        var article = (ArticlePage)page;
                        article.Name = $"Journal article {i + 1}";
                        article.PublishedDate = DateTime.UtcNow.Date.AddDays(-30 * i);
                    },
                    imageSizes: Image(nameof(ArticlePage.HeroImage), 1200, 630)),
            ]);

        var startPage = SeedFactory.Page(
            type: typeof(StartPage),
            count: 1,
            contentAreaAssignments:
            [
                new ContentAreaAssignment(nameof(StartPage.HeroArea), ["hero"]),
                new ContentAreaAssignment(nameof(StartPage.PromoArea), ["promo1", "promo2", "promo3"]),
            ],
            configure: (page, _) => ((StartPage)page).Name = "Home",
            children: [articlePage, productPage, articleListing]);

        return SeedFactory.Tree(blocks, [startPage]);
    }

    private static Dictionary<string, (int Width, int Height)> Image(string property, int width, int height)
        => new() { [property] = (width, height) };
}

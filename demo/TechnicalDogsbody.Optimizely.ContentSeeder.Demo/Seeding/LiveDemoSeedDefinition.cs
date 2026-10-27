using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.Seeding;

public sealed class LiveDemoSeedDefinition : IAiSeedDefinition
{
    public string Id => "live-demo-v1";

    public AiWritingBrief WritingBrief { get; } = new AiWritingBrief
    {
        Prompt = "A short live demonstration of Content Seeder: a journal for an independent coffee roaster based in Manchester who thinks he's funny.",
        ToneOfVoice = "Mancunian colloquialisms, riddled with sarcasm",
        Audience = "Digital agency staff watching a demonstration of this content seeder tool.",
        AvoidPhrases = ["elevate", "unlock", "seamless"],
        MaxParagraphs = 3,
    }
    .ForContentType<ArticlePage>(t => t
        .Property(x => x.Title, "A specific, friendly headline under 60 characters."))
    .ForContentType<HeroSection>(t => t
        .Property(x => x.Heading, "A punchy headline.", maxWords: 5)
        .Property(x => x.Tagline, "One short supporting sentence.", maxWords: 14))
    .ForContentType<RichTextElementBlock>(t => t
        .Property(x => x.Body, "Two paragraphs of two sentences."))
    .ForContentType<ButtonElementBlock>(t => t
        .UseGenerator(x => x.ButtonText));

    public ContentGenerators? Generators { get; } = new ContentGenerators()
        .ForContentType<ButtonElementBlock>(t => t
            .Property(x => x.ButtonText, f => "Shop " + f.Commerce.ProductName()));

    public SeedTree Build()
    {
        var blocks = new List<BlockDefinition>
        {
            new("live-promo-1", typeof(PromoBlock), imageSizes: Image(nameof(PromoBlock.Image), 600, 400)),
            new("live-promo-2", typeof(PromoBlock), imageSizes: Image(nameof(PromoBlock.Image), 600, 400)),
            new("live-hero-block", typeof(HeroBlock), imageSizes: Image(nameof(HeroBlock.BackgroundImage), 1920, 800)),
        };

        var sections = new List<SectionDefinition>
        {
            new("live-hero", typeof(HeroSection), imageSizes: Image(nameof(HeroSection.BackgroundImage), 1920, 800)),

            new("live-single-block", typeof(BlankSection),
                rows:
                [
                    new RowDefinition(columns:
                    [
                        new ColumnDefinition(elements:
                        [
                            new ElementDefinition(typeof(ButtonElementBlock), name: "Call to action"),
                        ]),
                    ]),
                ]),

            new("live-repeating", typeof(BlankSection),
                rows:
                [
                    new RepeatingElementDefinition(typeof(RichTextElementBlock), name: "Text", minColumns: 1, maxColumns: 3, reuseContent: true),
                    new RepeatingElementDefinition(typeof(ImageElementBlock), name: "Image", minColumns: 1, maxColumns: 4, reuseContent: false,
                        imageSizes: Image(nameof(ImageElementBlock.Image), 600, 400)),
                ]),

            new("live-videos", typeof(BlankSection),
                rows:
                [
                    new RowDefinition(columns:
                    [
                        new ColumnDefinition(elements:
                        [
                            new ElementDefinition(typeof(VideoBlock), name: "Channel video",
                                videos: Videos(nameof(VideoBlock.VideoUrl),
                                    VideoSource.YouTube("@BlenderOfficial", titleProperty: nameof(VideoBlock.Caption)))),
                        ]),
                        new ColumnDefinition(elements:
                        [
                            new ElementDefinition(typeof(VideoBlock), name: "Embedded video",
                                videos: Videos(nameof(VideoBlock.VideoUrl),
                                    VideoSource.Urls("https://www.youtube.com/embed/aqz-KE-bpKQ"))),
                        ]),
                    ]),
                ]),
        };

        var listing = SeedFactory.Page(typeof(ArticleListingPage), count: 1,
            configure: (page, _) => ((ArticleListingPage)page).Name = "Journal",
            children: [
                SeedFactory.Page(typeof(ArticlePage),
                count: 2,
                imageSizes: Image(nameof(ArticlePage.HeroImage), 1200, 630))]);

        var experience = SeedFactory.Experience(typeof(LandingPageExperience), count: 1,
            configure: (page, _) => ((LandingPageExperience)page).Name = "Landing page",
            sections:
            [
                new SectionSlotAssignment("live-hero", name: "Hero"),
                new SectionSlotAssignment("live-single-block", name: "Single block"),
                new SectionSlotAssignment("live-repeating", name: "Repeating columns"),
                new SectionSlotAssignment("live-videos", name: "Videos"),
            ],
            imageSizes: Image(nameof(LandingPageExperience.HeroImage), 1600, 900));

        var home = SeedFactory.Page(typeof(StartPage), count: 1,
            configure: (page, _) => ((StartPage)page).Name = "Live Demo",
            contentAreaAssignments:
            [
                new ContentAreaAssignment(nameof(StartPage.PromoArea), ["live-promo-1", "live-promo-2"]),
                new ContentAreaAssignment(nameof(StartPage.HeroArea), ["live-hero-block"]),
            ],
            children: [listing, experience]);

        return SeedFactory.Tree(blocks: blocks, nodes: [home], sections: sections);
    }

    private static Dictionary<string, (int, int)> Image(string property, int width, int height)
        => new() { [property] = (width, height) };

    private static Dictionary<string, VideoSource> Videos(string property, VideoSource source)
        => new() { [property] = source };
}

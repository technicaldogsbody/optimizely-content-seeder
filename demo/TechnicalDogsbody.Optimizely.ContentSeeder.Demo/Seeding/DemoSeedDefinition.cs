using EPiServer.Core;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Demo.ContentTypes;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.Seeding;

/// <summary>
/// The demo site's seed definition. It is deliberately thorough: every content type in the demo is seeded, and each of the
/// seeder's features is used in more than one way, including the awkward cases (empty layouts, the same section twice on one
/// page, a video feed that doesn't exist…). Comments marked "Edge case" say what each one is there to exercise.
/// </summary>
public sealed class DemoSeedDefinition : IAiSeedDefinition
{
    public string Id => "demo-seed-v1";

    // Languages set on the definition override ContentSeeder:MasterLanguage and Languages in appsettings.json for this seed
    // only (LiveDemoSeedDefinition has none, so it still uses appsettings). To seed other languages here, change this list, or
    // delete both properties to go back to appsettings. Every language needs to be enabled in the CMS.
    public string? MasterLanguage => "en";

    public IReadOnlyList<string>? Languages => ["ar-AE"];

    // Bogus generator rules: the same content as WritingBrief, expressed as code. Each rule sits beside the brief's
    // requirement for that property, so the two can be compared directly. Rules run when Bogus generates a value, and
    // under an AI provider they still set the non-text values and the placeholder kept if an AI request fails.
    // Unlike the brief, a rule cannot enforce a word or character limit: it shapes length by construction instead.
    public ContentGenerators Generators { get; } = new ContentGenerators()
        // ── pages ──
        // The listing's title is a fixed value per language. It is a generator rule, not configure: configure runs for every
        // language, so a title set there would be copied into the Chinese and Arabic versions as English.
        .ForContentType<ArticleListingPage>(t => t
            .Property(x => x.Title, f => "The Trailhead journal")
            .Property(x => x.Title, "zh", f => "步道起點 日志")
            .Property(x => x.Title, "ar-AE", f => "مجلة نقطة الانطلاق"))
        .ForContentType<ArticlePage>(t => t
            // Brief: "A specific headline.", max 70 characters. A short, title-like sentence stays well under that.
            .Property(x => x.Title, f => f.Lorem.Sentence(4, 2).TrimEnd('.'))
            // Brief: "A realistic full name."
            .Property(x => x.Author, f => f.Name.FullName())
            // Bogus only: some words get inline tags. The AI still writes the body.
            .RichText(x => x.Body, o => o.Tags("strong", "em", "a").Probability(0.1).Paragraphs(8)))
        .ForContentType<ProductPage>(t => t
            // Brief: "A plausible product name, e.g. a boot or tent model."
            .Property(x => x.ProductName, f => f.Commerce.ProductAdjective() + " " + f.Commerce.Product())
            // Brief: "Two or three sentences about what makes it good for walking."
            .Property(x => x.Description, f => f.Commerce.ProductDescription()))
        // ── named set "kitchenSink" ──
        // Used by one article and one element, so they show every common rich text tag in a fixed order, for checking how a
        // front end renders them. The title is fixed per language, and the AI leaves a KitchenSink body alone (see WritingBrief).
        .ForContentType<ArticlePage>("kitchenSink", t => t
            .Property(x => x.Title, f => "Rich text kitchen sink")
            .Property(x => x.Title, "ar-AE", f => "كل عناصر النص المنسق")
            .KitchenSink(x => x.Body))
        .ForContentType<RichTextElementBlock>("kitchenSink", t => t.KitchenSink(x => x.Body))
        .ForContentType<LandingPageExperience>(t => t
            // Brief: "A punchy headline.", max 6 words. Language-neutral rule, then one rule per language. A language
            // without its own rule (sv, de…) uses the neutral one.
            .Property(x => x.Heading, f => "Gear for " + f.PickRandom("day hikes", "long trails", "wild camping", "winter walks"))
            .Property(x => x.Heading, "en", f => "Gear for " + f.PickRandom("day hikes", "long trails", "wild camping", "winter walks"))
            .Property(x => x.Heading, "zh", f => "适合" + f.PickRandom("日间徒步", "长途穿越", "野外露营", "冬季健行") + "的装备")
            .Property(x => x.Heading, "ar-AE", f => "معدات لـ" + f.PickRandom("المشي لمسافات قصيرة", "المسارات الطويلة", "التخييم البري", "المشي الشتوي"))
            // Brief: "One short sentence.", max 12 words.
            .Property(x => x.Tagline, f => f.Lorem.Sentence(10).TrimEnd('.'))
            // Brief: "A search-result summary.", max 155 characters.
            .Property(x => x.MetaDescription, f => f.Lorem.Sentence(15, 3).TrimEnd('.')))
        .ForContentType<CampaignPageExperience>(t => t
            // Brief: "A short campaign name.", max 4 words.
            .Property(x => x.CampaignName, f => f.PickRandom("Summer Trail Sale", "Winter Warmers", "Spring Fresh Start", "Autumn Layer Up"))
            .Property(x => x.CampaignName, "zh", f => f.PickRandom("夏季步道特卖", "冬季暖心", "春季焕新", "秋季叠穿"))
            .Property(x => x.CampaignName, "ar-AE", f => f.PickRandom("تخفيضات الصيف على المسارات", "دفء الشتاء", "بداية الربيع المنعشة", "طبقات الخريف"))
            // Brief: "One short sentence.", max 10 words.
            .Property(x => x.Tagline, f => f.Lorem.Sentence(8).TrimEnd('.'))
            // Brief: "A search-result summary.", max 155 characters.
            .Property(x => x.MetaDescription, f => f.Lorem.Sentence(15, 3).TrimEnd('.')))
        // ── shared blocks ──
        .ForContentType<HeroBlock>(t => t
            // Brief: "A punchy headline.", max 6 words.
            .Property(x => x.Heading, f => f.Commerce.ProductAdjective() + " " + f.Commerce.Product())
            // Brief: "One short sentence.", max 14 words.
            .Property(x => x.SubHeading, f => f.Lorem.Sentence(10).TrimEnd('.'))
            // Brief: "Two or three words.", max 3 words. Imperative button label, picked from a fixed list.
            .Property(x => x.CtaText, f => f.PickRandom("Shop now", "Learn more", "Get started", "Explore", "Find out more"))
            .Property(x => x.CtaText, "zh", f => f.PickRandom("立即选购", "了解更多", "开始使用", "探索", "查看详情"))
            .Property(x => x.CtaText, "ar-AE", f => f.PickRandom("تسوّق الآن", "اعرف المزيد", "ابدأ الآن", "استكشف", "اكتشف المزيد")))
        .ForContentType<PromoBlock>(t => t
            // Brief: "A short offer headline.", max 6 words. A product or category name suits a promo card.
            .Property(x => x.Title, f => f.Commerce.Department()))
        .ForContentType<TeaserBlock>(t => t
            // Brief: "A short headline.", max 6 words. Article-style headline.
            .Property(x => x.Heading, f => f.Lorem.Sentence(5, 3).TrimEnd('.'))
            // Brief: "One sentence."
            .Property(x => x.Summary, f => f.Lorem.Sentence(14).TrimEnd('.')))
        .ForContentType<VideoBlock>(t => t
            // Brief: "A short, descriptive caption.", max 10 words.
            .Property(x => x.Caption, f => f.Lorem.Sentence(6).TrimEnd('.')))
        // ── Visual Builder sections ──
        .ForContentType<HeroSection>(t => t
            // Brief: "A punchy headline.", max 5 words.
            .Property(x => x.Heading, f => f.PickRandom("Built for the long trail", "Light enough to forget", "Ready for the weather"))
            .Property(x => x.Heading, "zh", f => f.PickRandom("为长途穿越而生", "轻到让你忘记它", "随时迎接天气变化"))
            .Property(x => x.Heading, "ar-AE", f => f.PickRandom("صُنعت لمسارات الرحلات الطويلة", "خفيفة لدرجة تنسيها", "جاهزة لكل الأجواء"))
            // Brief: "Two or three words.", max 3 words.
            .Property(x => x.CtaButtonText, f => f.PickRandom("Shop boots", "See the range", "Find your pack"))
            .Property(x => x.CtaButtonText, "zh", f => f.PickRandom("选购徒步靴", "浏览全部系列", "找到你的背包"))
            .Property(x => x.CtaButtonText, "ar-AE", f => f.PickRandom("تسوّق الأحذية", "شاهد المجموعة", "اعثر على حقيبتك")))
        .ForContentType<CTASection>(t => t
            // Brief: "A call to action.", max 6 words.
            .Property(x => x.Heading, f => f.PickRandom("Find your next walk", "Get kitted out", "Need help choosing?"))
            .Property(x => x.Heading, "zh", f => f.PickRandom("找到你的下一段徒步", "全套装备一站配齐", "需要选购建议吗？"))
            .Property(x => x.Heading, "ar-AE", f => f.PickRandom("اعثر على رحلتك القادمة", "جهّز نفسك بالكامل", "تحتاج مساعدة في الاختيار؟"))
            // Brief: "One sentence."
            .Property(x => x.Description, f => f.Lorem.Sentence(12).TrimEnd('.'))
            // Brief: "Two or three words.", max 3 words.
            .Property(x => x.ButtonText, f => f.PickRandom("Shop now", "Explore", "Book a fitting"))
            .Property(x => x.ButtonText, "zh", f => f.PickRandom("立即选购", "探索", "预约试穿"))
            .Property(x => x.ButtonText, "ar-AE", f => f.PickRandom("تسوّق الآن", "استكشف", "احجز تجربة مقاس")))
        .ForContentType<FeatureCardsSection>(t => t
            // Brief: "A short section heading.", max 6 words.
            .Property(x => x.Heading, f => f.PickRandom("Why walkers choose us", "What's in the box", "Made to last"))
            .Property(x => x.Heading, "zh", f => f.PickRandom("徒步者为何选择我们", "盒子里有什么", "经久耐用"))
            .Property(x => x.Heading, "ar-AE", f => f.PickRandom("لماذا يختارنا المتنزهون", "ما الذي في الصندوق", "صُنعت لتدوم"))
            // Brief: "One sentence."
            .Property(x => x.Description, f => f.Lorem.Sentence(14).TrimEnd('.')))
        // ── Visual Builder elements ──
        .ForContentType<RichTextElementBlock>(t => t
            // Brief: "One short paragraph of two or three sentences." A string here is wrapped as rich text by the seeder.
            .Property(x => x.Body, f => f.Lorem.Paragraph(4)))
        .ForContentType<ButtonElementBlock>(t => t
            // Brief: "Two or three words.", max 3 words.
            .Property(x => x.ButtonText, f => f.PickRandom("Shop now", "Learn more", "Find out more"))
            .Property(x => x.ButtonText, "zh", f => f.PickRandom("立即选购", "了解更多", "查看详情"))
            .Property(x => x.ButtonText, "ar-AE", f => f.PickRandom("تسوّق الآن", "اعرف المزيد", "اكتشف المزيد")))
        .ForContentType<StatisticElementBlock>(t => t
            // Brief: "Just the figure, e.g. 98% or 1,200km."
            .Property(x => x.Value, f => f.PickRandom(
                $"{f.Random.Int(1, 99)}%", $"{f.Random.Int(100, 2000):N0}km", f.Random.Int(1, 500).ToString("N0")))
            .Property(x => x.Value, "zh", f => f.PickRandom(
                $"{f.Random.Int(1, 99)}%", $"{f.Random.Int(100, 2000):N0}公里", f.Random.Int(1, 500).ToString("N0")))
            .Property(x => x.Value, "ar-AE", f => f.PickRandom(
                $"{f.Random.Int(1, 99)}%", $"{f.Random.Int(100, 2000):N0} كم", f.Random.Int(1, 500).ToString("N0")))
            // Brief: "A short label.", max 8 words.
            .Property(x => x.Label, f => f.PickRandom("Miles of trails", "Happy walkers", "Returns processed", "Years on the trail"))
            .Property(x => x.Label, "zh", f => f.PickRandom("步道里程", "快乐的徒步者", "已处理退货", "在路上的年份"))
            .Property(x => x.Label, "ar-AE", f => f.PickRandom("أميال من المسارات", "متنزهون سعداء", "مرتجعات تمت معالجتها", "سنوات على المسار")))
        .ForContentType<TestimonialElementBlock>(t => t
            // Brief: "One or two sentences, a believable customer quote, without quotation marks."
            .Property(x => x.Quote, f => f.Lorem.Sentence(16).TrimEnd('.'))
            // Brief: "A realistic full name."
            .Property(x => x.AuthorName, f => f.Name.FullName())
            // Brief: "A short descriptor such as 'Weekend walker'.", max 4 words.
            .Property(x => x.AuthorTitle, f => f.PickRandom("Weekend walker", "Trail runner", "Mountain guide", "Scout leader"))
            .Property(x => x.AuthorTitle, "zh", f => f.PickRandom("周末徒步者", "越野跑者", "登山向导", "童军领队"))
            .Property(x => x.AuthorTitle, "ar-AE", f => f.PickRandom("متنزه عطلة نهاية الأسبوع", "عداء مسارات", "مرشد جبال", "قائد كشافة")))
        .ForContentType<ImageElementBlock>(t => t
            // Brief: "What the picture shows.", max 12 words.
            .Property(x => x.AltText, f => f.Lorem.Sentence(8).TrimEnd('.'))
            // Brief: "A short caption.", max 10 words.
            .Property(x => x.Caption, f => f.Lorem.Sentence(6).TrimEnd('.')));

    // Only used when an AI provider is configured (see Startup); the default Bogus provider ignores it.
    public AiWritingBrief WritingBrief { get; } = new AiWritingBrief
    {
        Prompt = "Trailhead & Co., a fictional UK shop selling outdoor and hiking gear: boots, packs, tents and waterproofs, "
               + "plus guides to walking routes and kit care.",
        ToneOfVoice = "Friendly, practical and quietly enthusiastic, like an experienced walker giving advice. Plain English.",
        Audience = "Day hikers and weekend backpackers, from beginners to seasoned walkers.",
        Instructions = "British spelling. Sentence case for headings. No exclamation marks.",
        AvoidPhrases = ["elevate", "unlock", "seamless", "game-changer"],
        MaxParagraphs = 3,
    }
    // ── pages ──
    // The title comes from its generator rule (a fixed value per language), so the AI does not write it.
    .ForContentType<ArticleListingPage>(t => t.UseGenerator(x => x.Title))
    .ForContentType<ArticlePage>(t => t
        .Prompt("Practical guides and route write-ups.")
        .Property(x => x.Title, "A specific headline.", maxCharacters: 70)
        .Property(x => x.Author, "A realistic full name.")
        // Rich text may use simple HTML: the AI is allowed paragraphs, headings, lists, bold, italics and links. A string
        // property never gets markup, which is why only the body is asked for it.
        .Property(x => x.Body, "Three to four paragraphs. Include one <h2> subheading, a short <ul> list, and <strong> on a key phrase."))
    // The "kitchenSink" set: its title comes from its generator rule and its body is a KitchenSink, so the AI writes neither.
    .ForContentType<ArticlePage>("kitchenSink", t => t.UseGenerator(x => x.Title))
    .ForContentType<ProductPage>(t => t
        .Property(x => x.ProductName, "A plausible product name, e.g. a boot or tent model.")
        .Property(x => x.Description, "Two or three sentences about what makes it good for walking."))
    .ForContentType<LandingPageExperience>(t => t
        .Property(x => x.Heading, "A punchy headline.", maxWords: 6)
        .Property(x => x.Tagline, "One short sentence.", maxWords: 12)
        .Property(x => x.MetaDescription, "A search-result summary.", maxCharacters: 155))
    .ForContentType<CampaignPageExperience>(t => t
        .Prompt("Seasonal sales and product launches.")
        .Property(x => x.CampaignName, "A short campaign name.", maxWords: 4)
        .Property(x => x.Tagline, "One short sentence.", maxWords: 10)
        .Property(x => x.MetaDescription, "A search-result summary.", maxCharacters: 155))
    // ── shared blocks ──
    .ForContentType<HeroBlock>(t => t
        .Property(x => x.Heading, "A punchy headline.", maxWords: 6)
        .Property(x => x.SubHeading, "One short sentence.", maxWords: 14)
        .Property(x => x.CtaText, "Two or three words.", maxWords: 3))
    .ForContentType<PromoBlock>(t => t
        .Property(x => x.Title, "A short offer headline.", maxWords: 6))
    .ForContentType<TeaserBlock>(t => t
        .Property(x => x.Heading, "A short headline.", maxWords: 6)
        .Property(x => x.Summary, "One sentence."))
    .ForContentType<VideoBlock>(t => t
        // Used when the video has no title of its own (the uploaded sample, or a list of URLs).
        .Property(x => x.Caption, "A short, descriptive caption.", maxWords: 10))
    // ── Visual Builder sections ──
    .ForContentType<HeroSection>(t => t
        .Property(x => x.Heading, "A punchy headline.", maxWords: 5)
        .Property(x => x.CtaButtonText, "Two or three words.", maxWords: 3))
    .ForContentType<CTASection>(t => t
        .Property(x => x.Heading, "A call to action.", maxWords: 6)
        .Property(x => x.Description, "One sentence.")
        .Property(x => x.ButtonText, "Two or three words.", maxWords: 3))
    .ForContentType<FeatureCardsSection>(t => t
        .Property(x => x.Heading, "A short section heading.", maxWords: 6)
        .Property(x => x.Description, "One sentence."))
    // ── Visual Builder elements (repeated dozens of times per Experience page, so keep each one short) ──
    .ForContentType<RichTextElementBlock>(t => t
        .Property(x => x.Body, "One short paragraph of two or three sentences."))
    .ForContentType<ButtonElementBlock>(t => t
        .Property(x => x.ButtonText, "Two or three words.", maxWords: 3))
    .ForContentType<StatisticElementBlock>(t => t
        .Property(x => x.Value, "Just the figure, e.g. 98% or 1,200km.")
        .Property(x => x.Label, "A short label.", maxWords: 8))
    .ForContentType<TestimonialElementBlock>(t => t
        .Property(x => x.Quote, "One or two sentences, a believable customer quote, without quotation marks.")
        .Property(x => x.AuthorName, "A realistic full name.")
        .Property(x => x.AuthorTitle, "A short descriptor such as 'Weekend walker'.", maxWords: 4))
    .ForContentType<ImageElementBlock>(t => t
        .Property(x => x.AltText, "What the picture shows.", maxWords: 12)
        .Property(x => x.Caption, "A short caption.", maxWords: 10));

    public SeedTree Build()
    {
        // ════════════════════════════════════════════════════════════════════
        //  Shared blocks (ContentArea content)
        // ════════════════════════════════════════════════════════════════════
        var blocks = new List<BlockDefinition>
        {
            new("hero", typeof(HeroBlock), imageSizes: Image(nameof(HeroBlock.BackgroundImage), 1920, 600)),

            new("promo1", typeof(PromoBlock), imageSizes: Image(nameof(PromoBlock.Image), 600, 400)),
            new("promo2", typeof(PromoBlock), imageSizes: Image(nameof(PromoBlock.Image), 600, 400)),
            new("promo3", typeof(PromoBlock), imageSizes: Image(nameof(PromoBlock.Image), 600, 400)),
            // Edge case: no imageSizes, so the image property is left empty and the view must cope.
            new("promo-no-image", typeof(PromoBlock)),

            new("teaser1", typeof(TeaserBlock), imageSizes: Image(nameof(TeaserBlock.ThumbnailImage), 400, 300)),
            new("teaser2", typeof(TeaserBlock), imageSizes: Image(nameof(TeaserBlock.ThumbnailImage), 400, 300)),

            // ── Videos as plain blocks, one for each way of choosing one ──
            // A random recent video from a YouTube channel, with its real title as the caption.
            new("video-youtube", typeof(VideoBlock),
                videos: Videos(nameof(VideoBlock.VideoUrl),
                    VideoSource.YouTube("UCSMOQeBJ2RAnuFungnQOxLg", titleProperty: nameof(VideoBlock.Caption)))),
            // The same channel by @handle (looked up from the channel page).
            new("video-youtube-handle", typeof(VideoBlock),
                videos: Videos(nameof(VideoBlock.VideoUrl),
                    VideoSource.YouTube("@BlenderOfficial", titleProperty: nameof(VideoBlock.Caption)))),
            // A playlist, with a fallback in case it can't be read.
            new("video-youtube-playlist", typeof(VideoBlock),
                videos: Videos(nameof(VideoBlock.VideoUrl),
                    VideoSource.YouTubePlaylist("PLa1F2ddGya_-UvuAqHAksYnB0qL9yWDO6", titleProperty: nameof(VideoBlock.Caption),
                        fallback: ["https://youtu.be/aqz-KE-bpKQ"]))),
            // A Vimeo channel (vimeo.com/channels/staffpicks).
            new("video-vimeo-channel", typeof(VideoBlock),
                videos: Videos(nameof(VideoBlock.VideoUrl),
                    VideoSource.Vimeo("staffpicks", titleProperty: nameof(VideoBlock.Caption)))),
            // Edge case: a fixed list of URLs in different forms (short, embed). No title comes with these, so the
            // caption is generated (Bogus or AI) instead.
            new("video-url-list", typeof(VideoBlock),
                videos: Videos(nameof(VideoBlock.VideoUrl),
                    VideoSource.Urls("https://youtu.be/aqz-KE-bpKQ", "https://player.vimeo.com/video/265010648"))),
            // Edge case: a channel that doesn't exist. The feed 404s, one warning is logged, and the fallback URL is used.
            new("video-bad-feed-fallback", typeof(VideoBlock),
                videos: Videos(nameof(VideoBlock.VideoUrl),
                    VideoSource.YouTube("UC0000000000000000000000", fallback: ["https://youtu.be/aqz-KE-bpKQ"]))),
            // The uploaded sample, as a media asset rather than a URL.
            new("video-uploaded", typeof(VideoBlock),
                videos: Videos(nameof(VideoBlock.Video), VideoSource.Uploaded())),
            // Edge case: both at once. The uploaded file wins in the view; the URL is still stored.
            new("video-uploaded-and-url", typeof(VideoBlock),
                videos: new Dictionary<string, VideoSource>
                {
                    [nameof(VideoBlock.Video)] = VideoSource.Uploaded(),
                    [nameof(VideoBlock.VideoUrl)] = VideoSource.Vimeo("staffpicks", titleProperty: nameof(VideoBlock.Caption)),
                }),
        };

        // ════════════════════════════════════════════════════════════════════
        //  Visual Builder sections
        // ════════════════════════════════════════════════════════════════════
        var sections = new List<SectionDefinition>
        {
            // ── Structural: a SectionData with its own rows, columns and elements, built inline on every page ──
            new("blank-section", typeof(BlankSection),
                rows:
                [
                    new RowDefinition(columns:
                    [
                        new ColumnDefinition(elements:
                        [
                            new ElementDefinition(typeof(RichTextElementBlock), name: "Body Text"),
                            new ElementDefinition(typeof(ButtonElementBlock), name: "CTA Button"),
                            // An uploaded video: the sample clip is downloaded once and saved as a media asset.
                            new ElementDefinition(typeof(VideoBlock), name: "Video",
                                videos: Videos(nameof(VideoBlock.Video), VideoSource.Uploaded()))
                        ])
                    ]),
                    // One row per column count from 1 to 6: rows of 1, 2, 3, 4, 5 and 6 rich-text columns. reuseContent generates the text
                    // once and copies it, so AI is asked for one element rather than twenty-one.
                    new RepeatingElementDefinition(typeof(RichTextElementBlock), name: "Repeated Text", minColumns: 1, maxColumns: 6, reuseContent: true),
                    new RepeatingElementDefinition(typeof(ButtonElementBlock), name: "Button", minColumns: 2, maxColumns: 5, reuseContent: false),
                    // Rows of 1, 2 and 3 videos (six in all). Without reuseContent each element picks its own video from the
                    // channel, and picks do not repeat until the feed has been used up.
                    new RepeatingElementDefinition(typeof(VideoBlock), name: "Repeated Video", minColumns: 1, maxColumns: 3,
                        videos: Videos(nameof(VideoBlock.VideoUrl),
                            VideoSource.YouTube("UCSMOQeBJ2RAnuFungnQOxLg", titleProperty: nameof(VideoBlock.Caption))))
                ]),

            // Every element type in one section, with Visual Builder Styles, so a single page shows them all.
            new("kitchen-sink-section", typeof(BlankSection),
                rows:
                [
                    // A single wide column; the style settings are the ones registered for rich text.
                    new RowDefinition(name: "Intro", columns:
                    [
                        new ColumnDefinition(name: "Intro column", elements:
                        [
                            new ElementDefinition(typeof(RichTextElementBlock), name: "Intro text",
                                displayTemplateKey: "richTextDefault",
                                displaySettings: Style(("fontSize", "large"), ("alignment", "center")))
                        ])
                    ]),

                    // Three statistics, one per column, each styled differently.
                    new RowDefinition(name: "Key figures", columns:
                    [
                        new ColumnDefinition(elements: [Stat("Distance", "primary", "large")]),
                        new ColumnDefinition(elements: [Stat("Satisfaction", "success", "large")]),
                        new ColumnDefinition(elements: [Stat("Returns", "danger", "small")]),
                    ]),

                    // Two different element types side by side, both with images.
                    new RowDefinition(name: "Photo and quote", columns:
                    [
                        new ColumnDefinition(elements:
                        [
                            new ElementDefinition(typeof(ImageElementBlock), name: "Photo",
                                imageSizes: Image(nameof(ImageElementBlock.Image), 800, 600),
                                displayTemplateKey: "imageDefault",
                                displaySettings: Style(("borderRadius", "medium"), ("shadow", "large")))
                        ]),
                        new ColumnDefinition(elements:
                        [
                            new ElementDefinition(typeof(TestimonialElementBlock), name: "Quote",
                                imageSizes: Image(nameof(TestimonialElementBlock.AuthorPhoto), 200, 200),
                                displayTemplateKey: "testimonialDefault",
                                displaySettings: Style(("layout", "withPhoto"), ("rating", "true")))
                        ]),
                    ]),

                    // Edge case: several elements stacked in one column, one of them unnamed, with every button variant
                    // and a configure callback.
                    new RowDefinition(name: "Stacked buttons", columns:
                    [
                        new ColumnDefinition(name: "Buttons", elements:
                        [
                            new ElementDefinition(typeof(ButtonElementBlock), name: "Primary",
                                configure: (button, _) => ((ButtonElementBlock)button).OpenInNewTab = true,
                                displayTemplateKey: "buttonDefault",
                                displaySettings: Style(("variant", "primary"), ("size", "large"))),
                            new ElementDefinition(typeof(ButtonElementBlock),
                                displayTemplateKey: "buttonDefault",
                                displaySettings: Style(("variant", "secondary"), ("size", "medium"))),
                            new ElementDefinition(typeof(ButtonElementBlock), name: "Danger",
                                displayTemplateKey: "buttonDefault",
                                displaySettings: Style(("variant", "danger"), ("size", "small"))),
                        ])
                    ]),

                    // Videos: the uploaded file with autoplay, and a pick from a URL list in a square frame.
                    new RowDefinition(name: "Videos", columns:
                    [
                        new ColumnDefinition(elements:
                        [
                            new ElementDefinition(typeof(VideoBlock), name: "Uploaded video",
                                videos: Videos(nameof(VideoBlock.Video), VideoSource.Uploaded()),
                                displayTemplateKey: "videoDefault",
                                displaySettings: Style(("aspectRatio", "standard"), ("autoplay", "true")))
                        ]),
                        new ColumnDefinition(elements:
                        [
                            new ElementDefinition(typeof(VideoBlock), name: "Linked video",
                                videos: Videos(nameof(VideoBlock.VideoUrl),
                                    VideoSource.Urls("https://youtu.be/aqz-KE-bpKQ", "https://vimeo.com/265010648")),
                                displayTemplateKey: "videoDefault",
                                displaySettings: Style(("aspectRatio", "square")))
                        ]),
                    ]),

                    // The same rich text tags as the kitchen-sink article, as an element. Its named set is "kitchenSink", so the
                    // Arabic version of this element is translated with those rules too.
                    new RowDefinition(name: "Every rich text tag", columns:
                    [
                        new ColumnDefinition(elements:
                        [
                            new ElementDefinition(typeof(RichTextElementBlock), name: "Every rich text tag", instance: "kitchenSink")
                        ])
                    ]),

                    // Edge case: a row whose only column is empty.
                    new RowDefinition(name: "Empty column", columns: [new ColumnDefinition(name: "Nothing here")]),

                    // Repeating elements of other types. A statistic row from 1 to 3 columns (each different), a single row
                    // of two testimonials that share one generated quote (min == max), and one-to-two images.
                    new RepeatingElementDefinition(typeof(StatisticElementBlock), name: "Figure", minColumns: 1, maxColumns: 3,
                        displayTemplateKey: "statDefault", displaySettings: Style(("numberColor", "success"), ("size", "large"))),
                    new RepeatingElementDefinition(typeof(TestimonialElementBlock), name: "Review", minColumns: 2, maxColumns: 4,
                        reuseContent: true, imageSizes: Image(nameof(TestimonialElementBlock.AuthorPhoto), 200, 200)),
                    new RepeatingElementDefinition(typeof(ImageElementBlock), name: "Gallery", minColumns: 1, maxColumns: 7,
                        imageSizes: Image(nameof(ImageElementBlock.Image), 600, 400)),
                ]),

            // Edge case: a structural section with no rows at all.
            new("empty-section", typeof(BlankSection)),

            // ── Leaf: a BlockData used directly as a section. Created once and shared by every page that places it ──
            new("hero-leaf-section", typeof(HeroSection), imageSizes: Image(nameof(HeroSection.BackgroundImage), 1920, 800)),

            new("cta-section", typeof(CTASection),
                // Edge case: configure sets a bool and a colour string that the generators would otherwise guess.
                configure: (section, _) =>
                {
                    var cta = (CTASection)section;
                    cta.ButtonUrl = "/";
                    cta.ButtonOpenInNewTab = true;
                    cta.BackgroundColor = "#0b5fff";
                }),

            // Edge case: a nullable int property with a default.
            new("features-section", typeof(FeatureCardsSection),
                configure: (section, _) => ((FeatureCardsSection)section).ColumnCount = 4),

            // Edge case: a type that is both an element and a section, used here as a section.
            new("testimonial-section", typeof(TestimonialElementBlock),
                imageSizes: Image(nameof(TestimonialElementBlock.AuthorPhoto), 200, 200)),

            // A video as a Visual Builder section: a random video from Blender's Vimeo page (vimeo.com/yourown3dsoftware).
            new("video-section", typeof(VideoBlock),
                videos: Videos(nameof(VideoBlock.VideoUrl),
                    VideoSource.VimeoUser("yourown3dsoftware", titleProperty: nameof(VideoBlock.Caption)))),
        };

        // ════════════════════════════════════════════════════════════════════
        //  Pages
        // ════════════════════════════════════════════════════════════════════
        var articlePage = SeedFactory.Page(
            type: typeof(ArticlePage),
            count: 3,
            // The instance index makes each article distinct without relying on generated text.
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
            // The teasers, and one video block that is also shown on the start page: the same shared block in several places.
            contentAreaAssignments:
            [
                new ContentAreaAssignment(nameof(ProductPage.TeaserArea), ["teaser1", "teaser2", "video-uploaded"])
            ],
            imageSizes: Image(nameof(ProductPage.ProductImage), 800, 600),
            // Edge case: a third level of nesting (start page > product > article), created under each product.
            children:
            [
                SeedFactory.Page(
                    type: typeof(ArticlePage),
                    count: 1,
                    configure: (page, i) => ((ArticlePage)page).Name = $"Care guide {i + 1}",
                    imageSizes: Image(nameof(ArticlePage.HeroImage), 1200, 630))
            ]);

        // An article listing with 12 articles beneath it: at 6 per page that is two pages, so the pager and its
        // ?page= links are exercised. Articles are seeded as the listing's children, so the listing finds them.
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
                        // Spread over the last year, so the newest-first order is visible in the listing.
                        article.PublishedDate = DateTime.UtcNow.Date.AddDays(-30 * i);
                    },
                    imageSizes: Image(nameof(ArticlePage.HeroImage), 1200, 630)),
            ]);

        // One article that uses the "kitchenSink" set (see Generators and WritingBrief). It is the same content type as the others,
        // so the difference is carried by the named set alone.
        var kitchenSinkArticle = SeedFactory.Page(
            type: typeof(ArticlePage),
            count: 1,
            configure: (page, _) => ((ArticlePage)page).Name = "Kitchen sink",
            instance: "kitchenSink");

        // Experience: a hero, a video and the big structural section.
        var landingPageExperience = SeedFactory.Experience(
            type: typeof(LandingPageExperience),
            count: 2,
            sections:
            [
                new SectionSlotAssignment("hero-leaf-section", name: "Hero"),
                new SectionSlotAssignment("video-section", name: "Video"),
                new SectionSlotAssignment("blank-section", name: "Content")
            ],
            imageSizes: Image(nameof(LandingPageExperience.HeroImage), 1600, 900));

        // Experience made only of leaf sections, each with Visual Builder Styles on its slot, plus an empty section.
        var landingLeafSections = SeedFactory.Experience(
            type: typeof(LandingPageExperience),
            count: 1,
            configure: (page, _) => ((PageData)page).Name = "Landing - leaf sections only",
            sections:
            [
                new SectionSlotAssignment("hero-leaf-section", name: "Hero",
                    displayTemplateKey: "heroDefault", displaySettings: Style(("overlay", "dark"), ("height", "large"))),
                new SectionSlotAssignment("features-section", name: "Features",
                    displayTemplateKey: "featureDefault", displaySettings: Style(("cardStyle", "bordered"), ("textAlign", "center"))),
                new SectionSlotAssignment("testimonial-section", name: "Testimonial"),
                new SectionSlotAssignment("empty-section", name: "Nothing to see"),
                new SectionSlotAssignment("cta-section", name: "Call to action",
                    displayTemplateKey: "ctaDefault", displaySettings: Style(("bgStyle", "gradient"), ("textColor", "light"))),
            ],
            imageSizes: Image(nameof(LandingPageExperience.HeroImage), 1600, 900));

        // The other Experience type. Edge case: the same sections placed twice on one page (the CTA twice, shared; the
        // kitchen sink once, built inline), in a different order from the landing pages.
        var campaignPageExperience = SeedFactory.Experience(
            type: typeof(CampaignPageExperience),
            count: 1,
            configure: (page, i) =>
            {
                var campaign = (CampaignPageExperience)page;
                campaign.Name = $"Campaign {i + 1}";
                campaign.StartDate = DateTime.UtcNow.Date.AddDays(30 * i);
                campaign.EndDate = DateTime.UtcNow.Date.AddDays(30 * i + 21);
            },
            sections:
            [
                new SectionSlotAssignment("cta-section", name: "Top call to action"),
                new SectionSlotAssignment("kitchen-sink-section", name: "Everything"),
                new SectionSlotAssignment("features-section", name: "Features"),
                new SectionSlotAssignment("cta-section", name: "Bottom call to action"),
            ],
            imageSizes: Image(nameof(CampaignPageExperience.HeroImage), 1600, 600));

        var startPage = SeedFactory.Page(
            type: typeof(StartPage),
            count: 1,
            contentAreaAssignments:
            [
                new ContentAreaAssignment(nameof(StartPage.HeroArea), ["hero"]),
                new ContentAreaAssignment(nameof(StartPage.PromoArea),
                [
                    "promo1", "promo2", "promo3", "promo-no-image",
                    "video-youtube", "video-youtube-handle", "video-youtube-playlist", "video-vimeo-channel",
                    "video-url-list", "video-bad-feed-fallback", "video-uploaded", "video-uploaded-and-url",
                ])
            ],
            configure: (page, _) => ((StartPage)page).Name = "Home",
            children:
            [
                articlePage, productPage, articleListing, kitchenSinkArticle,
                landingPageExperience, landingLeafSections,
                campaignPageExperience,
            ]);

        return SeedFactory.Tree(blocks, [startPage], sections);
    }

    // ── small helpers so the tree above stays readable ───────────────────────────────────────────────────────────────

    private static Dictionary<string, (int, int)> Image(string property, int width, int height)
        => new() { [property] = (width, height) };

    private static Dictionary<string, VideoSource> Videos(string property, VideoSource source)
        => new() { [property] = source };

    private static Dictionary<string, string> Style(params (string Key, string Value)[] settings)
        => settings.ToDictionary(s => s.Key, s => s.Value);

    private static ElementDefinition Stat(string name, string numberColor, string size)
        => new(typeof(StatisticElementBlock), name: name,
            displayTemplateKey: "statDefault",
            displaySettings: Style(("numberColor", numberColor), ("size", size)));
}

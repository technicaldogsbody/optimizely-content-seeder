---
name: seed-definition
description: Create or update an ISeedDefinition or IAiSeedDefinition for TechnicalDogsbody.Optimizely.ContentSeeder - the class that declares which pages, blocks, Visual Builder sections and videos to seed into an Optimizely CMS site, and (for AI content) a typed writing brief. Use when asked to create, add to, change, extend or fix a seed definition, seed demo content, add videos or content types to existing seed data, or configure AI-written seed content.
---

Write a seed definition for TechnicalDogsbody.Optimizely.ContentSeeder: the class that says which pages, blocks and Visual Builder sections to create in an Optimizely CMS site, and, for AI content, how the text should be written.

## When to use this

Use it when asked to create, update, extend or fix a class implementing `ISeedDefinition` or `IAiSeedDefinition`, or to seed demo content for an Optimizely CMS site with the ContentSeeder package. Do not hand-write content-creation code (`IContentRepository.Save` loops); the seeder already does that, plus languages, resume, tracking and cleanup.

**First, look for an existing definition** (search the solution for `ISeedDefinition` / `IAiSeedDefinition`). If there is one, you are updating it: follow "Updating an existing definition" below instead of starting from a template.

## Workflow

1. **Read the site's content model first.** Find the `[ContentType]` classes and note, for each one you will seed: its base type (`PageData`, `BlockData`, `ExperienceData`, `SectionData`), its `ContentArea` properties, its image properties (`ContentReference`), and any `[CompositionBehavior]` marking a block as a Visual Builder section or element. Never guess property names; use `nameof(...)`.
2. **Decide what is shared and what is per-page.** Blocks that several pages show in a `ContentArea` are `BlockDefinition`s. Pages are `SeedNode`s. Visual Builder layouts are `SectionDefinition`s placed on Experience pages.
3. **Choose the interface.** Plain `ISeedDefinition` gives realistic-looking placeholder data (Bogus). Implement `IAiSeedDefinition` when the user wants AI-written copy; it is an `ISeedDefinition` plus a typed `WritingBrief`, and still works with no AI provider registered (the brief is simply ignored).
4. **Write the class** using the templates below, then **register it** and **build**.
5. **Tell the user to run Dry run** in the ContentSeeder admin gadget (CMS admin) before Seed, and to **Reseed** after changing the definition (Seed only creates what is missing).

## Updating an existing definition

Most requests are changes to a definition that is already seeded ("add a video", "seed the new testimonial block", "make the hero shorter"). Edit it, don't rewrite it.

1. **Read the whole existing class first**, plus anything it calls (helper methods, shared constants). Note how it is written: named arguments, helper methods, comments, the ids it uses.
2. **Make the smallest change that does the job**, in the style already there. Keep ids, `configure` callbacks, display settings, comments and the `WritingBrief` as they are unless the request is about them. Never regenerate the file from the templates below.
3. **Keep the `Id`.** Content is tracked by the definition's `Id`; changing it makes the seeder treat the definition as a new one and leaves the old content behind. Only change it if the user wants that.
4. **Keep references consistent.** Adding a block or section: also add the `ContentAreaAssignment` / `SectionSlotAssignment` that uses it (an unused definition seeds content nobody can see). Removing one: also remove every assignment that points at it, or `SeedTree.Validate()` throws.
5. **Adding a content type:** check it exists and is registered, and read its properties (`nameof(...)`, never guessed). If you are adding AI guidance for it, add a `.ForContentType<T>(...)` entry to the brief; an entry for a type that is never seeded does nothing.
6. **Tell the user what applying it needs.** The seeder never edits content that already exists:
   - Changing the tree (adding, removing or renaming a block, section, node or element; changing `count`, `imageSizes`, `videos` or `reuseContent`): the admin gadget shows the definition as changed. They must **Reseed** to see it everywhere.
   - Adding a new node under an existing page: a plain **Seed** creates it under the right parent, with its children, and leaves existing pages alone. The gadget still flags the change, so a Reseed is needed for the rest of the tree to match the definition.
   - Changing only the `WritingBrief`, the `Generators` or a `configure` callback: the tree's shape is the same, so nothing is flagged, but it still only affects content created from now on. **Reseed** to regenerate.
   - Adding a language is configuration (`Languages`), not a definition change.
   - Suggest **Dry run** first when the change adds repeating elements, videos or AI-written content, since each is more requests.
7. **Build**, and mention anything you could not verify (for example a feed or channel id you were given).

## Template: ISeedDefinition

```csharp
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

public sealed class SiteSeedDefinition : ISeedDefinition
{
    // Stable and unique. Convention: bump the suffix when you want a clean slate.
    public string Id => "site-seed-v1";

    public SeedTree Build()
    {
        // Shared blocks: created first, referenced by id from ContentAreaAssignments.
        var blocks = new List<BlockDefinition>
        {
            new("hero", typeof(HeroBlock),
                imageSizes: new Dictionary<string, (int, int)>
                {
                    [nameof(HeroBlock.BackgroundImage)] = (1920, 600),
                }),
            new("promo1", typeof(PromoBlock)),
            new("promo2", typeof(PromoBlock)),
        };

        // Pages: count = how many independent instances; children are created under EACH instance.
        var articles = SeedFactory.Page(typeof(ArticlePage), count: 5,
            imageSizes: new Dictionary<string, (int, int)> { [nameof(ArticlePage.HeroImage)] = (1200, 630) });

        var home = SeedFactory.Page(
            typeof(StartPage),
            count: 1,
            contentAreaAssignments:
            [
                new ContentAreaAssignment(nameof(StartPage.HeroArea), ["hero"]),
                new ContentAreaAssignment(nameof(StartPage.PromoArea), ["promo1", "promo2"]),
            ],
            configure: (page, i) => ((StartPage)page).Name = "Home",
            children: [articles]);

        // Parameter order: blocks, nodes, sections.
        return SeedFactory.Tree(blocks, [home]);
    }
}
```

### API summary

| Type | Constructor | Notes |
|------|-------------|-------|
| `SeedTree` | `(blocks?, nodes?, sections?)` | Root. Blocks are created first, then sections, then pages. |
| `BlockDefinition` | `(id, type, configure?, imageSizes?)` | A shared block. `id` is referenced by `ContentAreaAssignment`. |
| `SeedNode` | `(type, count = 1, contentAreaAssignments?, sections?, configure?, imageSizes?, children?)` | A page (or any content under a parent). Always use named arguments. |
| `ContentAreaAssignment` | `(propertyName, blockIds)` | `propertyName` is a `ContentArea` property on that node's type. |
| `configure` | `Action<object, int>` | Called after generation, before saving; `int` is the zero-based instance index. Cast `object` to the type. |
| `imageSizes` | `Dictionary<string, (int Width, int Height)>` | Only the `ContentReference` properties listed get an image; the rest stay null. |
| `videos` | `Dictionary<string, VideoSource>` | Last parameter of `BlockDefinition`, `SeedNode`, `SectionDefinition`, `ElementDefinition`, `RepeatingElementDefinition`. See "Videos" below. |
| `MasterLanguage` / `Languages` | `string?` / `IReadOnlyList<string>?` (properties on the definition, default null) | Override `ContentSeeder:MasterLanguage` and `Languages` for this seed only. Null uses the options; an empty list means no translation branches. A tag must be a valid culture. |
| `Generators` | `ContentGenerators?` (property on the definition, default null) | Bogus generator rules for this definition, built with `.ForContentType<T>(t => t.Property(x => x.Prop, f => ...))`. A language-specific rule is `.Property(x => x.Prop, "zh", f => ...)`. See "Bogus generator rules" below. |

## Rules that are easy to get wrong

- **IDs share one namespace.** A `BlockDefinition.Id` and a `SectionDefinition.Id` must all be unique; every assignment must reference a declared id. `SeedTree.Validate()` throws otherwise.
- **`ContentAreaAssignment` applies to the master language only.** Other languages get translated or generated text; shared blocks have their own language versions.
- **Do not set text properties in `configure` unless you mean to override.** `configure` runs after the provider (Bogus or AI), so it wins. Use it for deterministic things such as `Name`, dates, or ordering, not for prose you want an AI to write.
- **`configure` also runs for every language version, and a `[CultureSpecific]` value it sets is copied to each one unchanged.** So a title set in `configure` appears in English on every language. Use `configure` only for a value that should be the same everywhere (`Name`, a fixed date, ordering). Per-language text belongs in generator rules, with a language (`.Property(x => x.Title, "zh", f => ...)`). A property that isn't `[CultureSpecific]` (a date, a price, an image, a flag) applies to the master only; the seeder skips it on language versions rather than failing the save.
- **Images** come only from `imageSizes`. Listing a property that is not a `ContentReference` does nothing.
- **Changing the shape of a definition** (adding a node, section, block, or changing counts) is detected by the admin gadget, which suggests **Reseed**. Plain Seed never edits existing content.
- **Everything is created through Optimizely's APIs**, so content types must be registered and a type derived from `ImageData` must exist if you use `imageSizes`.
- Keep `count` small while developing. With AI generation every item is a request: use Dry run to see the number.

## Visual Builder (Experience pages)

Sections are declared in `SeedFactory.Tree(blocks, nodes, sections)` and placed on `ExperienceData` pages with `SeedFactory.Experience(sections: ...)` and `SectionSlotAssignment`. Use `SeedFactory.Page` and `SeedFactory.Tree` (two arguments) for pages that place no sections. Placing sections on a page that does not derive from `ExperienceData` throws.

```csharp
var sections = new List<SectionDefinition>
{
    // Structural section: a SectionData with its own rows/columns/elements. Built inline on every
    // Experience that uses it, with fresh content each time.
    new("content-section", typeof(BlankSection),
        rows:
        [
            new RowDefinition(columns:
            [
                new ColumnDefinition(elements:
                [
                    new ElementDefinition(typeof(RichTextElementBlock), name: "Body Text"),
                    new ElementDefinition(typeof(ButtonElementBlock), name: "CTA Button"),
                ]),
            ]),
            // One row per column count from 1 to 4 (rows of 1, 2, 3, 4 columns), one element per column.
            new RepeatingElementDefinition(typeof(RichTextElementBlock), name: "Text", minColumns: 1, maxColumns: 4),
        ]),

    // Leaf section: a BlockData used directly as a section. Created once and shared by every page.
    new("hero-section", typeof(HeroSection),
        imageSizes: new Dictionary<string, (int, int)> { [nameof(HeroSection.BackgroundImage)] = (1920, 800) }),
};

var landing = SeedFactory.Experience(
    typeof(LandingPageExperience),
    count: 3,
    sections:
    [
        new SectionSlotAssignment("hero-section", name: "Hero"),
        new SectionSlotAssignment("content-section", name: "Content"),
    ]);

return SeedFactory.Tree(blocks: null, nodes: [home /* contains landing as a child */], sections: sections);
```

- `rows` is only valid when the type derives from `SectionData`; elements must be `BlockData` types enabled as elements.
- `RowDefinition`, `ColumnDefinition`, `ElementDefinition`, `RepeatingElementDefinition` and `SectionSlotAssignment` all accept `displayTemplateKey` / `displaySettings` for Visual Builder Styles; set them only when the site registered those styles.
- Every language of an Experience gets the master's layout; inline elements are translated, shared sections and blocks are referenced.
- A `RepeatingElementDefinition` from 1 to 6 columns creates 21 elements per Experience, which is 21 AI requests per language. Pass `reuseContent: true` when the repeated elements are only there to exercise a layout: the content is generated once and copied (`configure` still runs for each copy), and the copies are translated once. Leave it off (the default) when each element should read differently.

## Videos

Declare video properties with `videos:`, keyed by property name (like `imageSizes`):

```csharp
new BlockDefinition("video", typeof(VideoBlock),
    videos: new Dictionary<string, VideoSource>
    {
        [nameof(VideoBlock.VideoUrl)] = VideoSource.YouTube("UCSMOQeBJ2RAnuFungnQOxLg", titleProperty: nameof(VideoBlock.Caption)),
        [nameof(VideoBlock.Video)] = VideoSource.Uploaded(),
    })
```

- Sources: `YouTube("UC…" or "@handle")`, `YouTubePlaylist("PL…")`, `Vimeo("channel")` (vimeo.com/channels/x), `VimeoUser("user")` (vimeo.com/user), `Urls("https://youtu.be/…", …)`, and `Uploaded()`. No API keys.
- **`Uploaded()` is a short sample clip that is downloaded the first time it is used** (not shipped in the package) and saved once as a media asset that every property shares. It needs internet access. Point `ContentSeeder:SampleVideoUrl` (or `Uploaded("https://…/clip.mp4")`) at your own direct `.mp4` link to change it.
- **Property types matter.** Feed and URL sources need a `string` (or `Url`) property and store a watch URL. `Uploaded()` needs a `ContentReference` property and a content type extending `VideoData` somewhere in the site. A mismatch is skipped with a warning.
- **Check the site has somewhere to put a video.** The seeder fills properties; it does not create content types. If there is no block with a video property, say so and suggest adding one (a `VideoBlock` with a `ContentReference` for the file, a `string` for a YouTube/Vimeo link and a caption) rather than inventing property names. One block type can be a plain block, a Visual Builder section and an element if it carries both `SectionEnabled` and `ElementEnabled` composition behaviours.
- `titleProperty:` copies the video's real title into a string property, replacing generated/AI text for that property only. Use it for a caption.
- `fallback: ["https://youtu.be/…"]` is used if the feed can't be read; without it the property is left as it was. The seed never fails over a video site.
- Videos work on blocks, pages, shared and structural sections, elements and repeating elements; language versions keep the master's video; `reuseContent: true` copies the first element's video.
- Ask the user for the YouTube **channel id** (`UC…`) or a Vimeo channel/user name when you can: a `@handle` needs a lookup. Do not invent channel ids.

## Bogus generator rules

Put per-content Bogus rules on the definition, not in `Startup.cs`. They sit next to `WritingBrief`:

```csharp
public ContentGenerators Generators { get; } = new ContentGenerators()
    .ForContentType<HeroBlock>(t => t
        .Property(x => x.Heading, f => f.Commerce.ProductAdjective() + " " + f.Commerce.Product()))
    .ForContentType<PromoBlock>(t => t.Property(x => x.Title, f => f.Commerce.Department()));
```

- **Bogus and AI are separate.** Rules drive Bogus values; `WritingBrief` drives the AI's prose. An environment that uses Bogus (Integration) and one that uses AI (pre-production) both use the same definition, and only `ContentSeeder:AiProvider` differs.
- Rules also set the non-text values under AI, and the placeholder an item keeps if an AI request fails.
- A rule must return the property's type, or a string for a rich text (`XhtmlString`) property.
- **Named sets:** `.ForContentType<T>("name", t => ...)` adds a second rule set for a type, on `ContentGenerators` and `AiWritingBrief`. An item uses it with `instance: "name"` on its `SeedNode`, `BlockDefinition`, `SectionDefinition`, `ElementDefinition` or `RepeatingElementDefinition`. Named rules win per property; the rest falls back to the default. Brief-wide fields (`Prompt`, `ToneOfVoice`, `MaxParagraphs`…) are not per set. Children do not inherit a page's set. A name the definition does not define for that type fails the seed.
- **Rich text with options:** `.RichText(x => x.Prop, o => o.Tags("strong", "em").Probability(0.2).Paragraphs(2))` adds inline tags to random words. Allowed tags: strong, b, em, i, u, s, sub, sup, code, mark, a. `.KitchenSink(x => x.Prop)` writes a fixed block with every common editor tag, in a set order. Both are Bogus only; the AI leaves a `KitchenSink` property alone. The property must be `XhtmlString`.
- **Blank fields:** `.Blank(x => x.Prop)` in a generator rule leaves that field empty under Bogus. Use it for a field that should be empty; a global map entry for the same property does not apply.
- **Per-language rules:** `.Property(x => x.Heading, "zh", f => ...)` is used only when that language is generated. Lookup is the exact tag, then its base language (`zh-Hans` uses `zh`), then the language-neutral rule. Tags match regardless of case. Give every language its own text wherever the content is shown to readers.
- **A fixed value** is a rule that returns a constant, e.g. `f => "The Trailhead journal"`. If the AI provider writes that property, also add `.UseGenerator(x => x.Title)` to the brief for that type, so the AI does not write it.
- Precedence: this rule, then the global `ExtendBogusPropertyMap` entries, then the built-in heuristics. Use `ExtendBogusPropertyMap` only for a rule that should apply to every definition.
- Changing rules needs a **Reseed**; they are not in the "definition changed" check.

## Template: IAiSeedDefinition

Add a `WritingBrief`. It is only read when an AI provider is registered (`UseAiWritingForContentSeeder`, from the `.AI` package); with Bogus it is ignored, so the same class works either way.

```csharp
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

public sealed class SiteSeedDefinition : IAiSeedDefinition
{
    public string Id => "site-seed-v1";

    public AiWritingBrief WritingBrief { get; } = new AiWritingBrief
    {
        Prompt = "Trailhead & Co., a fictional UK shop selling hiking gear, plus walking guides.", // what the site is
        ToneOfVoice = "Friendly, practical, quietly enthusiastic. Plain English.",
        Audience = "Day hikers and weekend backpackers.",
        Instructions = "British spelling. Sentence case for headings. No exclamation marks.",
        AvoidPhrases = ["elevate", "unlock", "seamless"],
        MaxParagraphs = 3, // cap for rich-text (XhtmlString) properties
    }
    // Per content type; also applies to derived types, and more-derived guidance wins.
    .ForContentType<ArticlePage>(t => t
        .Prompt("Practical guides and route write-ups.")
        .Property(x => x.Title, "A specific headline.", maxCharacters: 70)
        .Property(x => x.Author, "A realistic full name."))
    .ForContentType<HeroSection>(t => t
        .Property(x => x.Heading, "A punchy headline.", maxWords: 5)
        .Property(x => x.CtaButtonText, "Two or three words.", maxWords: 3)
        .Blank(x => x.InternalNote));

    public SeedTree Build() { /* exactly as for ISeedDefinition */ }
}
```

### Writing brief rules

- **`Prompt` says what the site is about** (a business, audience, subject), not how to write. Be concrete; "a fictional UK shop selling hiking gear" beats "an e-commerce site".
- **`.Property(x => x.Prop, guidance, maxWords:, maxCharacters:)`** gives one property guidance. `maxWords` / `maxCharacters` are **hard limits** for plain text: an over-long reply is sent back once, then trimmed. Use them for headings and button labels. Guidance alone is only a request and small models ignore it.
- **Guidance replaces the default length rule.** Write it as the requirement itself ("Two or three words.", "Just the figure, e.g. 98%"), not as a description of the field.
- **`.Blank(x => x.Prop)`** leaves the field empty under the AI provider: the model does not write it, and no value is generated. **`.UseGenerator(x => x.Prop)`** has Bogus fill it instead of the model.
- Properties whose names end in `Url`, `Link`, `Email`, `Id`, `Key`, `Code`, `Slug`, `Path`, `Icon`, `Class`, `Target`, `Template` etc. are treated as machine values and left to Bogus unless you add `.Property(...)` for them.
- Only `string` and `XhtmlString` properties are written by AI. Numbers, dates, images and URLs stay Bogus/placeholder.
- Additional languages are **translated from the master language**, not written separately, so the brief is written in the master language.
- **Formatting:** `string` properties never get HTML; a rich text property may get only the allow-listed tags (paragraphs, strong, em, u, h2–h4, lists, blockquote, a). Other markup is removed with its text kept.

## Registration and configuration

```csharp
services.AddContentSeeder()
        .AddDefinition<SiteSeedDefinition>();

services.Configure<ContentSeederOptions>(configuration.GetSection("ContentSeeder"));
```

```json
{ "ContentSeeder": { "SeedOnStartup": false, "MasterLanguage": "en", "Languages": ["sv"] } }
```

AI text (optional, `TechnicalDogsbody.Optimizely.ContentSeeder.AI` package; `Model` is required):

```csharp
services.UseAiWritingForContentSeeder(_ => new OllamaApiClient(new Uri("http://localhost:11434")),
    o => { o.Model = "llama3.2:3b"; o.DisplayName = "Ollama"; });
```

Any `IChatClient` works (Claude, OpenAI, Azure OpenAI, Bedrock, Ollama, Gemini). `ContentSeeder:MaxParallelGeneration` (default 4) sets how many AI requests run at once for Visual Builder elements and their translations; it only speeds Ollama up if the server allows parallel requests (`OLLAMA_NUM_PARALLEL`).

Ollama reasoning models (`gemma4`, `qwen3`…) think before they answer, which makes short copy several times slower with nothing in the logs. Set `Effort` to `None` (`o.Effort = ReasoningEffort.None`, or `"Effort": "None"` in config) to turn thinking off.

## Checklist before you finish

- [ ] `Id` is unique and stable; blocks and sections have distinct ids.
- [ ] Every `ContentAreaAssignment` property exists on that node's type and every block id is declared.
- [ ] `Sections` are only on `ExperienceData` nodes; `rows` only on `SectionData` types.
- [ ] Property names use `nameof(...)`; `configure` casts to the right type.
- [ ] Counts are modest, and the user knows to run Dry run first.
- [ ] Every block and section you added is actually referenced by an assignment (and nothing references one you removed).
- [ ] If you changed an existing definition: its `Id`, ids, comments and callbacks are untouched, and the user knows to **Reseed**.
- [ ] Videos: property types match the source (`string` for feeds and URLs, `ContentReference` for `Uploaded()`), and a `VideoData` type exists if `Uploaded()` is used.
- [ ] Bogus generator rules are on the definition (`Generators`), not in `Startup.cs`; each rule returns the property's type.
- [ ] Text that differs by language has a language rule (`.Property(x => x.Prop, "zh", ...)`), and no per-language text is set in `configure`.
- [ ] A fixed value the AI could write is marked `UseGenerator` in the brief (`.UseGenerator(...)`).
- [ ] The project builds.

<p align="center">
  <img src="https://raw.githubusercontent.com/technicaldogsbody/optimizely-content-seeder/main/logo_and_title.png" alt="Content Seeder" width="240" />
</p>

# TechnicalDogsbody.Optimizely.ContentSeeder

[![CI](https://github.com/technicaldogsbody/optimizely-content-seeder/actions/workflows/ci.yml/badge.svg)](https://github.com/technicaldogsbody/optimizely-content-seeder/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/TechnicalDogsbody.Optimizely.ContentSeeder.svg)](https://www.nuget.org/packages/TechnicalDogsbody.Optimizely.ContentSeeder)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Releasing: see [RELEASING.md](RELEASING.md).

A fluent C# API for seeding content trees, shared blocks, and media assets into Optimizely CMS 12 and CMS 13 environments. Designed for developer local setup, integration tests, and demo sites. CMS 13 also supports Visual Builder sections; CMS 12 seeds pages, blocks and media.

Content is seeded once per definition ID. Re-running the app does not duplicate content. A built-in admin UI gadget lets you re-seed or delete all seeded content on demand.

---

## Packages

| Package | Purpose |
|---------|---------|
| `TechnicalDogsbody.Optimizely.ContentSeeder` | The CMS-neutral core: seed definitions, Bogus fake data, the seeding contracts and the shared admin UI. Every site also needs one connector. |
| `TechnicalDogsbody.Optimizely.ContentSeeder.Connector.Optimizely13` | The connector for Optimizely CMS 13. Seeds pages, blocks, media and Visual Builder sections, adds the admin gadget and the completion notification, and provides `SeedFactory.Experience`. |
| `TechnicalDogsbody.Optimizely.ContentSeeder.Connector.Optimizely12` | The connector for Optimizely CMS 12. Seeds pages, blocks and media. It has no sections, Experience pages or notification bell. |
| `TechnicalDogsbody.Optimizely.ContentSeeder.AI` | Optional AI add-on. Writes text properties through any Microsoft.Extensions.AI `IChatClient`: Claude (Claude API or Amazon Bedrock), OpenAI, Azure OpenAI, Ollama (free, local), Gemini and more. |

---

## Installation

Install the connector for your CMS version. It brings in the core package.

For Optimizely CMS 13:

```shell
dotnet add package TechnicalDogsbody.Optimizely.ContentSeeder.Connector.Optimizely13
```

For Optimizely CMS 12:

```shell
dotnet add package TechnicalDogsbody.Optimizely.ContentSeeder.Connector.Optimizely12
```

Add `TechnicalDogsbody.Optimizely.ContentSeeder.AI` as well if you want AI-written content.

Add the Optimizely NuGet feed first if you have not already:

```shell
dotnet nuget add source "https://nuget.optimizely.com/v3/index.json" -n Optimizely
```

---

## Quick start

### 1. Register the seeder

```csharp
// Program.cs or Startup.cs. Needs the connector's namespace for AddContentSeeder and SeedFactory.
using TechnicalDogsbody.Optimizely.ContentSeeder.Extensions;

services.AddContentSeeder()
        .AddDefinition<MySeedDefinition>();

services.Configure<ContentSeederOptions>(
    configuration.GetSection("ContentSeeder"));
```

### 2. Configure via appsettings

```json
{
  "ContentSeeder": {
    "SeedOnStartup": true,
    "MasterLanguage": "en",
    "Languages": ["sv"]
  }
}
```

`SeedOnStartup: true` runs the seeder automatically on startup. Set it to `false` to trigger seeding manually from the admin gadget instead.

### 3. Write a seed definition

```csharp
public sealed class MySeedDefinition : ISeedDefinition
{
    public string Id => "my-site-seed-v1";

    public SeedTree Build()
    {
        var promoBlock = new BlockDefinition(
            id: "promo",
            type: typeof(PromoBlock));

        var homePage = SeedFactory.Page(
            type: typeof(StartPage),
            count: 1,
            configure: (page, _) => ((StartPage)page).Name = "Home",
            contentAreaAssignments:
            [
                new ContentAreaAssignment(nameof(StartPage.PromoArea), ["promo"])
            ],
            children:
            [
                SeedFactory.Page(typeof(ArticlePage), count: 5)
            ]);

        return SeedFactory.Tree(
            blocks: [promoBlock],
            nodes:  [homePage]);
    }
}
```

---

## Concepts

### SeedTree

The root object returned by `ISeedDefinition.Build()`. Takes a list of `BlockDefinition` instances and a list of root `SeedNode` instances. Blocks are created first, before any pages.

```csharp
return SeedFactory.Tree(blocks: [heroBlock, promoBlock], nodes: [startPage]);
```

### BlockDefinition

Declares a shared block. Each block has a stable `id` that other nodes reference in `ContentAreaAssignment`.

```csharp
new BlockDefinition(
    id:         "hero",
    type:       typeof(HeroBlock),
    configure:  (block, _) => ((HeroBlock)block).Heading = "Welcome",
    imageSizes: new Dictionary<string, (int, int)>
    {
        [nameof(HeroBlock.BackgroundImage)] = (1920, 600)
    })
```

### SeedNode

Declares a page type and its children. `count` controls how many independent instances are created.

```csharp
SeedFactory.Page(
    type:  typeof(ArticlePage),
    count: 5,
    imageSizes: new Dictionary<string, (int, int)>
    {
        [nameof(ArticlePage.HeroImage)] = (1200, 630)
    },
    children: [SeedFactory.Page(typeof(CommentBlock), count: 2)])
```

Child nodes are created under **each** instance of the parent. Five `ArticlePage` instances with two `CommentBlock` children each produces ten `CommentBlock` instances total.

### ContentAreaAssignment

Assigns one or more blocks to a `ContentArea` property on a page or block.

```csharp
new ContentAreaAssignment(nameof(StartPage.PromoArea), ["promo1", "promo2", "promo3"])
```

The string IDs must match `BlockDefinition.Id` values declared in the same `SeedTree`.

### Image sizes

The `imageSizes` dictionary on `BlockDefinition` and `SeedNode` controls which `ContentReference` properties receive a generated image asset, and at what dimensions. Properties not listed are left `null`.

```csharp
imageSizes: new Dictionary<string, (int, int)>
{
    [nameof(HeroBlock.BackgroundImage)] = (1920, 600),
    [nameof(HeroBlock.ThumbnailImage)]  = (400,  300)
}
```

Images are downloaded from [Picsum Photos](https://picsum.photos/) and saved as `ImageData` assets in Optimizely's asset tree.

### Videos

Declare video properties with `videos`, the video equivalent of `imageSizes`. The key is the property name; the value says
where the video comes from. It is available on `BlockDefinition`, `SeedNode`, `SectionDefinition`, `ElementDefinition` and
`RepeatingElementDefinition`.

```csharp
new BlockDefinition("video", typeof(VideoBlock),
    videos: new Dictionary<string, VideoSource>
    {
        // A random recent video from a YouTube channel, written to a string property as a watch URL,
        // with the video's own title written to Caption so the text matches the video.
        [nameof(VideoBlock.VideoUrl)] = VideoSource.YouTube("UCSMOQeBJ2RAnuFungnQOxLg", titleProperty: nameof(VideoBlock.Caption)),

        // The sample video, saved once as a media asset and shared by everything that asks for it.
        [nameof(VideoBlock.Video)] = VideoSource.Uploaded(),
    })
```

| Source | Picks | Property type |
|--------|-------|---------------|
| `VideoSource.YouTube("UC…")` or `("@handle")` | a recent video from the channel's public feed | `string` or `Url` |
| `VideoSource.YouTubePlaylist("PL…")` | a video from a playlist | `string` or `Url` |
| `VideoSource.Vimeo("staffpicks")` | a recent video from a Vimeo channel (vimeo.com/channels/…) | `string` or `Url` |
| `VideoSource.VimeoUser("someone")` | a recent video from a Vimeo user (vimeo.com/someone) | `string` or `Url` |
| `VideoSource.Urls(…)` | one of the YouTube/Vimeo URLs you list | `string` or `Url` |
| `VideoSource.Uploaded()` | the sample video, as a media asset | `ContentReference` |

- **No API keys.** Feeds are the public ones YouTube and Vimeo publish. Each feed is read once per run, and picks come from a
  shuffled bag so a video isn't repeated until the feed is used up. A `"@handle"` is turned into a channel id by reading the
  channel page, so use the `UC…` id if you need it to be rock solid.
- **If a feed can't be read** the seed carries on: pass `fallback: ["https://youtu.be/…"]` to use those URLs, otherwise the
  property is left as it was. One warning is logged per feed.
- **`titleProperty`** writes the video's real title into a string property (replacing generated or AI text for that one
  property). Language versions keep the master's title, since a video's title doesn't change with the language.
- **Language versions** keep the master's video; a random one is never chosen per language.
- **The uploaded sample** is a 10-second, 2 MB clip of *Big Buck Bunny* (© Blender Foundation, CC BY 3.0), downloaded from
  test-videos.co.uk on first use rather than shipped in the package. It is saved as a `VideoData` asset in the seeded media
  folder, so your site needs a content type that extends `EPiServer.Core.VideoData` (like the demo's `DemoVideoFile`).
  Set `ContentSeeder:SampleVideoUrl` to any direct `.mp4` link to use your own, or pass a URL to `Uploaded("https://…")`.
- With `reuseContent: true` on a `RepeatingElementDefinition`, every copy keeps the first element's video.
- Videos are applied after the content provider and before `configure`, so `configure` can still override.
- Declare a video on a property of the wrong type, or one that doesn't exist, and it is skipped with one warning.

`VideoUrls.TryParse(url, out var video)` is public and turns any YouTube/Vimeo URL form (watch, `youtu.be`, shorts, embed, player)
into a `VideoReference` with `.WatchUrl` and `.EmbedUrl`, which is handy in a view; see the demo's `VideoBlock.cshtml`.

### Configure callback

An optional `Action<object, int>` called after the content generation provider populates properties and before the content is saved. The second parameter is the zero-based instance index (useful when `count > 1`).

```csharp
configure: (page, i) =>
{
    var article = (ArticlePage)page;
    article.Name         = $"Article {i + 1}";
    article.PublishedDate = DateTime.UtcNow.AddDays(-i);
}
```

`configure` also runs for every language version. Its effect there is limited:

- **`[CultureSpecific]` properties and `Name`** set in `configure` are copied to each language version, so they hold the **same value in every language**. Do not use `configure` for per-language text: a title set there would appear in English on every language.
- **Other properties** (dates, prices, images, flags) apply to the master only. Language versions keep the master's value, and the seeder skips the write rather than failing the save.

For text that should differ by language, use [generator rules](#generator-rules-in-the-definition) with a language.

### Multi-language

Set `MasterLanguage` and `Languages` in `ContentSeederOptions`. The seeder creates a master-language version and one language branch per entry in `Languages`. Content area assignments apply to the master language only.

A seed definition can set its own languages, which override the options for that seed only. Leave them unset to use the options. An empty list means that seed has no translation branches, even when the options name some:

```csharp
public string? MasterLanguage => "en-GB";
public IReadOnlyList<string>? Languages => ["sv-SE", "ar-AE"];
```

A language tag set on a definition must be a valid culture (such as `sv-SE`), or the seed fails before it writes anything. Tags in the options keep the older behaviour of falling back to English with a warning.

Generator rules can target a language, so each language version gets its own placeholder text. A rule for a language is used for that language only, and other languages use the language-neutral rule. See [Generator rules in the definition](#generator-rules-in-the-definition).

```json
{
  "ContentSeeder": {
    "MasterLanguage": "en",
    "Languages": ["sv", "de"]
  }
}
```

**Placeholder text per language.** The default Bogus provider writes "lorem ipsum" in the language's own words only where
Bogus has them: Arabic, Farsi, Japanese, Korean, Russian, Turkish, Vietnamese and a few others. For every other
language Bogus falls back to the English (Latin) set, which looks fine for Swedish or German but is the wrong script
for Chinese, so the seeder supplies a small built-in word list for **Chinese (Simplified and Traditional), Ukrainian,
Greek, Georgian and Nepali**. Other languages without Bogus data still get Latin lorem. For real text in every
language, use an [AI provider](#ai-content-generation), which translates from the master language.

---

## Extending the property map

The built-in Bogus property map covers common property name patterns (`Title`, `Name`, `Email`, `Url`, etc.) and CLR types (`string`, `int`, `bool`, `DateTime`, `double`). You can extend or replace it.

### Generator rules in the definition

The usual place for rules about one definition's content is the definition itself, next to its `WritingBrief`. The rules are typed, so a misspelt property fails to compile:

```csharp
public ContentGenerators Generators { get; } = new ContentGenerators()
    .ForContentType<HeroBlock>(t => t
        .Property(x => x.Heading, f => f.Commerce.ProductAdjective() + " " + f.Commerce.Product()))
    .ForContentType<PromoBlock>(t => t.Property(x => x.Title, f => f.Commerce.Department()));
```

A rule can also be given for one language, by its CMS language tag. Add a language rule next to the neutral rule:

```csharp
.ForContentType<HeroBlock>(t => t
    .Property(x => x.Heading, f => f.Commerce.ProductAdjective() + " " + f.Commerce.Product())
    .Property(x => x.Heading, "zh", f => "适合" + f.PickRandom("远足", "长途穿越") + "的装备"))
```

- **Blank fields:** `.Blank(x => x.Prop)` in a generator rule leaves that field empty under Bogus, for a field that should be empty.
- **Language lookup:** the exact tag, then its base language (`zh-Hans` uses `zh`), then the language-neutral rule. Tags match regardless of case.
- **A fixed value** (a title that is the same in every language, say) is a rule that returns a constant: `f => "The Trailhead journal"`. If the AI provider writes that property, add `.UseGenerator(x => x.Title)` to the brief, so the AI does not write it and Bogus fills it from the rule.
- **Bogus and AI do different jobs.** Generator rules decide values when Bogus generates them. With an AI provider, the writing brief drives the prose. Rules still set the non-text values (numbers, dates, URLs, flags), and the placeholder an item keeps if an AI request fails. Switch provider by configuration; the definition does not change.
- **Precedence:** a rule on the definition wins over the global entries below, which win over the built-in heuristics.
- **Rules apply to derived types too**, and a rule on a more derived type wins over one on its base type.
- **Rules are not part of the "definition changed" check.** Changing them needs a **Reseed** to apply.

### Rich text (`XhtmlString`)

A rich text property can be filled with generated HTML rather than a single plain paragraph. `RichText` takes a configuration callback:

```csharp
.ForContentType<ArticlePage>(t => t
    .RichText(x => x.Body, o => o
        .Tags("strong", "em", "a")   // words are wrapped in these at random
        .Probability(0.2)            // chance that each word is wrapped (default 0.15)
        .Paragraphs(3)))             // paragraphs written (default 1)
```

The tags allowed are `strong`, `b`, `em`, `i`, `u`, `s`, `sub`, `sup`, `code`, `mark` and `a`. A link is given the placeholder `href="#"`. Passing any other tag throws, since it could not wrap a single word.

`KitchenSink` writes a fixed block that uses every common editor tag: headings `h1` to `h6`, paragraphs with each inline tag, a line break, `pre`, `blockquote`, a bulleted list with a nested list, a numbered list, a rule, a table and an image. The order never changes, so you can check how a front end renders each tag from one run to the next. The text is lorem, in the language being generated.

```csharp
.ForContentType<ArticlePage>(t => t.KitchenSink(x => x.Body))
```

`RichText` and `KitchenSink` apply to Bogus only. The AI provider writes its own formatting, and it leaves a `KitchenSink` property alone. The property must be an `XhtmlString`; a `string` property throws when the rule is added.

### Named sets

Rules are set per content type, so one page of a type that is otherwise normal needs its own rules. A **named set** adds a second set of rules, under a name, for the same type. Items that name it use its rules for each property it sets, and the default rules for the rest:

```csharp
.ForContentType<ArticlePage>(t => t.RichText(x => x.Body, o => o.Tags("strong")))           // default
.ForContentType<ArticlePage>("kitchenSink", t => t.KitchenSink(x => x.Body))                 // named

SeedFactory.Page(typeof(ArticlePage), instance: "kitchenSink")                                   // uses the named set
```

The same name also works on `AiWritingBrief.ForContentType<T>(name, ...)`, so one name can change both the Bogus rules and the AI brief. Put the name on a page (`SeedNode`), a shared block (`BlockDefinition`), a shared section (`SectionDefinition`), an element (`ElementDefinition`) or a repeating element (`RepeatingElementDefinition`). Notes:

- **Named before default, per property.** A named rule for a property replaces the default's for that property only. In the writing brief, named guidance also drops an inherited length limit, unless the named set gives its own.
- **Language versions use the same set.** A page's, block's or element's translations use the set it was created with.
- **Children do not inherit it.** A page's `instance` applies to that page only. Set it on each child that needs it.
- **Brief-wide settings are not per set.** `Prompt`, `ToneOfVoice`, `Audience`, `Instructions`, `AvoidPhrases` and `MaxParagraphs` apply to the whole definition. A named set can add prompts, guidance, limits, `Blank` and `UseGenerator`.
- **Rows and columns have no name.** Only the elements inside them do.
- **The name is part of the definition's shape.** Adding or changing a name is reported as a change, as with any shape change. Editing a named set's rules is not, as with other rules.
- **A name must be defined.** If a node or element names a set that its definition does not define for that type, the seed fails before it writes anything.

### Global generators (every definition)

Use `ExtendBogusPropertyMap` for a rule that should apply to every definition, such as one property name used across the site:

```csharp
services.AddContentSeeder()
        .AddDefinition<MySeedDefinition>()
        .ExtendBogusPropertyMap(map =>
        {
            // Unqualified — applies to any content type with this property name.
            map["JobTitle"]    = f => f.Name.JobTitle();
            map["CompanyName"] = f => f.Company.CompanyName();

            // Qualified — applies only to HeroBlock.Heading.
            // Takes priority over the unqualified "Heading" entry.
            map["HeroBlock.Heading"] = f => f.Commerce.ProductAdjective() + " " + f.Commerce.Product();
        });
```

Qualified keys use the format `"ContentTypeName.PropertyName"` and take priority over unqualified entries. Suffix matching applies to unqualified keys only (e.g., a key of `"Name"` matches `ProductName`, `AuthorName`, etc.).

### Replace the entire map

```csharp
services.AddContentSeeder()
        .UseBogusPropertyMap<MyCustomPropertyMap>();
```

`MyCustomPropertyMap` must implement `IBogusPropertyMap`. Subclass `DefaultBogusPropertyMap` and override `BuiltInMap` if you want to keep the built-in entries and add your own.

```csharp
public class MyCustomPropertyMap(IOptions<BogusGeneratorMapOptions> options)
    : DefaultBogusPropertyMap(options)
{
    public override IReadOnlyDictionary<string, Func<Faker, object>> BuiltInMap =>
        new Dictionary<string, Func<Faker, object>>(
            base.BuiltInMap,
            StringComparer.OrdinalIgnoreCase)
        {
            ["JobTitle"] = f => f.Name.JobTitle()
        };
}
```

---

## ContentSeederOptions reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SeedOnStartup` | `bool` | `false` | Run all pending definitions automatically on startup. |
| `SeededImagesFolder` | `string` | `"Images/Seeded"` | Folder (relative to the site's global assets root) where seeded media (images and the sample video) is uploaded. Deleted in full on Reseed/Delete All, so re-seeding never accumulates orphaned assets. |
| `SeededBlocksFolder` | `string` | `"Seeded"` | Folder under For All Sites where a seed's shared blocks are created, one subfolder per seed definition (`Seeded/{seed id}`). Each seed's folder is removed on Delete once it is empty. Blocks seeded before this option existed stay where they are until they are deleted and seeded again. |
| `MasterLanguage` | `string` | `"en"` | Language code used for the primary content version. |
| `Languages` | `string[]` | `[]` | Additional language branches created after the master version. |
| `LocaleMap` | `Dictionary<string, string>` | built-in | Maps CMS language tags to Bogus locales (e.g. `"sv"` → `"sv"`). A tag that isn't listed falls back to its base language (`ja-JP` → `ja`, `zh-Hans` → `zh`), then to the master language's locale, then to `en`; each unmapped language is logged once per run. Add an entry to use a different locale. |
| `MaxParallelGeneration` | `int` | `4` | With an AI provider, how many items are generated at once. The elements of a Visual Builder section, and their translations into other languages, are independent, so running them together makes Experience pages much faster. Ignored by the Bogus provider. For Ollama it only helps if the server allows parallel requests (`OLLAMA_NUM_PARALLEL`); set `1` to generate one at a time. |
| `SampleVideoUrl` | `string` | a 2 MB Big Buck Bunny clip | The direct `.mp4` link downloaded for `VideoSource.Uploaded()` and saved once as a media asset. |

---

## AI content generation

The `AI` add-on replaces Bogus for **prose** — `string` and `XhtmlString` properties — while Bogus
still fills everything else (images from `imageSizes`, numbers, dates, URLs, and your generator rules, see
[Extending the property map](#extending-the-property-map)). The writing brief and the generator rules are
independent: the brief steers the AI, and the rules steer Bogus. It works with any [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai)
`IChatClient`, so you choose the vendor by choosing a client; the add-on itself has no vendor SDK dependency.

Each content item is written with **one** structured request covering all of its text properties, so a heading,
summary and body read as one coherent piece. If a request fails or is declined, that item keeps its Bogus
values. An error that would fail every request — bad key, no credit, unknown model, a rejected request, an
unreachable service — switches AI off for the rest of the run with a single logged error.

Properties whose names end in `Url`, `Link`, `Href`, `Email`, `Phone`, `Color`, `Id`, `Key`, `Code`, `Slug`
and similar are left to Bogus unless you give them guidance in the writing brief.

**Formatting.** A `string` property never contains HTML: any markup the model returns is removed, and its text kept. A rich text (`XhtmlString`) property may contain only a short list of tags: paragraphs, `strong`, `em`, `u`, `h2` to `h4`, `ul`/`ol` with `li`, `blockquote` and `a`. Any other tag is removed with its text kept, and attributes are dropped except a link's `href`, which is kept only for `http`, `https`, `mailto` or a relative address. Script and event handlers are therefore never written into content.

### Writing brief

What to write is part of the seed definition, strongly typed. Implement `IAiSeedDefinition` instead of
`ISeedDefinition` (plain definitions are written with an empty brief):

```csharp
public sealed class MySeedDefinition : IAiSeedDefinition
{
    public string Id => "my-seed-v1";

    public AiWritingBrief WritingBrief { get; } = new AiWritingBrief
    {
        Prompt       = "An online shop for independent coffee roasters, selling beans, grinders and brewing kit.",
        ToneOfVoice  = "Warm, knowledgeable and a little playful. Plain English, no jargon.",
        Audience     = "Home baristas, from beginners to enthusiasts.",
        Instructions = "British spelling. Sentence case for headings.",
        AvoidPhrases = ["elevate", "unlock", "seamless"],
        MaxParagraphs = 4,
    }
    .ForContentType<ArticlePage>(t => t
        .Prompt("In-depth brewing guides.")
        .Property(x => x.Title, "A specific, useful headline under 70 characters."))
    .ForContentType<HeroSection>(t => t
        .Property(x => x.Heading, "A punchy headline.", maxWords: 5)
        .UseGenerator(x => x.CtaButtonText));  // Bogus fills it, the AI does not

    public SeedTree Build() => /* ... */;
}
```

`maxWords` and `maxCharacters` are **hard limits**, not requests. Models often ignore "two or three words", so a
plain-text value that breaks a limit is sent back once to be shortened, and trimmed to fit if it is still too
long. For rich-text properties a limit is only passed to the model as a requirement.

`ForContentType<T>` also applies to types deriving from `T` (`ForContentType<BlockData>` covers every block),
with more-derived guidance winning. Editing the brief doesn't trigger the plugin's "definition changed"
warning, which tracks the content's shape only; Reseed to rewrite existing content with a new brief.

### Languages

The master language is written from the brief. Every **additional language is translated from the saved
master-language content**, not generated separately, so each language version says the same thing. The
model is told the language by name ("Swedish (sv)") and is asked to keep the meaning, tone and
formatting. Images, numbers, dates and URLs are shared with the master rather than regenerated, and
word/character limits still apply to the translation.

Everything that has a language version gets one: pages, blocks, shared sections, and the Visual Builder
layout of Experience pages. Each language of an Experience gets the master's sections, rows, columns and
elements, with the inline elements translated; shared sections and blocks are referenced and have
translated versions of their own.

If a translation fails, that item falls back to placeholder text in the right language rather than a
copy of the master's text. Without an AI provider (the default Bogus provider) each language is generated
independently in its own locale, as before.

Translation quality depends on the model. Small local models (such as `llama3.2:3b`) produce Swedish
that is recognisable but clumsy; use a larger model for languages other than English.

### Registering a provider

```shell
dotnet add package TechnicalDogsbody.Optimizely.ContentSeeder.AI
```

Register after `AddContentSeeder()`, passing a factory for the client (or call
`UseAiForContentSeeder(configure)` to use an `IChatClient` you've already registered). The client is registered
as a keyed singleton, so it never clashes with an `IChatClient` your application uses itself.

**Ollama** — free and local ([OllamaSharp](https://www.nuget.org/packages/OllamaSharp)):

```csharp
services.UseAiForContentSeeder(
    _ => new OllamaApiClient(new Uri("http://localhost:11434")),
    o => { o.Model = "llama3.2:3b"; o.DisplayName = "Ollama"; });
```

**Claude API** (the [`Anthropic`](https://www.nuget.org/packages/Anthropic) package):

```csharp
services.UseAiForContentSeeder(
    _ => new AnthropicClient { ApiKey = configuration["ContentSeeder:Anthropic:ApiKey"] }.AsIChatClient(),
    o => { o.Model = "claude-opus-5-5"; o.Effort = ReasoningEffort.Low; o.DisplayName = "Claude"; });
```

Leave `ApiKey` out (`new AnthropicClient()`) and the SDK uses the `ANTHROPIC_API_KEY` environment variable
or an `ant auth login` profile.

**Claude on Amazon Bedrock** (the [`Anthropic.Bedrock`](https://www.nuget.org/packages/Anthropic.Bedrock) package).
Credentials come from the standard AWS chain (environment, shared credentials file, instance/role), or `AwsProfile`:

```csharp
services.UseAiForContentSeeder(
    _ => new AnthropicBedrockMantleClient(new MantleAwsClientOptions { AwsRegion = "us-east-1" }).AsIChatClient(),
    o => { o.Model = "anthropic.claude-opus-5-5"; o.Effort = ReasoningEffort.Low; o.DisplayName = "Bedrock"; });
```

**OpenAI** (the [`Microsoft.Extensions.AI.OpenAI`](https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI) package):

```csharp
services.UseAiForContentSeeder(
    _ => new OpenAIClient(apiKey).GetChatClient("<model>").AsIChatClient(),
    o => { o.Model = "<model>"; o.DisplayName = "OpenAI"; });
```

Any other service that exposes an `IChatClient` (Azure OpenAI, Gemini, …) works the same way. The demo project
([`Startup.cs`](demo/TechnicalDogsbody.Optimizely.ContentSeeder.Demo/Startup.cs)) wires up all four above from
configuration, selected by `ContentSeeder:AiProvider`.

| Setting | Required | Default | Notes |
|---|---|---|---|
| `Model` | Yes | — | Sent with every request. No default, so an outdated model is never used silently. Validated at startup. |
| `DisplayName` | No | from the client | Shown in the admin UI and log messages. When not set it is taken from the client itself (OpenAI, Claude, Ollama…). |
| `MaxOutputTokens` | No | `16000` | Per content item. Reasoning models count reasoning against this. |
| `Effort` | No | not sent | `Low`, `Medium`, `High`… How hard a reasoning model thinks. `Low` suits short copy and is faster and cheaper on Claude and OpenAI reasoning models. Leave unset for models that don't reason. **`None` turns thinking off**: Ollama reasoning models such as `gemma4` and `qwen3` think before answering by default, which for short copy is several times slower (measured on `gemma4:e2b`: 537 tokens and about 7-17s versus 208 tokens and 3.5s with thinking off) and shows nothing in the logs while it happens. Set `Effort` to `None` for them. |
| `UseJsonSchema` | No | `true` | Constrain replies to the item's JSON schema. Set `false` for services that only support plain JSON mode. |
| `StrictJsonSchema` | No | `true` | Ask OpenAI-style services to enforce the schema strictly. Ignored by services without a strict mode. |

Claude receives the schema through its native structured-output support, and OpenAI through `json_schema` with
`strict: true`; both are covered by tests that run the provider through the real SDK adapters.

Small local models are fast and free but follow the brief loosely (length limits especially); larger models
follow it more closely. The first request is slow while Ollama loads the model.

### Cost

With a paid API there is one request per content item: every page, block, section and inline element (a
`RepeatingElementDefinition` from 1 to 6 columns is 21 elements, per Experience page). Use **Dry run** to see

Each additional language adds a translation request per element too, so a long repeating section multiplies
quickly. If the repeated elements only exist to exercise a layout, set `reuseContent: true` on the
`RepeatingElementDefinition`: the content is generated once and every element is a copy (your `configure`
callback still runs for each, so it can vary them). Elements with identical text are also translated once and
the result copied, whether or not they were generated that way.

```csharp
new RepeatingElementDefinition(typeof(RichTextElementBlock), name: "Text", minColumns: 1, maxColumns: 6, reuseContent: true)
```

It makes no difference with the default Bogus provider; with AI it turns 21 requests per language into one.
how many items a Seed would create before running it against a paid API.

---

## After a seed

Code that depends on seeded content, such as pointing a site at the seeded start page, can run after every successful
seed or reseed, whether it was started from the admin UI or at startup:

```csharp
public sealed class PointSiteAtStartPage : ISeedCompletedHandler
{
    public Task HandleAsync(CancellationToken ct = default) => /* ... */ Task.CompletedTask;
}

services.AddContentSeeder()
        .AddSeedCompletedHandler<PointSiteAtStartPage>();
```

Handlers run in registration order. If a seed fails, they don't run.

## Admin UI gadget

The package registers a gadget in the Optimizely CMS admin panel under **Content Seeder** (visible to users in the `CmsAdmin` role).

It shows the seeded page, block and section counts, and which providers are generating the content. While a run is in
progress the **Last seeded** tile becomes **Running for**, showing how long the run has been going; when idle it shows
when the last seed completed. The counts update live during a run: **Text** (Bogus, or the AI provider and its model, marked ✦) and **Images** (Picsum
Photos). A custom provider shows its type name, or implement `IDescribesProvider` to give it a proper name.

The **Seed definition** dropdown chooses what the actions apply to: one seed definition, or **All definitions**. Each
definition in the list shows its page count, whether it is changed, and whether a run was interrupted.

| Action | Description |
|--------|-------------|
| Dry Run | Reports how many items would be created or skipped, without writing anything. |
| Seed | Creates the content that is missing for the chosen definition, or for all definitions. |
| Reseed | Deletes the chosen definition's seeded content and builds it again from its current definition. |
| Delete | Permanently deletes the chosen definition's seeded content and its images. Choosing **All definitions** clears everything. |

Definitions don't affect each other. Seeding, reseeding or deleting one definition never changes another definition's
pages, blocks, sections or images, so each definition can have its own home page. A definition that has been removed
from the code can still be deleted, which cleans up any content it left behind.

---

## Completion email and notification

When a seed or reseed finishes, the user who started it can be told in two ways. Both go only to that user; a run started at
startup, or by a user without an email address or a sign-in name, notifies nobody.

**Email** is sent over SMTP. It's an HTML report in the admin page's style, with a plain-text part for mail clients that
don't show HTML. It lists the totals, a table of each definition's counts and when it was last seeded, and a link to the
admin page. Set these in `ContentSeeder:Smtp`:

| Setting | Description |
|---------|-------------|
| `Host` | The SMTP host. Leave blank to send no email. |
| `Port` | Usually `587`, with STARTTLS. |
| `UseStartTls` | `true` for STARTTLS. |
| `UserName` / `Password` | Login for the server. Keep the password in user secrets. Leave blank for an unauthenticated relay. |
| `FromAddress` / `FromName` | The sender. |

`ContentSeeder:AdminPageUrl` is the absolute address of the admin page, linked from the email. Leave it blank to leave the
link out.

For SendGrid, use host `smtp.sendgrid.net`, port `587`, `UseStartTls` `true`, and username `apikey`, with your API key as
the password.

A mail failure is logged as a warning and never fails the seed.

**The CMS notification bell** (Optimizely CMS 13 only) posts a notification to the same user, on a channel called
`ContentSeeder`. It appears in the CMS notification bell, and it's registered the first time it's needed. CMS 12 has no
notification bell in this package, so only the email applies there.

## Idempotency

Each `ISeedDefinition` has a stable string `Id`. The seeder tracks what it has created in a `ContentSeederRecord` SQL table.

Seed only fills gaps. For each node in the tree it reuses an existing page only if that page sits under the same parent and has the same content type; otherwise it creates the page. So a node you add to a definition is created where it belongs, with its children under it, and nothing that already exists is changed. A page that was deleted in the CMS is created again. Changing content that already exists (its shape, its generator rules, its writing brief) needs a **Reseed**, which deletes the seeded content and builds it again. Use **Delete All** to remove it without rebuilding.

---

## AI coding assistant skill

A ready-made skill teaches your coding assistant how to write a seed definition (`ISeedDefinition` and
`IAiSeedDefinition`, including Visual Builder sections and the AI writing brief) for *your* content model. It lives in
[`skills/`](skills) as one source file, and an installer writes it in the format each tool reads:

```powershell
./skills/install.ps1 -Target C:\src\MySite -Tool claude,copilot   # or: all
```

| Tool | Installed as |
|------|--------------|
| Claude Code | `.claude/skills/seed-definition/SKILL.md` |
| GitHub Copilot | `.github/instructions/seed-definition.instructions.md` |
| Cursor | `.cursor/rules/seed-definition.mdc` |
| Windsurf | `.windsurf/rules/seed-definition.md` |
| Cline | `.clinerules/seed-definition.md` |
| OpenAI Codex and other `AGENTS.md` tools | a marked block in `AGENTS.md` |
| Gemini CLI | a marked block in `GEMINI.md` |

Run it with PowerShell 7 (`pwsh`, on Windows, macOS or Linux). Re-running updates the files in place.

---

## Demo project

A full reference implementation is in the `demo/` folder. See [demo/README.md](demo/README.md) for setup instructions.

---

## Code layout (for contributors)

The repository holds the core, the AI add-on, the two connectors and a shared project. The shared project is the Optimizely
seeding code, compiled into both connectors. Within each assembly the code is organised into layers by namespace.
Dependencies point inwards only: an inner layer never references an outer one. `LayerDependencyTests` enforces this on
every build.

| Layer (namespace) | Holds |
| --- | --- |
| `Definitions` | The seed tree and the fluent definition types. Content types are described by their CLR type. |
| `Abstractions` | Interfaces and models shared across layers, including `ISeedTracker` and the tracking record. |
| `Application` | The MicroMediator commands and queries (`SeedCommand`, `DryRunQuery`, …) and their handlers. |
| `Orchestration` | The seed run: blocks, pages, sections, language branches, translation and deletes. |
| `Infrastructure` | The SQL tracking table (Dapper), its migration and the startup hosted service. |
| `Providers` | Content generation (Bogus and AI), images and videos. |
| `UI` | The admin REST API, page controller, menu and admin gadget. |
| `Extensions` | Registration: `AddContentSeeder()` and its builder. |

Commands and queries go through `IMediator`, and the orchestrator is internal, so the mediator is the supported way to
start a seed run from your own code.

## Supported versions

| Framework | Optimizely CMS |
|-----------|---------------|
| .NET 10 | CMS 13 (13.x), CMS 12 (12.x) |
| .NET 9 | CMS 12 (12.x) |
| .NET 8 | CMS 12 (12.x) |

The core and AI packages target net8, net9 and net10. Each connector targets the frameworks its CMS supports. CMS 13's
connector targets net10 only.

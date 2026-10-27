namespace TechnicalDogsbody.Optimizely.ContentSeeder.Options;

/// <summary>
/// Configuration options for the ContentSeeder package.
/// Bind from appsettings.json under the "ContentSeeder" key.
/// </summary>
public sealed class ContentSeederOptions
{
    /// <summary>
    /// When true, the IHostedService calls SeedAsync() on application start.
    /// Bound from <c>ContentSeeder:SeedOnStartup</c>. Default: false.
    /// </summary>
    public bool SeedOnStartup { get; set; }

    /// <summary>
    /// Slash-separated path (relative to the site's global assets root) of the folder that
    /// holds media assets created by <see cref="Abstractions.IImageContentFactory"/>.
    /// Bound from <c>ContentSeeder:SeededImagesFolder</c>. Default: "Images/Seeded".
    /// Deleted in full (via Reseed/Delete All) so re-seeding never accumulates orphaned images.
    /// </summary>
    public string SeededImagesFolder { get; set; } = "Images/Seeded";

    /// <summary>
    /// Slash-separated path (relative to For All Sites in the block tree) of the folder that holds shared blocks created
    /// by a seed. Each seed definition gets its own subfolder, so the blocks for <c>demo-seed-v1</c> sit in
    /// <c>Seeded/demo-seed-v1</c>. Bound from <c>ContentSeeder:SeededBlocksFolder</c>. Default: "Seeded".
    /// Blocks seeded before this option existed stay where they were until they are deleted and seeded again.
    /// </summary>
    public string SeededBlocksFolder { get; set; } = "Seeded";

    /// <summary>
    /// IETF tag for the primary language. When null, single-language mode is used.
    /// </summary>
    public string? MasterLanguage { get; set; }

    /// <summary>
    /// Additional IETF language tags to seed as translation branches.
    /// </summary>
    public string[] Languages { get; set; } = [];

    /// <summary>
    /// How many content items are written at once when an AI provider is in use. Items within one page's
    /// layout (the elements of a Visual Builder section, and their translations) are independent, so
    /// generating them together cuts the time a seed takes. Ignored by the default Bogus provider, which is
    /// instant. Raise it for cloud APIs; for Ollama it only helps if the server allows parallel requests
    /// (<c>OLLAMA_NUM_PARALLEL</c>). Set to 1 to generate one at a time.
    /// Bound from <c>ContentSeeder:MaxParallelGeneration</c>. Default: 4.
    /// </summary>
    public int MaxParallelGeneration { get; set; } = 4;

    /// <summary>
    /// The video downloaded for <c>VideoSource.Uploaded()</c> and saved once as a media asset. Needs to be a direct link
    /// to an .mp4. Default: a 10-second, 2 MB clip of Big Buck Bunny (© Blender Foundation, CC BY 3.0) from
    /// test-videos.co.uk. Bound from <c>ContentSeeder:SampleVideoUrl</c>.
    /// </summary>
    public string SampleVideoUrl { get; set; } =
        "https://test-videos.co.uk/vids/bigbuckbunny/mp4/h264/720/Big_Buck_Bunny_720_10s_2MB.mp4";

    /// <summary>
    /// Maps IETF language tags to Bogus locale strings.
    /// Pre-populated with the locales Bogus supports; consumers may add or override entries.
    /// A tag that is not listed falls back to its base language ("ja-JP" to "ja"), then to the master
    /// language's locale, then to "en", with a logged message the first time each language is resolved.
    /// </summary>
    /// <summary>Outgoing mail for the completion email. Leave <see cref="SmtpOptions.Host"/> blank to send no email.</summary>
    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>
    /// The absolute URL of the Content Seeder admin page, linked from the completion email. Blank leaves the link out.
    /// </summary>
    public string AdminPageUrl { get; set; } = string.Empty;

    public Dictionary<string, string> LocaleMap { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        // Only locales Bogus actually has. A tag that isn't listed falls back to its base language
        // ("ja-JP" → "ja"), then to the master language's locale, then to "en".
        ["en"]      = "en",
        ["en-US"]   = "en_US",
        ["en-GB"]   = "en_GB",
        ["en-AU"]   = "en_AU",
        ["en-CA"]   = "en_CA",
        ["en-IE"]   = "en_IE",
        ["en-IN"]   = "en_IND",
        ["en-NG"]   = "en_NG",
        ["en-ZA"]   = "en_ZA",
        ["fr"]      = "fr",
        ["fr-CA"]   = "fr_CA",
        ["fr-CH"]   = "fr_CH",
        ["de"]      = "de",
        ["de-AT"]   = "de_AT",
        ["de-CH"]   = "de_CH",
        ["es"]      = "es",
        ["es-MX"]   = "es_MX",
        ["nl"]      = "nl",
        ["nl-BE"]   = "nl_BE",
        ["sv"]      = "sv",
        ["no"]      = "nb_NO",
        ["nb"]      = "nb_NO",
        ["nn"]      = "nb_NO",
        ["fi"]      = "fi",
        ["it"]      = "it",
        ["pt"]      = "pt_PT",
        ["pt-BR"]   = "pt_BR",
        ["ja"]      = "ja",
        // Bogus has no neutral Chinese, only zh_CN (Simplified) and zh_TW (Traditional); plain "zh" gets zh_CN.
        ["zh"]      = "zh_CN",
        ["zh-Hans"] = "zh_CN",
        ["zh-CN"]   = "zh_CN",
        ["zh-SG"]   = "zh_CN",
        ["zh-Hant"] = "zh_TW",
        ["zh-TW"]   = "zh_TW",
        ["zh-HK"]   = "zh_TW",
        ["zh-MO"]   = "zh_TW",
        ["ko"]      = "ko",
        ["ru"]      = "ru",
        ["uk"]      = "uk",
        ["pl"]      = "pl",
        ["cs"]      = "cz",
        ["sk"]      = "sk",
        ["ro"]      = "ro",
        ["hr"]      = "hr",
        ["el"]      = "el",
        ["tr"]      = "tr",
        ["ar"]      = "ar",
        ["fa"]      = "fa",
        ["vi"]      = "vi",
        ["id"]      = "id_ID",
        ["lv"]      = "lv",
        ["ka"]      = "ge",
        ["af"]      = "af_ZA",
        ["az"]      = "az",
        ["ne"]      = "ne",
        ["zu"]      = "zu_ZA",
    };

}

/// <summary>
/// The SMTP server the completion email is sent through. The email goes only to the user who started the seed, and only
/// when they have an email address and <see cref="Host"/> is set.
/// </summary>
public sealed class SmtpOptions
{
    /// <summary>The SMTP host. Blank turns the completion email off.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>The SMTP port. 587 uses STARTTLS, which is the usual setting.</summary>
    public int Port { get; set; } = 587;

    /// <summary>True to upgrade the connection with STARTTLS.</summary>
    public bool UseStartTls { get; set; } = true;

    /// <summary>The login for the SMTP server. Leave blank for an unauthenticated relay.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>The password for <see cref="UserName"/>. Keep it in user secrets, not in appsettings.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>The address the email is sent from.</summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>The display name for <see cref="FromAddress"/>.</summary>
    public string FromName { get; set; } = "Content Seeder";
}

using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Orchestration;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Orchestration;

public class SeedLanguagesTests
{
    private sealed class FakeDefinition(string? master = null, IReadOnlyList<string>? languages = null) : ISeedDefinition
    {
        public string Id => "fake";
        public SeedTree Build() => new();
        public string? MasterLanguage => master;
        public IReadOnlyList<string>? Languages => languages;
    }

    [Fact]
    public void Resolve_DefinitionDoesNotSetLanguages_UsesOptions()
    {
        var options = new ContentSeederOptions { MasterLanguage = "en-GB", Languages = ["sv-SE"] };

        var result = SeedLanguages.Resolve(new FakeDefinition(), options);

        Assert.Equal("en-GB", result.Master);
        Assert.Equal(new[] { "sv-SE" }, result.Additional.ToArray());
    }

    [Fact]
    public void Resolve_DefinitionSetsLanguages_OverridesOptions()
    {
        var options = new ContentSeederOptions { MasterLanguage = "en", Languages = ["sv-SE", "de-DE"] };

        var result = SeedLanguages.Resolve(new FakeDefinition("en-US", ["ar-AE"]), options);

        Assert.Equal("en-US", result.Master);
        Assert.Equal(new[] { "ar-AE" }, result.Additional.ToArray());
    }

    [Fact]
    public void Resolve_DefinitionSetsEmptyList_NoBranchesEvenWhenOptionsNameSome()
    {
        var options = new ContentSeederOptions { Languages = ["sv-SE"] };

        var result = SeedLanguages.Resolve(new FakeDefinition(languages: []), options);

        Assert.Empty(result.Additional);
    }

    [Fact]
    public void Resolve_NothingSet_DefaultsToEnglish()
    {
        var result = SeedLanguages.Resolve(new FakeDefinition(), new ContentSeederOptions());

        Assert.Equal("en", result.Master);
        Assert.Empty(result.Additional);
    }

    [Fact]
    public void Resolve_All_ListsMasterThenBranches()
    {
        var result = SeedLanguages.Resolve(new FakeDefinition("en", ["sv-SE", "de-DE"]), new ContentSeederOptions());

        Assert.Equal(new[] { "en", "sv-SE", "de-DE" }, result.All.ToArray());
    }

    [Fact]
    public void Resolve_DefinitionSetsInvalidMasterTag_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SeedLanguages.Resolve(new FakeDefinition("not a tag!"), new ContentSeederOptions()));

        Assert.Contains("not a tag!", ex.Message);
    }

    [Fact]
    public void Resolve_DefinitionSetsInvalidBranchTag_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SeedLanguages.Resolve(new FakeDefinition("en", ["sv-SE", "not a tag!"]), new ContentSeederOptions()));
    }

    [Fact]
    public void Resolve_OptionsSetInvalidTag_DoesNotThrow()
    {
        // Options keep their existing fallback to English, so an invalid tag there is not an error here.
        var options = new ContentSeederOptions { Languages = ["not a tag!"] };

        var result = SeedLanguages.Resolve(new FakeDefinition(), options);

        Assert.Equal(new[] { "not a tag!" }, result.Additional.ToArray());
    }
}

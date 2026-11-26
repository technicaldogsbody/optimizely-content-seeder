using System.ComponentModel.DataAnnotations;
using System.Reflection;
using EPiServer.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using TechnicalDogsbody.Optimizely.ContentSeeder.Options;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers;
using TechnicalDogsbody.Optimizely.ContentSeeder.Providers.Bogus;
using Xunit;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Providers;

// The site's own list bound attributes, matched by name.
[AttributeUsage(AttributeTargets.Property)]
public sealed class MinElementsAttribute(int min) : Attribute
{
    public int Min { get; } = min;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class MaxElementsAttribute(int max) : Attribute
{
    public int Max { get; } = max;
}

public class BogusBlockListTests
{
    public class FooterColumnBlock : BlockData
    {
        public virtual string? Heading { get; set; }
        public virtual IList<FooterLinkBlock>? Links { get; set; }
    }

    public class FooterLinkBlock : BlockData
    {
        public virtual string? Text { get; set; }
    }

    public class NestingBlock : BlockData
    {
        public virtual string? Title { get; set; }
        public virtual IList<NestingBlock>? Children { get; set; }
    }

    private sealed class FooterSettings
    {
        [MinElements(1)]
        [MaxElements(4)]
        public IList<FooterColumnBlock>? FooterColumns { get; set; }

        [MinElements(5)]
        [MaxElements(6)]
        public IList<FooterLinkBlock>? ManyLinks { get; set; }

        public IList<NestingBlock>? Tree { get; set; }

        public IList<string>? Tags { get; set; }
    }

    private static BogusContentGenerationProvider CreateSut()
    {
        var factory = Mock.Of<IImageContentFactory>(
            f => f.CreateImageAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())
                 == Task.FromResult(ContentHandle.Empty));
        var opts = OptionsFactory.Create(new ContentSeederOptions { MasterLanguage = "en" });
        var map = new DefaultBogusPropertyMap(OptionsFactory.Create(new BogusGeneratorMapOptions()));

        return new BogusContentGenerationProvider(factory, map, opts, NullLogger<BogusContentGenerationProvider>.Instance,
            richText: new XhtmlRichTextConverter(), contentModel: new OptimizelyContentModelAdapter());
    }

    private static async Task<FooterSettings> PopulateAsync()
    {
        var content = new FooterSettings();
        await CreateSut().PopulateAsync(content, typeof(FooterSettings).GetProperties(), "en");
        return content;
    }

    [Fact]
    public async Task BlockList_IsFilledWithPopulatedBlocks()
    {
        var content = await PopulateAsync();

        Assert.NotNull(content.FooterColumns);
        Assert.InRange(content.FooterColumns!.Count, 1, 3);
        Assert.All(content.FooterColumns, c => Assert.False(string.IsNullOrWhiteSpace(c.Heading)));
    }

    [Fact]
    public async Task BlockList_InsideListItem_IsFilled()
    {
        var content = await PopulateAsync();

        Assert.All(content.FooterColumns!, c =>
        {
            Assert.NotNull(c.Links);
            Assert.NotEmpty(c.Links!);
            Assert.All(c.Links!, l => Assert.False(string.IsNullOrWhiteSpace(l.Text)));
        });
    }

    [Fact]
    public async Task BlockList_RespectsMinElements()
    {
        var content = await PopulateAsync();

        Assert.NotNull(content.ManyLinks);
        Assert.Equal(5, content.ManyLinks!.Count);
    }

    [Fact]
    public async Task BlockList_OfItsOwnType_StopsNesting()
    {
        var content = await PopulateAsync();

        int depth = 0;
        IList<NestingBlock>? level = content.Tree;
        while (level is { Count: > 0 })
        {
            depth++;
            level = level[0].Children;
        }

        Assert.Equal(3, depth);
    }

    [Fact]
    public async Task ListOfNonBlocks_IsLeftAlone()
    {
        var content = await PopulateAsync();

        Assert.Null(content.Tags);
    }

    private sealed class BoundsSample
    {
        public IList<FooterLinkBlock>? None { get; set; }

        [MaxElements(2)]
        public IList<FooterLinkBlock>? MaxTwo { get; set; }

        [MinLength(4)]
        [MaxLength(10)]
        public IList<FooterLinkBlock>? DataAnnotations { get; set; }

        [MaxElements(0)]
        public IList<FooterLinkBlock>? ZeroMax { get; set; }
    }

    [Theory]
    [InlineData(nameof(BoundsSample.None), 1, 3)]
    [InlineData(nameof(BoundsSample.MaxTwo), 1, 2)]
    [InlineData(nameof(BoundsSample.DataAnnotations), 4, 4)]
    [InlineData(nameof(BoundsSample.ZeroMax), 1, 3)]
    public void BlockListBounds_ReadsPropertyAttributes(string property, int min, int max)
    {
        var prop = typeof(BoundsSample).GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!;

        Assert.Equal((min, max), BogusContentGenerationProvider.BlockListBounds(prop));
    }

    [Theory]
    [InlineData(typeof(IList<FooterLinkBlock>), true)]
    [InlineData(typeof(IEnumerable<FooterLinkBlock>), true)]
    [InlineData(typeof(List<FooterLinkBlock>), true)]
    [InlineData(typeof(IList<string>), false)]
    [InlineData(typeof(FooterLinkBlock), false)]
    [InlineData(typeof(ContentArea), false)]
    public void Adapter_RecognisesBlockLists(Type propertyType, bool expected)
    {
        IContentModelAdapter adapter = new OptimizelyContentModelAdapter();

        Assert.Equal(expected, adapter.TryGetBlockListItemType(propertyType, out _));
    }
}

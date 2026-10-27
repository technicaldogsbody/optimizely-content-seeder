using EPiServer.VisualBuilder;
using Xunit;
using TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Definitions;

public class SeedTreeValidationTests
{
    // ── test content types ───────────────────────────────────────────────────

    private sealed class FakeExperience : ExperienceData { }
    private sealed class FakeSection : SectionData { }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static BlockDefinition Block(string id) => new(id, typeof(object));

    private static SeedNode Node(params ContentAreaAssignment[] assignments)
        => new(typeof(object), contentAreaAssignments: [.. assignments]);

    private static ContentAreaAssignment Assignment(string prop, params string[] blockIds)
        => new(prop, [.. blockIds]);

    private static SectionDefinition Section(string id, Type? type = null, IReadOnlyList<RowDefinition>? rows = null)
        => new(id, type ?? typeof(object), rows: rows);

    private static ExperienceSeedNode ExperienceNode(params SectionSlotAssignment[] sections)
        => new(typeof(FakeExperience), sections: [.. sections]);

    private static SectionSlotAssignment SlotAssignment(string sectionId) => new(sectionId);

    // ── happy paths ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_EmptyTree_DoesNotThrow()
    {
        var tree = new ExperienceSeedTree();

        var ex = Record.Exception(tree.Validate);

        Assert.Null(ex);
    }

    [Fact]
    public void Validate_BlocksWithUniqueIds_DoesNotThrow()
    {
        var tree = new ExperienceSeedTree(blocks: [Block("hero"), Block("promo")]);

        var ex = Record.Exception(tree.Validate);

        Assert.Null(ex);
    }

    [Fact]
    public void Validate_NodeWithValidBlockIdReference_DoesNotThrow()
    {
        var tree = new ExperienceSeedTree(
            blocks: [Block("hero")],
            nodes: [Node(Assignment("HeroArea", "hero"))]);

        var ex = Record.Exception(tree.Validate);

        Assert.Null(ex);
    }

    [Fact]
    public void Validate_ZeroCountNode_DoesNotThrow()
    {
        var tree = new ExperienceSeedTree(nodes: [new ExperienceSeedNode(typeof(object), count: 0)]);

        var ex = Record.Exception(tree.Validate);

        Assert.Null(ex);
    }

    // ── duplicate block IDs ──────────────────────────────────────────────────

    [Fact]
    public void Validate_DuplicateBlockId_ThrowsInvalidOperationException()
    {
        var tree = new ExperienceSeedTree(blocks: [Block("hero"), Block("hero")]);

        var ex = Assert.Throws<InvalidOperationException>(tree.Validate);

        Assert.Contains("hero", ex.Message);
    }

    [Fact]
    public void Validate_DuplicateBlockId_MessageContainsBlockId()
    {
        var tree = new ExperienceSeedTree(blocks: [Block("promo"), Block("promo")]);

        var ex = Assert.Throws<InvalidOperationException>(tree.Validate);

        Assert.Contains("promo", ex.Message, StringComparison.Ordinal);
    }

    // ── unknown block references ─────────────────────────────────────────────

    [Fact]
    public void Validate_ContentAreaReferencesUnknownBlockId_ThrowsInvalidOperationException()
    {
        var tree = new ExperienceSeedTree(
            blocks: [Block("hero")],
            nodes: [Node(Assignment("MainArea", "unknown-block"))]);

        var ex = Assert.Throws<InvalidOperationException>(tree.Validate);

        Assert.Contains("unknown-block", ex.Message);
    }

    [Fact]
    public void Validate_NestedNodeReferencesUnknownBlockId_ThrowsInvalidOperationException()
    {
        var child = new ExperienceSeedNode(
            typeof(object),
            contentAreaAssignments: [Assignment("PromoArea", "missing")]);

        var parent = new ExperienceSeedNode(typeof(object), children: [child]);

        var tree = new ExperienceSeedTree(nodes: [parent]);

        var ex = Assert.Throws<InvalidOperationException>(tree.Validate);

        Assert.Contains("missing", ex.Message);
    }

    [Fact]
    public void Validate_ContentAreaReferencesUnknownBlockId_MessageContainsPropertyName()
    {
        var tree = new ExperienceSeedTree(
            blocks: [Block("hero")],
            nodes: [Node(Assignment("HeroContentArea", "bad-id"))]);

        var ex = Assert.Throws<InvalidOperationException>(tree.Validate);

        Assert.Contains("HeroContentArea", ex.Message);
    }

    // ── BlockDefinition construction guards ──────────────────────────────────

    [Fact]
    public void BlockDefinition_NullId_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => new BlockDefinition(null!, typeof(object)));
    }

    [Fact]
    public void BlockDefinition_WhitespaceId_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => new BlockDefinition("   ", typeof(object)));
    }

    [Fact]
    public void BlockDefinition_NullType_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new BlockDefinition("valid-id", null!));
    }

    // ── SeedNode construction guards ─────────────────────────────────────────

    [Fact]
    public void SeedNode_NullType_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ExperienceSeedNode(null!));
    }

    [Fact]
    public void SeedNode_NegativeCount_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExperienceSeedNode(typeof(object), count: -1));
    }

    [Fact]
    public void SeedNode_ZeroCount_IsValid()
    {
        var node = new ExperienceSeedNode(typeof(object), count: 0);

        Assert.Equal(0, node.Count);
    }

    // ── Section happy paths ──────────────────────────────────────────────────

    [Fact]
    public void Validate_SectionsWithUniqueIds_DoesNotThrow()
    {
        var tree = new ExperienceSeedTree(sections: [Section("hero"), Section("cta")]);

        var ex = Record.Exception(tree.Validate);

        Assert.Null(ex);
    }

    [Fact]
    public void Validate_ExperienceNodeWithValidSectionReference_DoesNotThrow()
    {
        var tree = new ExperienceSeedTree(
            sections: [Section("hero")],
            nodes: [ExperienceNode(SlotAssignment("hero"))]);

        var ex = Record.Exception(tree.Validate);

        Assert.Null(ex);
    }

    [Fact]
    public void Validate_BlockAndSectionWithDifferentIds_DoesNotThrow()
    {
        var tree = new ExperienceSeedTree(blocks: [Block("hero-block")], sections: [Section("hero-section")]);

        var ex = Record.Exception(tree.Validate);

        Assert.Null(ex);
    }

    // ── duplicate / colliding IDs ─────────────────────────────────────────────

    [Fact]
    public void Validate_DuplicateSectionId_ThrowsInvalidOperationException()
    {
        var tree = new ExperienceSeedTree(sections: [Section("hero"), Section("hero")]);

        var ex = Assert.Throws<InvalidOperationException>(tree.Validate);

        Assert.Contains("hero", ex.Message);
    }

    [Fact]
    public void Validate_SectionIdCollidesWithBlockId_ThrowsInvalidOperationException()
    {
        var tree = new ExperienceSeedTree(blocks: [Block("hero")], sections: [Section("hero")]);

        var ex = Assert.Throws<InvalidOperationException>(tree.Validate);

        Assert.Contains("hero", ex.Message);
    }

    // ── unknown section references ────────────────────────────────────────────

    [Fact]
    public void Validate_SectionSlotReferencesUnknownSectionId_ThrowsInvalidOperationException()
    {
        var tree = new ExperienceSeedTree(nodes: [ExperienceNode(SlotAssignment("unknown-section"))]);

        var ex = Assert.Throws<InvalidOperationException>(tree.Validate);

        Assert.Contains("unknown-section", ex.Message);
    }

    [Fact]
    public void Validate_NestedExperienceNodeReferencesUnknownSectionId_ThrowsInvalidOperationException()
    {
        var child = new ExperienceSeedNode(typeof(FakeExperience), sections: [SlotAssignment("missing")]);
        var parent = new ExperienceSeedNode(typeof(object), children: [child]);

        var tree = new ExperienceSeedTree(nodes: [parent]);

        var ex = Assert.Throws<InvalidOperationException>(tree.Validate);

        Assert.Contains("missing", ex.Message);
    }

    // ── Sections only valid on ExperienceData ─────────────────────────────────

    [Fact]
    public void Validate_SectionsOnNonExperienceNode_ThrowsInvalidOperationException()
    {
        var tree = new ExperienceSeedTree(
            sections: [Section("hero")],
            nodes: [new ExperienceSeedNode(typeof(object), sections: [SlotAssignment("hero")])]);

        var ex = Assert.Throws<InvalidOperationException>(tree.Validate);

        Assert.Contains("ExperienceData", ex.Message);
    }

    // ── SectionDefinition construction guards ─────────────────────────────────

    [Fact]
    public void SectionDefinition_NullId_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => new SectionDefinition(null!, typeof(object)));
    }

    [Fact]
    public void SectionDefinition_WhitespaceId_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => new SectionDefinition("   ", typeof(object)));
    }

    [Fact]
    public void SectionDefinition_NullType_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new SectionDefinition("valid-id", null!));
    }

    [Fact]
    public void SectionDefinition_RowsOnNonSectionDataType_ThrowsInvalidOperationException()
    {
        var rows = new RowDefinition[] { new(columns: [new ColumnDefinition()]) };

        var ex = Assert.Throws<InvalidOperationException>(
            () => new SectionDefinition("hero", typeof(object), rows: rows));

        Assert.Contains("hero", ex.Message);
    }

    [Fact]
    public void SectionDefinition_RowsOnSectionDataType_DoesNotThrow()
    {
        var rows = new RowDefinition[] { new(columns: [new ColumnDefinition()]) };

        var ex = Record.Exception(() => new SectionDefinition("hero", typeof(FakeSection), rows: rows));

        Assert.Null(ex);
    }

    [Fact]
    public void SectionDefinition_NoRows_AllowsAnyType()
    {
        var ex = Record.Exception(() => new SectionDefinition("hero", typeof(object)));

        Assert.Null(ex);
    }

    // ── SectionSlotAssignment construction guards ─────────────────────────────

    [Fact]
    public void SectionSlotAssignment_NullSectionId_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => new SectionSlotAssignment(null!));
    }

    [Fact]
    public void SectionSlotAssignment_WhitespaceSectionId_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(() => new SectionSlotAssignment("   "));
    }

    // ── ElementDefinition / ColumnDefinition / RowDefinition defaults ─────────

    [Fact]
    public void ElementDefinition_NullType_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ElementDefinition(null!));
    }

    [Fact]
    public void ColumnDefinition_NullElements_DefaultsToEmptyList()
    {
        var column = new ColumnDefinition();

        Assert.Empty(column.Elements);
    }

    [Fact]
    public void RowDefinition_NullColumns_DefaultsToEmptyList()
    {
        var row = new RowDefinition();

        Assert.Empty(row.Columns);
    }

    // ── RepeatingElementDefinition ────────────────────────────────────────────

    [Fact]
    public void RepeatingElementDefinition_NullType_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RepeatingElementDefinition(null!));
    }

    [Fact]
    public void RepeatingElementDefinition_NonBlockType_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => new RepeatingElementDefinition(typeof(string)));
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(-1, 3)]
    public void RepeatingElementDefinition_MinColumnsBelowOne_Throws(int min, int max)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RepeatingElementDefinition(typeof(FakeSection), minColumns: min, maxColumns: max));
    }

    [Fact]
    public void RepeatingElementDefinition_MaxBelowMin_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RepeatingElementDefinition(typeof(FakeSection), minColumns: 4, maxColumns: 2));
    }

    [Fact]
    public void RepeatingElementDefinition_ValidRange_StoresBounds()
    {
        var definition = new RepeatingElementDefinition(typeof(FakeSection), minColumns: 1, maxColumns: 6);

        Assert.Equal(1, definition.MinColumns);
        Assert.Equal(6, definition.MaxColumns);
    }

    [Fact]
    public void SectionDefinition_RepeatingElementOnLeafSection_Throws()
    {
        var rows = new ISectionRowSource[] { new RepeatingElementDefinition(typeof(FakeSection)) };

        Assert.Throws<InvalidOperationException>(() => new SectionDefinition("hero", typeof(string), rows: rows));
    }
}

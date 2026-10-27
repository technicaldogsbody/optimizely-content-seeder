namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Something that contributes one or more rows to a structural <see cref="SectionDefinition"/>:
/// either an explicit <see cref="RowDefinition"/> or a generator such as
/// <see cref="RepeatingElementDefinition"/>. Rows are emitted in declaration order.
/// </summary>
public interface ISectionRowSource
{
    /// <summary>Passes this source to the visitor's method for its own type.</summary>
    void Accept(ISectionRowSourceVisitor visitor);
}

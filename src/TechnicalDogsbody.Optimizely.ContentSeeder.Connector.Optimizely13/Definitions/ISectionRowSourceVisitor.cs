namespace TechnicalDogsbody.Optimizely.ContentSeeder.Definitions;

/// <summary>
/// Operations over the <see cref="ISectionRowSource"/> types. A new row source type adds a method here, so
/// every operation that handles row sources is forced to decide what the new type means.
/// </summary>
public interface ISectionRowSourceVisitor
{
    /// <summary>
    /// Visits a row definition.
    /// </summary>
    void Visit(RowDefinition row);

    /// <summary>
    /// Visits a repeating element definition.
    /// </summary>
    void Visit(RepeatingElementDefinition repeating);
}

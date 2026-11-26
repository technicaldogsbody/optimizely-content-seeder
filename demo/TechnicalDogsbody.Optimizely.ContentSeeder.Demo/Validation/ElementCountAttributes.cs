using System.Collections;
using System.ComponentModel.DataAnnotations;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Demo.Validation;

// A site's own list bound attributes, like those many Optimizely sites define. The CMS checks them when content is
// published, and ContentSeeder reads them (by name) to decide how many block list items to create.

/// <summary>A list property must have at least this many items.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MinElementsAttribute(int min) : ValidationAttribute($"Add at least {min} item(s).")
{
    public int Min { get; } = min;

    public override bool IsValid(object? value) => value is ICollection list && list.Count >= Min;
}

/// <summary>A list property may have at most this many items.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MaxElementsAttribute(int max) : ValidationAttribute($"Add no more than {max} item(s).")
{
    public int Max { get; } = max;

    public override bool IsValid(object? value) => value is not ICollection list || list.Count <= Max;
}

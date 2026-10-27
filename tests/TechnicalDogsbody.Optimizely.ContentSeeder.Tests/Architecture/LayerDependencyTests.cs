using System.Reflection;
using TechnicalDogsbody.Optimizely.ContentSeeder.Abstractions;
using Xunit;

namespace TechnicalDogsbody.Optimizely.ContentSeeder.Tests.Architecture;

/// <summary>
/// Keeps the layers of the one assembly pointing inwards: inner layers (definitions, abstractions) must not reach
/// into the outer ones (UI, extensions, infrastructure). Checked by reflecting over each type's members and base
/// types, so no architecture-testing package is needed.
/// </summary>
public class LayerDependencyTests
{
    private const string Root = "TechnicalDogsbody.Optimizely.ContentSeeder";

    // For each layer, the layers its types must not reference.
    private static readonly Dictionary<string, string[]> Forbidden = new()
    {
        ["Definitions"] = ["Abstractions", "Application", "Orchestration", "Infrastructure", "UI", "Providers", "Extensions"],
        ["Abstractions"] = ["Application", "Orchestration", "Infrastructure", "UI", "Providers", "Extensions"],
        ["Application"] = ["UI", "Infrastructure", "Providers", "Extensions"],
        ["Orchestration"] = ["UI", "Infrastructure", "Extensions"],
        ["Infrastructure"] = ["UI", "Extensions"],
    };

    private static readonly Assembly Assembly = typeof(ISeedTracker).Assembly;

    [Fact]
    public void InnerLayersDoNotReachIntoOuterLayers()
    {
        var violations = new List<string>();
        int scanned = 0;

        foreach (var type in Assembly.GetTypes())
        {
            string? from = LayerOf(type);
            if (from is null || !Forbidden.TryGetValue(from, out var banned))
                continue;

            scanned++;
            foreach (var target in ReferencedTypes(type).Distinct())
            {
                string? to = LayerOf(target);
                if (to is not null && banned.Contains(to))
                    violations.Add($"{from}: {type} -> {to}: {target}");
            }
        }

        Assert.True(scanned > 50, $"Only {scanned} types were checked; the scan is probably not finding the layers.");
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    // The layer is the first namespace segment under the root namespace. Types in the root namespace have none.
    private static string? LayerOf(Type type)
    {
        var ns = type.Namespace;
        if (ns is null || !ns.StartsWith(Root + ".", StringComparison.Ordinal))
            return null;

        return ns[(Root.Length + 1)..].Split('.')[0];
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                   | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var direct = new List<Type>();
        if (type.BaseType is { } baseType)
            direct.Add(baseType);
        direct.AddRange(type.GetInterfaces());
        direct.AddRange(type.GetFields(flags).Select(f => f.FieldType));
        direct.AddRange(type.GetProperties(flags).Select(p => p.PropertyType));
        foreach (var ctor in type.GetConstructors(flags))
            direct.AddRange(ctor.GetParameters().Select(p => p.ParameterType));
        foreach (var method in type.GetMethods(flags))
        {
            direct.Add(method.ReturnType);
            direct.AddRange(method.GetParameters().Select(p => p.ParameterType));
        }

        return direct.SelectMany(Unwrap);
    }

    // A type reaches every type it is built from: element types of arrays, and the arguments of generics.
    private static IEnumerable<Type> Unwrap(Type type)
    {
        if (type.HasElementType)
            return Unwrap(type.GetElementType()!);
        if (type.IsGenericType)
            return new[] { type.GetGenericTypeDefinition() }.Concat(type.GetGenericArguments().SelectMany(Unwrap));
        return [type];
    }
}

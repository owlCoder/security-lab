using System.Reflection;
using Platform.Access.Contracts;

namespace Platform.Access.Tests;

[TestFixture]
[Property("RequirementId", "ACC-05")]
public class ArchitectureTests
{
    private static readonly Assembly AccessAssembly = typeof(IAccessDecision).Assembly;

    private static readonly string[] InnerNamespaces =
    [
        "Platform.Access.Contracts",
        "Platform.Access.Domain",
        "Platform.Access.Application",
    ];

    [Test]
    public void ACC05_InnerLayers_DoNotDependOnAspNetOrInfrastructure()
    {
        var offenders = new List<string>();

        foreach (var type in AccessAssembly.GetTypes().Where(IsInnerLayer))
        {
            foreach (var used in UsedTypes(type))
            {
                var ns = used.Namespace ?? string.Empty;
                if (ns.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
                    ns.StartsWith("Platform.Access.Infrastructure", StringComparison.Ordinal) ||
                    ns.StartsWith("Platform.Access.Composition", StringComparison.Ordinal))
                {
                    offenders.Add($"{type.FullName} -> {used.FullName}");
                }
            }
        }

        Assert.That(offenders, Is.Empty, string.Join(Environment.NewLine, offenders));
    }

    [Test]
    public void ACC05_ExactlyOneAccessDecisionContractExists()
    {
        var contracts = AccessAssembly.GetTypes().Where(t => t.IsInterface && t.Name == "IAccessDecision").ToArray();

        Assert.That(contracts.Length, Is.EqualTo(1));
        Assert.That(contracts[0].GetMethods().Select(m => m.Name).ToArray(), Is.EqualTo(new[] { "DecideAsync" }));
    }

    private static bool IsInnerLayer(Type type) =>
        InnerNamespaces.Any(ns => (type.Namespace ?? string.Empty).StartsWith(ns, StringComparison.Ordinal));

    private static IEnumerable<Type> UsedTypes(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                 BindingFlags.Static | BindingFlags.DeclaredOnly;

        if (type.BaseType is not null) yield return type.BaseType;
        foreach (var i in type.GetInterfaces()) yield return i;
        foreach (var f in type.GetFields(all)) yield return f.FieldType;
        foreach (var p in type.GetProperties(all)) yield return p.PropertyType;
        foreach (var c in type.GetConstructors(all))
            foreach (var p in c.GetParameters()) yield return p.ParameterType;
        foreach (var m in type.GetMethods(all))
        {
            yield return m.ReturnType;
            foreach (var p in m.GetParameters()) yield return p.ParameterType;
        }
    }
}

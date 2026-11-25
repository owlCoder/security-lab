using System.Text.RegularExpressions;
using Platform.Access.Application.Services;
using Platform.Access.Domain.Models;

namespace Platform.Access.Application.Rules;

internal static class SnapshotAttributes
{
    public const int MaxNameLength = 64;
    public const int MaxAllowedValues = 64;

    private static readonly Regex NamePattern =
        new(@"\A[A-Za-z][A-Za-z0-9_.]{0,63}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string RequireName(string? name)
    {
        if (name is null || !NamePattern.IsMatch(name))
        {
            throw new AccessConfigurationException(
                $"Attribute name must match ^[A-Za-z][A-Za-z0-9_.]{{0,{MaxNameLength - 1}}}$.");
        }

        return name;
    }

   public static object? Read(EvaluationSnapshot snapshot, string name)
    {
        var attributes = snapshot.Resource?.Attributes;
        if (attributes is null || !attributes.TryGetValue(name, out var attribute) || !attribute.IsPresent)
        {
            return null;
        }

        return attribute.Value;
    }

    public static IReadOnlyList<object> RequireAllowedValues(IEnumerable<object?> values, string owner)
    {
        ArgumentNullException.ThrowIfNull(values);

        var copy = values.ToArray();
        if (copy.Length == 0 || copy.Length > MaxAllowedValues)
        {
            throw new AccessConfigurationException($"{owner} requires between 1 and {MaxAllowedValues} values.");
        }

        if (copy.Any(value => !ValueComparison.IsSupportedConstant(value)))
        {
            throw new AccessConfigurationException($"{owner} contains a null or unsupported constant.");
        }

        return copy!;
    }
}

public sealed class AttrEqualsCondition : ICondition<EvaluationSnapshot>
{
    private readonly string name;
    private readonly object expected;

    public AttrEqualsCondition(string attribute, object expected)
    {
        name = SnapshotAttributes.RequireName(attribute);
        if (!ValueComparison.IsSupportedConstant(expected))
        {
            throw new AccessConfigurationException("AttrEquals requires a non-null constant of a supported type.");
        }

        this.expected = expected;
    }

    public Tri Evaluate(EvaluationSnapshot context) =>
        ValueComparison.AreEqual(SnapshotAttributes.Read(context, name), expected);
}

public sealed class AttrInCondition : ICondition<EvaluationSnapshot>
{
    private readonly string name;
    private readonly IReadOnlyList<object> allowed;

    public AttrInCondition(string attribute, IEnumerable<object?> allowed)
    {
        name = SnapshotAttributes.RequireName(attribute);
        this.allowed = SnapshotAttributes.RequireAllowedValues(allowed, nameof(AttrInCondition));
    }

    public Tri Evaluate(EvaluationSnapshot context) =>
        ValueComparison.IsIn(SnapshotAttributes.Read(context, name), allowed!);
}

public sealed class StatusInCondition : ICondition<EvaluationSnapshot>
{
    private readonly IReadOnlyList<object> allowed;

    public StatusInCondition(IEnumerable<string> allowedStatuses)
    {
        ArgumentNullException.ThrowIfNull(allowedStatuses);
        var statuses = allowedStatuses.ToArray();

        if (statuses.Any(status => string.IsNullOrEmpty(status)))
        {
            throw new AccessConfigurationException("StatusIn does not accept a null or empty status.");
        }

        allowed = SnapshotAttributes.RequireAllowedValues(statuses, nameof(StatusInCondition));
    }

    public Tri Evaluate(EvaluationSnapshot context) =>
        ValueComparison.IsIn(context.Resource?.Status, allowed!);
}

public sealed class AttributePresentCondition : ICondition<EvaluationSnapshot>
{
    private readonly string name;

    public AttributePresentCondition(string attribute) => name = SnapshotAttributes.RequireName(attribute);

    public Tri Evaluate(EvaluationSnapshot context)
    {
        var attributes = context.Resource?.Attributes;
        if (attributes is null)
        {
            return Tri.Unknown;
        }

        return attributes.TryGetValue(name, out var attribute) && attribute.IsPresent && attribute.Value is not null
            ? Tri.True
            : Tri.False;
    }
}

public sealed class NowBeforeCondition : ICondition<EvaluationSnapshot>
{
    private readonly string name;

    public NowBeforeCondition(string attribute) => name = SnapshotAttributes.RequireName(attribute);

    public Tri Evaluate(EvaluationSnapshot context) =>
        SnapshotAttributes.Read(context, name) is DateTimeOffset limit
            ? ValueComparison.IsBefore(context.Now, limit)
            : Tri.Unknown;
}

public sealed class NowAfterCondition : ICondition<EvaluationSnapshot>
{
    private readonly string name;

    public NowAfterCondition(string attribute) => name = SnapshotAttributes.RequireName(attribute);

    public Tri Evaluate(EvaluationSnapshot context) =>
        SnapshotAttributes.Read(context, name) is DateTimeOffset limit
            ? ValueComparison.IsAtOrAfter(context.Now, limit)
            : Tri.Unknown;
}

public sealed class LessThanCondition : ICondition<EvaluationSnapshot>
{
    private readonly string left;
    private readonly string? rightAttribute;
    private readonly object? limit;

    private LessThanCondition(string left, string? rightAttribute, object? limit)
    {
        this.left = left;
        this.rightAttribute = rightAttribute;
        this.limit = limit;
    }

    public static LessThanCondition Attributes(string leftAttribute, string rightAttribute) =>
        new(SnapshotAttributes.RequireName(leftAttribute), SnapshotAttributes.RequireName(rightAttribute), null);

    public static LessThanCondition Value(string attribute, object limit)
    {
        if (!ValueComparison.IsOrderedConstant(limit))
        {
            throw new AccessConfigurationException("LessThan requires an integer or time constant.");
        }

        return new(SnapshotAttributes.RequireName(attribute), null, limit);
    }

    public Tri Evaluate(EvaluationSnapshot context) =>
        ValueComparison.IsLessThan(
            SnapshotAttributes.Read(context, left),
            rightAttribute is null ? limit : SnapshotAttributes.Read(context, rightAttribute));
}

public static class SnapshotConditions
{
    public static ICondition<EvaluationSnapshot> AttrEquals(string attribute, object expected) =>
        new AttrEqualsCondition(attribute, expected);

    public static ICondition<EvaluationSnapshot> AttrIn(string attribute, params object[] allowed) =>
        new AttrInCondition(attribute, allowed);

    public static ICondition<EvaluationSnapshot> StatusIn(params string[] allowedStatuses) =>
        new StatusInCondition(allowedStatuses);

    public static ICondition<EvaluationSnapshot> AttributePresent(string attribute) =>
        new AttributePresentCondition(attribute);

    public static ICondition<EvaluationSnapshot> NowBefore(string attribute) =>
        new NowBeforeCondition(attribute);

    public static ICondition<EvaluationSnapshot> NowAfter(string attribute) =>
        new NowAfterCondition(attribute);

    public static ICondition<EvaluationSnapshot> LessThan(string leftAttribute, string rightAttribute) =>
        LessThanCondition.Attributes(leftAttribute, rightAttribute);

    public static ICondition<EvaluationSnapshot> LessThanValue(string attribute, long limit) =>
        LessThanCondition.Value(attribute, limit);
}

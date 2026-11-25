using Platform.Access.Application.Services;

namespace Platform.Access.Application.Rules;

public sealed class AllOfCondition<TContext> : ICondition<TContext>
{
    private readonly ICondition<TContext>[] children;

    public AllOfCondition(IEnumerable<ICondition<TContext>> children)
    {
        this.children = ConditionChildren.Copy(children, nameof(AllOfCondition<TContext>));
    }

    public IReadOnlyList<ICondition<TContext>> Children => children;

    public Tri Evaluate(TContext context)
    {
        var unknown = false;
        foreach (var child in children)
        {
            var result = TriLogic.Normalize(child.Evaluate(context));
            if (result == Tri.False)
            {
                return Tri.False;
            }

            unknown |= result == Tri.Unknown;
        }

        return unknown ? Tri.Unknown : Tri.True;
    }
}

public sealed class AnyOfCondition<TContext> : ICondition<TContext>
{
    private readonly ICondition<TContext>[] children;

    public AnyOfCondition(IEnumerable<ICondition<TContext>> children)
    {
        this.children = ConditionChildren.Copy(children, nameof(AnyOfCondition<TContext>));
    }

    public IReadOnlyList<ICondition<TContext>> Children => children;

    public Tri Evaluate(TContext context)
    {
        var unknown = false;
        foreach (var child in children)
        {
            var result = TriLogic.Normalize(child.Evaluate(context));
            if (result == Tri.True)
            {
                return Tri.True;
            }

            unknown |= result == Tri.Unknown;
        }

        return unknown ? Tri.Unknown : Tri.False;
    }
}

public sealed class NotCondition<TContext> : ICondition<TContext>
{
    private readonly ICondition<TContext> inner;

    public NotCondition(ICondition<TContext> inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        this.inner = inner;
    }

    public IReadOnlyList<ICondition<TContext>> Children => [inner];

    public Tri Evaluate(TContext context) => TriLogic.Not(inner.Evaluate(context));
}

public static class Conditions
{
    public static ICondition<TContext> AllOf<TContext>(params ICondition<TContext>[] children) =>
        new AllOfCondition<TContext>(children);

    public static ICondition<TContext> AnyOf<TContext>(params ICondition<TContext>[] children) =>
        new AnyOfCondition<TContext>(children);

    public static ICondition<TContext> Not<TContext>(ICondition<TContext> inner) =>
        new NotCondition<TContext>(inner);
}

internal static class ConditionChildren
{
    public static ICondition<TContext>[] Copy<TContext>(IEnumerable<ICondition<TContext>> children, string owner)
    {
        ArgumentNullException.ThrowIfNull(children);

        var copy = children.ToArray();
        if (copy.Length == 0)
        {
            throw new AccessConfigurationException($"{owner} must contain at least one condition.");
        }

        if (copy.Any(child => child is null))
        {
            throw new AccessConfigurationException($"{owner} must not contain a null condition.");
        }

        return copy;
    }
}

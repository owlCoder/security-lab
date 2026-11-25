using Platform.Access.Application.Rules;

namespace Platform.Access.Tests.Support;

public sealed class ConstCondition(Tri value) : ICondition<int>
{
    public Tri Evaluate(int context) => value;
}

public sealed class ThrowingCondition : ICondition<int>
{
    public Tri Evaluate(int context) => throw new InvalidOperationException("condition failed");
}

public sealed class NullChildrenCondition : ICondition<int>
{
    public Tri Evaluate(int context) => Tri.True;
    public IReadOnlyList<ICondition<int>> Children => null!;
}

public static class RuleTestData
{
    public static ICondition<int> True() => new ConstCondition(Tri.True);
    public static ICondition<int> False() => new ConstCondition(Tri.False);
    public static ICondition<int> Unknown() => new ConstCondition(Tri.Unknown);

    public static ICondition<int> Of(Tri value) => new ConstCondition(value);

    public static Rule<int> Rule(string id, Tri value) => new(id, Of(value));
}

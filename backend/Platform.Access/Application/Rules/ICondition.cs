namespace Platform.Access.Application.Rules;

public interface ICondition<TContext>
{
    Tri Evaluate(TContext context);
    IReadOnlyList<ICondition<TContext>> Children => Array.Empty<ICondition<TContext>>();
}

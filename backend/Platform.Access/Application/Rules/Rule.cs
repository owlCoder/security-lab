using System.Text.RegularExpressions;
using Platform.Access.Application.Services;

namespace Platform.Access.Application.Rules;

public sealed class Rule<TContext>
{
    private static readonly Regex IdPattern =
        new(@"\A[a-z0-9._:-]{1,64}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public Rule(string id, ICondition<TContext> condition)
    {
        if (id is null || !IdPattern.IsMatch(id))
        {
            throw new AccessConfigurationException(
                "Rule id must match ^[a-z0-9._:-]{1,64}$.");
        }

        if (condition is null)
        {
            throw new AccessConfigurationException($"Rule '{id}' must have a condition.");
        }

        Id = id;
        Condition = condition;
    }

    public string Id { get; }

    public ICondition<TContext> Condition { get; }
}

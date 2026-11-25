namespace Platform.Access.Application.Rules;

public static class ValueComparison
{
    public static Tri AreEqual(object? actual, object? expected)
    {
        if (actual is null || expected is null)
        {
            return Tri.Unknown;
        }

        if (TryGetInteger(actual, out var leftNumber) && TryGetInteger(expected, out var rightNumber))
        {
            return FromBool(leftNumber == rightNumber);
        }

        if (actual.GetType() != expected.GetType())
        {
            return Tri.Unknown;
        }

        return actual switch
        {
            string left => FromBool(string.Equals(left, (string)expected, StringComparison.Ordinal)),
            bool left => FromBool(left == (bool)expected),
            Guid left => FromBool(left == (Guid)expected),
            DateTimeOffset left => FromBool(left == (DateTimeOffset)expected),
            Enum left => FromBool(left.Equals(expected)),
            _ => Tri.Unknown
        };
    }

    public static Tri IsIn(object? actual, IEnumerable<object?> allowed)
    {
        ArgumentNullException.ThrowIfNull(allowed);

        var result = actual is null ? Tri.Unknown : Tri.False;
        foreach (var candidate in allowed)
        {
            result = TriLogic.Or(result, AreEqual(actual, candidate));
            if (result == Tri.True)
            {
                return Tri.True;
            }
        }

        return result;
    }

    public static Tri IsLessThan(object? actual, object? limit)
    {
        if (actual is null || limit is null)
        {
            return Tri.Unknown;
        }

        if (TryGetInteger(actual, out var leftNumber) && TryGetInteger(limit, out var rightNumber))
        {
            return FromBool(leftNumber < rightNumber);
        }

        if (actual is DateTimeOffset leftTime && limit is DateTimeOffset rightTime)
        {
            return FromBool(leftTime < rightTime);
        }

        return Tri.Unknown;
    }

    public static Tri IsBefore(DateTimeOffset? now, DateTimeOffset? limit) =>
        now is null || limit is null ? Tri.Unknown : FromBool(now.Value < limit.Value);

    public static Tri IsAtOrAfter(DateTimeOffset? now, DateTimeOffset? limit) =>
        now is null || limit is null ? Tri.Unknown : FromBool(now.Value >= limit.Value);

    public static bool IsSupportedConstant(object? value) =>
        value is string or bool or Guid or DateTimeOffset or Enum ||
        (value is not null && TryGetInteger(value, out _));

    public static bool IsOrderedConstant(object? value) =>
        value is DateTimeOffset || (value is not null && TryGetInteger(value, out _));

    private static Tri FromBool(bool value) => value ? Tri.True : Tri.False;

    private static bool TryGetInteger(object value, out long number)
    {
        switch (value)
        {
            case sbyte v: number = v; return true;
            case byte v: number = v; return true;
            case short v: number = v; return true;
            case ushort v: number = v; return true;
            case int v: number = v; return true;
            case uint v: number = v; return true;
            case long v: number = v; return true;
            default: number = 0; return false;
        }
    }
}

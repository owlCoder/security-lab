namespace Platform.Access.Application.Rules;

public enum Tri : byte
{
    Unknown = 0,
    False = 1,
    True = 2
}

public static class TriLogic
{
    public static Tri Normalize(Tri value) =>
        value is Tri.False or Tri.True ? value : Tri.Unknown;

    public static Tri And(Tri left, Tri right)
    {
        left = Normalize(left);
        right = Normalize(right);

        if (left == Tri.False || right == Tri.False)
        {
            return Tri.False;
        }

        return left == Tri.True && right == Tri.True ? Tri.True : Tri.Unknown;
    }

    public static Tri Or(Tri left, Tri right)
    {
        left = Normalize(left);
        right = Normalize(right);

        if (left == Tri.True || right == Tri.True)
        {
            return Tri.True;
        }

        return left == Tri.False && right == Tri.False ? Tri.False : Tri.Unknown;
    }

    public static Tri Not(Tri value) => Normalize(value) switch
    {
        Tri.True => Tri.False,
        Tri.False => Tri.True,
        _ => Tri.Unknown
    };
}

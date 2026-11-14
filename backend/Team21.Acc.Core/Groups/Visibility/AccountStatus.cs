namespace Team21.Acc.Core.Groups.Visibility;

/// <summary>State of an account. Only Active may see materials.</summary>
public enum AccountStatus
{
    Active = 1,
    Inactive = 2,
    Blocked = 3
}

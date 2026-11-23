namespace Platform.Identity.Enums;

public enum FederatedLoginOutcome
{
    /// <summary>The (issuer, subject) pair was already linked; the same account is returned.</summary>
    Linked,

    /// <summary>First login: linked to the existing account with the same verified e-mail.</summary>
    NewlyLinked,

    /// <summary>Missing or too long issuer/subject.</summary>
    InvalidIdentity,

    /// <summary>The provider did not confirm the e-mail, so it cannot be used for the first link.</summary>
    EmailNotVerified,

    /// <summary>No internal account has this e-mail. Accounts are created only by an administrator (IDN-01).</summary>
    NoMatchingAccount,

    /// <summary>The account already has a different identity from the same issuer.</summary>
    AccountAlreadyLinked
}
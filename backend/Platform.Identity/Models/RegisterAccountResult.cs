using Platform.Identity.Enums;

namespace Platform.Identity.Models;

public sealed record RegisterAccountResult(
    RegistrationOutcome Outcome,
    Guid? AccountId,
    IReadOnlyList<string> Errors)
{
    public static RegisterAccountResult Created(Guid id) =>
        new(RegistrationOutcome.Created, id, []);

    public static RegisterAccountResult Taken() =>
        new(RegistrationOutcome.IdentifierTaken, null, []);

    public static RegisterAccountResult Invalid(params string[] errors) =>
        new(RegistrationOutcome.InvalidInput, null, errors);
}
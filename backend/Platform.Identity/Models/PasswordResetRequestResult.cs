namespace Platform.Identity.Models;

public sealed record PasswordResetRequestResult(bool IsAccepted, string Message)
{
    public static PasswordResetRequestResult Accepted() =>
        new(true, "Ako nalog sa unetom adresom postoji, dobicete uputstva za oporavak pristupa.");

    public static PasswordResetRequestResult InvalidEmail() =>
        new(false, "Email adresa nije validna.");
}

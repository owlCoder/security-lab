namespace Platform.Identity.Models;

public sealed record EmailConfirmationGrant(string Email, string Token);

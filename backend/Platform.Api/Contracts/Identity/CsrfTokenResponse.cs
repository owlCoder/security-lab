namespace Platform.Api.Contracts.Identity;

/// <summary>US-15: the request token the frontend sends back in the <c>X-CSRF-TOKEN</c> header.</summary>
public sealed record CsrfTokenResponse(string Token);

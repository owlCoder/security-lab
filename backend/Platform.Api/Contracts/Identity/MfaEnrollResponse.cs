namespace Platform.Api.Contracts.Identity;

public record MfaEnrollResponse(string Uri)
{
    // US-22: the otpauth:// URI carries the secret, and the generated record ToString() would print it into any
    // log that formats the response. Same approach as MfaVerifyRequest and LoginRequest.
    public override string ToString() => "MfaEnrollResponse { *** }";
}

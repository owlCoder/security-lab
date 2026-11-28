using Platform.Api.Models.Identity;

namespace Platform.Api.Interfaces.Identity;

public interface ILoginService
{
    /// <param name="sourceAddress">IDN-22: the client address the server sees for this request; the caller
    /// (the HTTP layer) passes it, and it is recorded only in the auth.login audit event.</param>
    public Task<LoginResult> ExecuteAsync(string? email, string? password, string correlationId,
        string? sourceAddress = null, CancellationToken ct = default);
}
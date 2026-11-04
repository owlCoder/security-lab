using Microsoft.Extensions.Configuration;
using Platform.Access.Contracts;
using Platform.Access.Domain.Models;

namespace Platform.Identity.Services;

public sealed class CreateAccountPolicy(IConfiguration configuration) : IAccessPolicy
{
    public const string ActionId = "identity.account.create";

    public string Id => "identity.account.create.admin-only";
    public string Version => "v1";

    public ValueTask<bool> EvaluateAsync(
        AccessRequest request,
        CancellationToken cancellationToken = default)
    {
        var admins = configuration.GetSection("Identity:AdminAccountIds").Get<string[]>() ?? [];
        var allowed = request.Subject is not null
            && admins.Contains(request.Subject.Id, StringComparer.Ordinal);

        return ValueTask.FromResult(allowed);
    }
}
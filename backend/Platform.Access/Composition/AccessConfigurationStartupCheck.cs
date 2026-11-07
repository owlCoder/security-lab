using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.Access.Application.Options;
using Platform.Access.Application.Services;
using Platform.Access.Contracts;
using Platform.Access.Infrastructure.Policies;

namespace Platform.Access.Composition;

internal sealed class AccessConfigurationStartupCheck(
    IActionCatalog catalog,
    InMemoryAccessPolicyRegistry registry,
    AccessConfigurationOptions options,
    ILogger<AccessConfigurationStartupCheck> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var uncovered = AccessConfigurationValidator.Validate(catalog, registry.RegisteredActions, options);

        if (uncovered.Count > 0)
        {
            logger.LogWarning(
                "{Count} catalog action(s) have no access policy and will be denied (NoPolicy): {Actions}",
                uncovered.Count,
                string.Join(", ", uncovered));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

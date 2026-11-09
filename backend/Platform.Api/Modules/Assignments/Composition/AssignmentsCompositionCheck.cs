using Platform.Assignments.Contracts;

namespace Platform.Api.Modules.Assignments.Composition;

// ASN-04 / ASN-06 TIM 15 (celina 5)

/// <summary>
/// Eksplicitno ograničenje umesto tihog odbijanja: pri pokretanju hosta proverava koji portovi
/// Assignments modula su i dalje podrazumevane fail-closed implementacije (član 1: skladišta,
/// član 4: autorizacija) i to prijavljuje u log. Van Development okruženja prijava je na nivou Error,
/// da operater vidi da ASN-04/ASN-06 rute zato odbijaju sve zahteve (404 / 403).
/// Ne ruši pokretanje: ostali moduli platforme rade nezavisno od dodela.
/// </summary>
public sealed class AssignmentsCompositionCheck(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    ILogger<AssignmentsCompositionCheck> logger) : IHostedService
{
    public static readonly EventId NotConfiguredEvent = new(41503, "AssignmentsPortsNotConfigured");

    private static readonly Type[] Ports =
    [
        typeof(IAssignmentRepository),
        typeof(IAttemptStore),
        typeof(IDeadlineExtensionStore),
        typeof(IStartAttemptAuthorization),
        typeof(IDeadlineExtensionAuthorization),
        typeof(ICourseEnrollmentCheck),
    ];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> notConfigured;
        try
        {
            using var scope = scopeFactory.CreateScope();
            notConfigured = GetNotConfiguredPorts(scope.ServiceProvider);
        }
        catch (Exception exception)
        {
            logger.LogError(NotConfiguredEvent,
                "Assignments composition check failed with {ExceptionType}.", exception.GetType().Name);
            return Task.CompletedTask;
        }

        if (notConfigured.Count == 0)
        {
            return Task.CompletedTask;
        }

        var level = environment.IsDevelopment() ? LogLevel.Warning : LogLevel.Error;
        logger.Log(level, NotConfiguredEvent,
            "Assignments ports not configured: {Ports}. ASN-04 and ASN-06 deny every request until real adapters are registered.",
            string.Join(", ", notConfigured));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Imena portova čija je razrešena implementacija i dalje fail-closed podrazumevana.</summary>
    public static IReadOnlyList<string> GetNotConfiguredPorts(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Ports
            .Where(port => services.GetService(port) is null or IFailClosedAssignmentPort)
            .Select(port => port.Name)
            .ToList();
    }
}

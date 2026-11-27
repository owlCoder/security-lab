using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Audit.Contracts;
using Platform.LabVisibility.Contracts;

namespace Platform.LabVisibility.Infrastructure.Auditing;

// KT2: trajni audit kroz AUD modul (tim 12), umesto zapisa u aplikacioni log.
// Singleton, kao CentralAccessAuditWriter: ITrustedAuditWriter je scoped, pa se uzima iz novog scope-a.
// Ako AUD ne sačuva zapis, ishod naše radnje se ne menja (odbijanje ostaje odbijanje),
// ali se diže tehnički alarm bez sadržaja (CONTRACTS.md, obaveze proizvođača 2 i 3).
public sealed class AuditWriterLabVisibilityAudit(
    IServiceScopeFactory scopes,
    ILogger<AuditWriterLabVisibilityAudit> logger) : ILabVisibilityAudit
{
    public static readonly EventId AuditLost = new(6201, "LabVisibilityAuditLost");

    public async ValueTask WriteAsync(LabVisibilityAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        // Nepoznata radnja ne ide u AUD; alarm ne ispisuje njen tekst.
        if (!LabVisibilityAuditMapping.IsKnownAction(auditEvent.Action))
        {
            LogLost("UnknownAction", auditEvent, LabVisibilityAuditMapping.ToContext(auditEvent).CorrelationId);
            return;
        }

        var draft = LabVisibilityAuditMapping.ToDraft(auditEvent);
        var context = LabVisibilityAuditMapping.ToContext(auditEvent);

        AuditWriteResult result;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var writer = scope.ServiceProvider.GetRequiredService<ITrustedAuditWriter>();

            // Radnja je već izvršena: otkazan HTTP zahtev ne sme da izgubi zapis (obaveza 3).
            result = await writer.WriteAsync(draft, context, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogLost("Exception", auditEvent, context.CorrelationId);
            return;
        }

        if (!result.IsRecorded)
        {
            LogLost($"{result.Status}/{result.RejectionCode}", auditEvent, context.CorrelationId);
        }
    }

    // Samo radnja, laboratorija, status i correlation ID. Nikad sadržaj, ključ ni odgovor.
    private void LogLost(string status, LabVisibilityAuditEvent auditEvent, string correlationId) =>
        logger.LogError(
            AuditLost,
            "LabVisibility audit was not saved. Action: {Action}, Lab: {LabId}, Status: {Status}, CorrelationId: {CorrelationId}",
            LabVisibilityAuditMapping.IsKnownAction(auditEvent.Action) ? auditEvent.Action : "unknown",
            auditEvent.LabId,
            status,
            correlationId);
}

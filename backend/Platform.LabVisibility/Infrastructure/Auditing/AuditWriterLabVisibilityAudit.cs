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
    ILabPublicContentReader labs,
    ILogger<AuditWriterLabVisibilityAudit> logger) : ILabVisibilityAudit
{
    public static readonly EventId AuditLost = new(6201, "LabVisibilityAuditLost");
    public static readonly EventId StudentViewServed = new(6202, "LabVisibilityStudentViewServed");

    // Sopstveni rok za ceo upis (kurs + AUD), nezavisan od klijenta (S11-04, tačka 5).
    // Spor ili zaglavljen AUD ne sme da zaustavi prikaz laboratorije ni čitanje ključa.
    public static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(1);

    public async ValueTask WriteAsync(LabVisibilityAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var context = LabVisibilityAuditMapping.ToContext(auditEvent);

        // Nepoznata radnja ne ide u AUD; alarm ne ispisuje njen tekst.
        if (!LabVisibilityAuditMapping.IsKnownAction(auditEvent.Action))
        {
            LogLost("UnknownAction", auditEvent, context.CorrelationId);
            return;
        }

        // Obim trajnog zapisa: uspešan studentski prikaz je najčešći, normalan zahtev bez bezbednosne
        // vrednosti, pa ide samo u aplikacioni log. Odbijanja, greške, pregled i ključ idu u AUD.
        if (LabVisibilityAuditMapping.IsSuccessfulStudentView(auditEvent))
        {
            logger.LogInformation(
                StudentViewServed,
                "LabVisibility student view served. Lab: {LabId}, CorrelationId: {CorrelationId}",
                auditEvent.LabId,
                context.CorrelationId);
            return;
        }

        // Radnja je već izvršena: otkazan HTTP zahtev ne sme da izgubi zapis (obaveza 3),
        // zato se koristi sopstveni rok, a ne token klijenta.
        using var timeout = new CancellationTokenSource(WriteTimeout);

        AuditWriteResult result;
        try
        {
            // WaitAsync štiti i od writer-a koji ignoriše token.
            result = await WriteDurableAsync(auditEvent, context, timeout.Token)
                .AsTask()
                .WaitAsync(WriteTimeout, CancellationToken.None);
        }
        catch (Exception exception) when (exception is TimeoutException || timeout.IsCancellationRequested)
        {
            LogLost("Timeout", auditEvent, context.CorrelationId);
            return;
        }
        catch (Exception)
        {
            // Tekst izuzetka se ne ispisuje: provajder može da u njega stavi osetljiv podatak.
            LogLost("Exception", auditEvent, context.CorrelationId);
            return;
        }

        if (!result.IsRecorded)
        {
            LogLost($"{result.Status}/{result.RejectionCode}", auditEvent, context.CorrelationId);
        }
    }

    private async ValueTask<AuditWriteResult> WriteDurableAsync(
        LabVisibilityAuditEvent auditEvent,
        AuditContext context,
        CancellationToken cancellationToken)
    {
        // Kurs iz pouzdanog izvora: isti kurs po kom se proverava članstvo, nikad iz zahteva.
        // Nepoznata laboratorija (npr. anonimno odbijanje za nepostojeći ID) ostaje bez kursa.
        var content = await labs.GetAsync(auditEvent.LabId, cancellationToken);
        var draft = LabVisibilityAuditMapping.ToDraft(auditEvent, content?.CourseId);

        await using var scope = scopes.CreateAsyncScope();
        var writer = scope.ServiceProvider.GetRequiredService<ITrustedAuditWriter>();
        return await writer.WriteAsync(draft, context, cancellationToken);
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

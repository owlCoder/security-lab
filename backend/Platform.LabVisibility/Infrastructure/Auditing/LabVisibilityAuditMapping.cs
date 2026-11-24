using System.Text;
using System.Text.RegularExpressions;
using Platform.Audit.Contracts;
using Platform.LabVisibility.Contracts;

namespace Platform.LabVisibility.Infrastructure.Auditing;

// Prevodi naš LabVisibilityAuditEvent u zapis AUD modula (lab.visibility.accessed).
// Događaj nema polje za sadržaj, pa ni zapis ne može da ga dobije (AUD-03).
public static partial class LabVisibilityAuditMapping
{
    // Pravilo AUD modula za correlation ID: 1–64 znaka, slova, cifre, '-', '_', '.'.
    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex ValidCorrelationId();

    [GeneratedRegex("^[a-z][a-z0-9_]{0,31}$")]
    private static partial Regex ValidReasonCode();

    public static AuditEventDraft ToDraft(LabVisibilityAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var (outcome, reasonCode) = MapOutcome(auditEvent.Outcome);

        var details = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["action"] = auditEvent.Action,
            ["laboratoryId"] = auditEvent.LabId.ToString()
        };

        if (reasonCode is not null)
        {
            details["reasonCode"] = reasonCode;
        }

        return new AuditEventDraft(
            AuditEventTypes.LabVisibilityAccessed,
            outcome,
            Details: details);
    }

    // Akter je subjekat koji je naš kod već dobio iz sesije ili od odluke o pristupu, nikad iz zahteva.
    public static AuditContext ToContext(LabVisibilityAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var actor = auditEvent.ActorId switch
        {
            null => AuditActor.Anonymous,
            EvaluatorPrincipal.Id => AuditActor.ForService(EvaluatorPrincipal.ServiceActorId),
            var id when Guid.TryParse(id, out var userId) && userId != Guid.Empty => AuditActor.ForUser(userId),
            // Subjekat koji nije ni korisnik ni evaluator: zapis bez identiteta, nikad tuđi ID.
            _ => AuditActor.Anonymous
        };

        return new AuditContext(actor, NormalizeCorrelationId(auditEvent.CorrelationId));
    }

    // Naši ishodi su ranije bili slobodni stringovi ("Allow", "allowed", "NotFound", "denied"...).
    // Ovde se svode na tri ishoda AUD modula; sve što nije uspeh ni odbijanje je Failure.
    private static (AuditOutcome Outcome, string? ReasonCode) MapOutcome(string? outcome)
    {
        var code = ToSnakeCase(outcome);

        return code switch
        {
            "allow" or "allowed" or "ok" or "saved" or "success" => (AuditOutcome.Success, null),
            "deny" or "denied" => (AuditOutcome.Denied, "forbidden"),
            "unauthenticated" => (AuditOutcome.Denied, "unauthenticated"),
            "published" => (AuditOutcome.Denied, "lab_published"),
            _ when ValidReasonCode().IsMatch(code) => (AuditOutcome.Failure, code),
            _ => (AuditOutcome.Failure, "unknown")
        };
    }

    private static string ToSnakeCase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 4);
        foreach (var character in value.Trim())
        {
            if (char.IsUpper(character) && builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }

            builder.Append(character is '-' or ' ' ? '_' : char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    private static string NormalizeCorrelationId(string? correlationId)
    {
        if (correlationId is not null && ValidCorrelationId().IsMatch(correlationId))
        {
            return correlationId;
        }

        // npr. TraceIdentifier "0HN7…:00000001": nedozvoljeni znakovi postaju '-'.
        var cleaned = new string((correlationId ?? string.Empty)
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-')
            .Take(64)
            .ToArray());

        return cleaned.Length > 0 ? cleaned : Guid.NewGuid().ToString("N");
    }
}

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

    // Samo četiri radnje modula idu u AUD. Nepoznata radnja se ne upisuje, ni kao tekst (AUD-03).
    private static readonly HashSet<string> KnownActions = new(LabVisibilityActions.All, StringComparer.Ordinal);

    // Fiksno mapiranje poznatih ishoda modula na ishod AUD-a i reasonCode.
    // U zapis nikad ne ide tekst ishoda koji je poslao kod: oblik stringa ne garantuje da ne nosi poverljiv podatak.
    private static readonly Dictionary<string, (AuditOutcome Outcome, string? ReasonCode)> KnownOutcomes =
        new(StringComparer.Ordinal)
        {
            ["allow"] = (AuditOutcome.Success, null),
            ["allowed"] = (AuditOutcome.Success, null),
            ["saved"] = (AuditOutcome.Success, null),
            ["deny"] = (AuditOutcome.Denied, ReasonCodes.Forbidden),
            ["denied"] = (AuditOutcome.Denied, ReasonCodes.Forbidden),
            ["unauthenticated"] = (AuditOutcome.Denied, ReasonCodes.Unauthenticated),
            ["published"] = (AuditOutcome.Denied, ReasonCodes.LabPublished),
            ["not_found"] = (AuditOutcome.Failure, ReasonCodes.NotFound),
            ["invalid"] = (AuditOutcome.Failure, ReasonCodes.Invalid)
        };

    public static class ReasonCodes
    {
        public const string Forbidden = "forbidden";
        public const string Unauthenticated = "unauthenticated";
        public const string LabPublished = "lab_published";
        public const string NotFound = "not_found";
        public const string Invalid = "invalid";
        public const string UnknownOutcome = "unknown_outcome";
    }

    public static bool IsKnownAction(string? action) => action is not null && KnownActions.Contains(action);

    public static AuditEventDraft ToDraft(LabVisibilityAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        if (!IsKnownAction(auditEvent.Action))
        {
            // Poruka namerno ne sadrži vrednost radnje.
            throw new ArgumentException("Unknown LabVisibility action.", nameof(auditEvent));
        }

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

    // Ishodi modula su stringovi ("Allow", "allowed", "NotFound", "denied"...). Normalizacija služi
    // samo za pretragu u fiksnoj tabeli; sve što nije u tabeli je Failure sa unknown_outcome.
    private static (AuditOutcome Outcome, string? ReasonCode) MapOutcome(string? outcome) =>
        KnownOutcomes.TryGetValue(ToSnakeCase(outcome), out var mapped)
            ? mapped
            : (AuditOutcome.Failure, ReasonCodes.UnknownOutcome);

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

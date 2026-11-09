using System.Globalization;
using System.Text.RegularExpressions;
using Platform.Assignments.Domain.ValueObjects;
using Platform.Audit.Contracts;

namespace Platform.Api.Modules.Assignments.Audit;

// ASN-04 / ASN-06 TIM 15 (celina 5)

/// <summary>
/// Predloženi tipovi događaja za Audit katalog (tim 12). Dok ih tim 12 ne doda u
/// <see cref="AuditEventCatalog"/>, Audit modul ih odbija sa <c>UnknownEventType</c>, pa se
/// odbijanja (Denied) beleže kroz postojeći <c>access.denied</c> (vidi <see cref="AssignmentAuditWriter"/>).
/// </summary>
public static class AssignmentAuditEventTypes
{
    public const string AttemptStarted = "assignment.attempt.started";
    public const string DeadlineExtended = "assignment.deadline.extended";
}

/// <summary>
/// Čisti nacrt (allowlist) i prevodi ga u <see cref="AuditEventDraft"/> Audit modula.
/// Javno je da bi član 1 isti nacrt upisao u svojoj transakciji
/// (<c>IDeadlineExtensionStore.SaveAsync</c> + <c>ITransactionalAuditWriter</c>).
/// </summary>
public static partial class AssignmentAuditDraftMapper
{
    public const string PolicyDeniedReason = "policy_denied";

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,31}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ReasonCodePattern();

    /// <summary>
    /// Allowlist: nepoznata radnja ili ishod bacaju (greška u našem kodu), a nevažeći ID-jevi i
    /// kodovi se izbacuju. Odbačena vrednost se nikad ne kopira u zapis, log ni izuzetak.
    /// </summary>
    public static AssignmentAuditDraft Sanitize(AssignmentAuditDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (!AssignmentAuditActions.IsKnown(draft.Action))
        {
            throw new ArgumentException("Unknown assignment audit action.", nameof(draft));
        }

        if (!Enum.IsDefined(draft.Outcome))
        {
            throw new ArgumentException("Unknown assignment audit outcome.", nameof(draft));
        }

        var isSuccess = draft.Outcome == AssignmentAuditOutcome.Success;

        return draft with
        {
            CourseId = PrincipalId.IsValid(draft.CourseId) ? draft.CourseId : null,
            StudentId = PrincipalId.IsValid(draft.StudentId) ? draft.StudentId : null,
            // reasonCode samo uz Denied i Failure, kao u Audit katalogu.
            ReasonCode = !isSuccess && IsValidReasonCode(draft.ReasonCode) ? draft.ReasonCode : null,
            AttemptId = isSuccess && draft.AttemptId is Guid id && id != Guid.Empty ? id : null,
            PreviousDueAtUtc = draft.PreviousDueAtUtc?.ToUniversalTime(),
            NewDueAtUtc = draft.NewDueAtUtc?.ToUniversalTime(),
        };
    }

    /// <summary>Glavni (predloženi) događaj: <c>assignment.attempt.started</c> ili <c>assignment.deadline.extended</c>.</summary>
    public static AuditEventDraft ToPlatformDraft(AssignmentAuditDraft draft)
    {
        var safe = Sanitize(draft);
        var details = new Dictionary<string, string>(StringComparer.Ordinal);

        if (safe.ReasonCode is not null)
        {
            details["reasonCode"] = safe.ReasonCode;
        }

        if (safe.Action == AssignmentAuditActions.StartAttempt)
        {
            if (safe.AttemptId is Guid attemptId)
            {
                details["attemptId"] = attemptId.ToString("D");
            }

            // Akter je sam student, pa resurs nije potreban.
            return new AuditEventDraft(
                AssignmentAuditEventTypes.AttemptStarted,
                ToAuditOutcome(safe.Outcome),
                ToCourseGuid(safe.CourseId),
                Resource: null,
                Details: details);
        }

        if (safe.PreviousDueAtUtc is DateTimeOffset previous)
        {
            details["previousDueAt"] = FormatUtc(previous);
        }

        if (safe.NewDueAtUtc is DateTimeOffset next)
        {
            details["newDueAt"] = FormatUtc(next);
        }

        // Resurs je student kome se menja rok (nije akter).
        return new AuditEventDraft(
            AssignmentAuditEventTypes.DeadlineExtended,
            ToAuditOutcome(safe.Outcome),
            ToCourseGuid(safe.CourseId),
            safe.StudentId is null ? null : new AuditResourceRef(AuditResourceTypes.Account, safe.StudentId),
            details);
    }

    /// <summary>
    /// Rezervni zapis za odbijanje dok predloženi tipovi nisu u katalogu: postojeći
    /// <c>access.denied</c> sa detaljima <c>action</c> i <c>denialReason</c>.
    /// </summary>
    public static AuditEventDraft ToAccessDeniedDraft(AssignmentAuditDraft draft)
    {
        var safe = Sanitize(draft);

        if (safe.Outcome != AssignmentAuditOutcome.Denied)
        {
            throw new ArgumentException("Only a denied action maps to access.denied.", nameof(draft));
        }

        return new AuditEventDraft(
            AuditEventTypes.AccessDenied,
            AuditOutcome.Denied,
            ToCourseGuid(safe.CourseId),
            Resource: null,
            Details: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["action"] = safe.Action,
                ["denialReason"] = PolicyDeniedReason,
            });
    }

    public static AuditOutcome ToAuditOutcome(AssignmentAuditOutcome outcome) => outcome switch
    {
        AssignmentAuditOutcome.Success => AuditOutcome.Success,
        AssignmentAuditOutcome.Denied => AuditOutcome.Denied,
        AssignmentAuditOutcome.Failure => AuditOutcome.Failure,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome))
    };

    /// <summary>
    /// Otvorena tačka iz ASN-06-contracts.md: <c>Assignment.CourseId</c> je string, a
    /// <c>AuditEventDraft.CourseId</c> je Guid. Kurs se šalje samo kada je string ispravan Guid;
    /// inače se izostavlja (nikad se ne izmišlja Guid).
    /// </summary>
    public static Guid? ToCourseGuid(string? courseId) =>
        Guid.TryParse(courseId, out var id) && id != Guid.Empty ? id : null;

    /// <summary>ISO 8601 UTC sa sufiksom Z, format <c>UtcTimestamp</c> iz Audit kataloga.</summary>
    public static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static bool IsValidReasonCode(string? value) =>
        value is not null && ReasonCodePattern().IsMatch(value);
}

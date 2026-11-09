using System.Text.RegularExpressions;
using Platform.Audit.Contracts;
using ApiIdentityContext = Platform.Api.Interfaces.Identity.IIdentityContext;

namespace Platform.Api.Modules.Assignments.Audit;

// ASN-04 / ASN-06 TIM 15 (celina 5)

/// <summary>
/// Akter = korisnik iz sesije koju je proverio <c>SessionCheckMiddleware</c> (IDN-02);
/// korelacioni ID = ID koji je <c>CorrelationMiddleware</c> (AUD-04, tim 12) dodelio zahtevu.
/// Bez sesije akter je <see cref="AuditActor.Anonymous"/>; nikad se ne izmišlja.
/// </summary>
public sealed partial class SessionAssignmentAuditContextProvider(
    ApiIdentityContext identityContext,
    ICorrelationContext correlationContext,
    IHttpContextAccessor httpContextAccessor) : IAssignmentAuditContextProvider
{
    // Isto pravilo kao AuditContext.CorrelationId u Platform.Audit.
    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationIdPattern();

    public AuditContext GetContext()
    {
        var identity = identityContext.Current;

        var actor = identity is { IsAuthenticated: true, UserId: Guid userId } && userId != Guid.Empty
            ? AuditActor.ForUser(userId)
            : AuditActor.Anonymous;

        return new AuditContext(actor, ResolveCorrelationId());
    }

    private string ResolveCorrelationId()
    {
        if (IsValidCorrelationId(correlationContext.CorrelationId))
        {
            return correlationContext.CorrelationId!;
        }

        // CorrelationMiddleware postavlja i TraceIdentifier na isti ID; ovo je rezerva.
        var traceIdentifier = httpContextAccessor.HttpContext?.TraceIdentifier;
        if (IsValidCorrelationId(traceIdentifier))
        {
            return traceIdentifier!;
        }

        // Serverski generisan ID, nikad vrednost od klijenta.
        return Guid.NewGuid().ToString("N");
    }

    public static bool IsValidCorrelationId(string? value) =>
        value is not null && CorrelationIdPattern().IsMatch(value);
}

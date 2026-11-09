using Microsoft.AspNetCore.Http;
using Team21.Acc.Core.Interfaces;

namespace Team21.Acc.Api.Services;

/// <summary>
/// Reads the correlation id the platform generated for this request. A value sent by the client
/// is never used, the id ends up in audit records and logs.
/// </summary>
public sealed class HttpCorrelationContext : ICorrelationContext
{
    public HttpCorrelationContext(IHttpContextAccessor accessor) =>
        CorrelationId = accessor.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString();

    public string CorrelationId { get; }
}

using System.Text.Json;
using Platform.Audit.Contracts;
using Platform.Audit.Domain;

namespace Platform.Audit.Infrastructure.Persistence;

/// <summary>Persistence representation; the domain remains independent of EF Core.</summary>
internal sealed class AuditEventRow
{
    public Guid Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public AuditActorKind ActorKind { get; private set; }
    public Guid? ActorId { get; private set; }
    public string EventType { get; private set; } = null!;
    public AuditOutcome Outcome { get; private set; }
    public Guid? CourseId { get; private set; }
    public string? ResourceType { get; private set; }
    public string? ResourceId { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public string DetailsJson { get; private set; } = "{}";
    public int SchemaVersion { get; private set; }

    public static AuditEventRow FromDomain(AuditEvent record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new AuditEventRow
        {
            Id = record.Id,
            OccurredAt = record.OccurredAt,
            ActorKind = record.ActorKind,
            ActorId = record.ActorId,
            EventType = record.EventType,
            Outcome = record.Outcome,
            CourseId = record.CourseId,
            ResourceType = record.ResourceType,
            ResourceId = record.ResourceId,
            CorrelationId = record.CorrelationId,
            DetailsJson = JsonSerializer.Serialize(record.Details),
            SchemaVersion = record.SchemaVersion
        };
    }

    public AuditEvent ToDomain()
    {
        var details = JsonSerializer.Deserialize<Dictionary<string, string>>(DetailsJson)
            ?? throw new InvalidOperationException("Stored audit details must be a JSON object.");
        var resource = ResourceType is null && ResourceId is null
            ? null
            : new AuditResourceRef(
                ResourceType ?? throw new InvalidOperationException("Stored audit resource is incomplete."),
                ResourceId ?? throw new InvalidOperationException("Stored audit resource is incomplete."));

        return AuditEvent.Create(
            Id, OccurredAt,
            new AuditEventDraft(EventType, Outcome, CourseId, resource, details, Id),
            new AuditContext(new AuditActor(ActorKind, ActorId), CorrelationId),
            SchemaVersion);
    }
}

using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Platform.Api.Modules.Assignments.Audit;
using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Models;
using Platform.Assignments.Domain.ValueObjects;
using Platform.Audit.Contracts;

namespace Platform.Api.Modules.Assignments.Persistence;

// ASN-06 TIM 15: individualni rok + audit zapis „oba ili nijedan” (port člana 1).

/// <summary>
/// <see cref="IDeadlineExtensionStore"/> nad PostgreSQL-om. <see cref="SaveAsync"/> radi u jednoj transakciji:
/// <list type="number">
/// <item>zaključa red dodele (<c>FOR UPDATE</c>), pa se produženja iste dodele ne preklapaju;</item>
/// <item>proveri da je trenutni efektivni rok i dalje <c>PreviousDueAtUtc</c>, inače
/// <see cref="InvalidOperationException"/> (istovremena izmena se nikad ne prepisuje tiho);</item>
/// <item>upiše novu verziju roka (stari redovi se ne menjaju, ASN-19);</item>
/// <item>upiše audit zapis kroz <see cref="ITransactionalAuditWriter"/> Audit modula u ISTOJ transakciji
/// (iza savepoint-a); ako ga Audit modul ne primi, isti zapis ide u <c>assignments.audit_outbox</c>,
/// takođe u istoj transakciji (Audit odluka D3: promena + outbox);</item>
/// <item>commit. Ako bilo koji korak padne, nema ni roka ni zapisa.</item>
/// </list>
/// Akter i korelacioni ID dolaze iz <see cref="IAssignmentAuditContextProvider"/> (sesija + middleware),
/// a nacrt je isti allowlist nacrt koji koristi <see cref="AssignmentAuditWriter"/> (bez tajni, AUD-03).
/// </summary>
public sealed class PostgresDeadlineExtensionStore(
    NpgsqlDataSource dataSource,
    ITransactionalAuditWriter transactionalAudit,
    IAssignmentAuditContextProvider auditContext,
    TimeProvider timeProvider,
    ILogger<PostgresDeadlineExtensionStore> logger) : IDeadlineExtensionStore
{
    public static readonly EventId OutboxEvent = new(41505, "AssignmentAuditQueuedInOutbox");

    private const string AuditSavepoint = "assignment_audit";

    public async ValueTask<StudentDeadlineOverride?> GetAsync(
        Guid assignmentId,
        string studentId,
        CancellationToken cancellationToken = default)
    {
        if (assignmentId == Guid.Empty || !PrincipalId.IsValid(studentId))
        {
            return null;
        }

        await using var command = dataSource.CreateCommand("""
            SELECT due_at_utc, previous_due_at_utc, extended_by_id, extended_at_utc
            FROM assignments.deadline_overrides
            WHERE assignment_id = @assignment AND student_id = @student
            ORDER BY version DESC
            LIMIT 1;
            """);
        command.Parameters.AddWithValue("assignment", assignmentId);
        command.Parameters.AddWithValue("student", studentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StudentDeadlineOverride(
            assignmentId,
            studentId,
            reader.GetFieldValue<DateTimeOffset>(0),
            reader.GetFieldValue<DateTimeOffset>(1),
            reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3));
    }

    public async ValueTask SaveAsync(
        StudentDeadlineOverride deadlineOverride,
        DeadlineExtensionAuditEntry auditEntry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deadlineOverride);
        ArgumentNullException.ThrowIfNull(auditEntry);

        if (auditEntry.Outcome != DeadlineExtensionAuditOutcome.Success
            || auditEntry.AssignmentId != deadlineOverride.AssignmentId
            || !string.Equals(auditEntry.StudentId, deadlineOverride.StudentId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The audit entry does not describe this deadline change.", nameof(auditEntry));
        }

        // Nacrt i kontekst se prave PRE transakcije: greška u njima ne sme da ostavi rok bez zapisa.
        var draft = AssignmentAuditDraftMapper.ToPlatformDraft(DeadlineExtensionAuditAdapter.ToDraft(auditEntry));
        draft = draft with { EventId = draft.EventId ?? Guid.NewGuid() };
        var context = auditContext.GetContext();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        DateTimeOffset assignmentDue;
        await using (var lockAssignment = new NpgsqlCommand("""
            SELECT due_at_utc FROM assignments.assignments WHERE id = @assignment FOR UPDATE;
            """, connection, transaction))
        {
            lockAssignment.Parameters.AddWithValue("assignment", deadlineOverride.AssignmentId);
            await using var reader = await lockAssignment.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("Assignment does not exist.");
            }

            assignmentDue = reader.GetFieldValue<DateTimeOffset>(0);
        }

        var latestVersion = 0;
        DateTimeOffset? latestDue = null;
        await using (var latest = new NpgsqlCommand("""
            SELECT version, due_at_utc FROM assignments.deadline_overrides
            WHERE assignment_id = @assignment AND student_id = @student
            ORDER BY version DESC
            LIMIT 1;
            """, connection, transaction))
        {
            latest.Parameters.AddWithValue("assignment", deadlineOverride.AssignmentId);
            latest.Parameters.AddWithValue("student", deadlineOverride.StudentId);
            await using var reader = await latest.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                latestVersion = reader.GetInt32(0);
                latestDue = reader.GetFieldValue<DateTimeOffset>(1);
            }
        }

        // Isto pravilo kao Assignment.GetEffectiveDueAtUtc. PostgreSQL čuva mikrosekunde, pa se
        // poredi na toj preciznosti.
        var currentDue = latestDue is DateTimeOffset individual && individual > assignmentDue ? individual : assignmentDue;
        if (ToMicroseconds(currentDue) != ToMicroseconds(deadlineOverride.PreviousDueAtUtc))
        {
            throw new InvalidOperationException("The deadline was changed concurrently.");
        }

        try
        {
            await using var insert = new NpgsqlCommand("""
                INSERT INTO assignments.deadline_overrides
                    (assignment_id, student_id, version, due_at_utc, previous_due_at_utc, extended_by_id, extended_at_utc)
                VALUES (@assignment, @student, @version, @due, @previous, @by, @at);
                """, connection, transaction);
            insert.Parameters.AddWithValue("assignment", deadlineOverride.AssignmentId);
            insert.Parameters.AddWithValue("student", deadlineOverride.StudentId);
            insert.Parameters.AddWithValue("version", latestVersion + 1);
            insert.Parameters.AddWithValue("due", deadlineOverride.DueAtUtc.ToUniversalTime());
            insert.Parameters.AddWithValue("previous", deadlineOverride.PreviousDueAtUtc.ToUniversalTime());
            insert.Parameters.AddWithValue("by", deadlineOverride.ExtendedById);
            insert.Parameters.AddWithValue("at", deadlineOverride.ExtendedAtUtc.ToUniversalTime());
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.UniqueViolation
                                                      or PostgresErrorCodes.ForeignKeyViolation
                                                      or PostgresErrorCodes.CheckViolation)
        {
            throw new InvalidOperationException("The deadline was changed concurrently.");
        }

        var result = await TryWriteAuditInTransactionAsync(draft, context, transaction, cancellationToken);
        if (!result.IsRecorded)
        {
            await WriteOutboxAsync(connection, transaction, draft, context, result, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        if (!result.IsRecorded)
        {
            // Tehnički alarm bez detalja: zapis NIJE izgubljen, čeka u outbox-u.
            logger.LogWarning(OutboxEvent,
                "Deadline extension audit queued in outbox: status {Status} ({RejectionCode}). CorrelationId: {CorrelationId}",
                result.Status, result.RejectionCode, context.CorrelationId);
        }
    }

    private async ValueTask<AuditWriteResult> TryWriteAuditInTransactionAsync(
        AuditEventDraft draft,
        AuditContext context,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        // Savepoint: neuspeo upis u audit šemu ne sme da pokvari transakciju sa rokom.
        await transaction.SaveAsync(AuditSavepoint, cancellationToken);

        AuditWriteResult result;
        try
        {
            result = await transactionalAudit.WriteAsync(draft, context, transaction, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            result = AuditWriteResult.Unavailable();
        }

        if (result.IsRecorded)
        {
            await transaction.ReleaseAsync(AuditSavepoint, cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(AuditSavepoint, cancellationToken);
        }

        return result;
    }

    private async ValueTask WriteOutboxAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        AuditEventDraft draft,
        AuditContext context,
        AuditWriteResult result,
        CancellationToken cancellationToken)
    {
        var details = draft.Details is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(draft.Details, StringComparer.Ordinal);

        await using var outbox = new NpgsqlCommand("""
            INSERT INTO assignments.audit_outbox
                (id, event_type, outcome, actor_kind, actor_id, correlation_id, course_id, resource_type,
                 resource_id, details, occurred_at_utc, rejection, delivered_at_utc)
            VALUES (@id, @type, @outcome, @actorKind, @actorId, @correlation, @course, @resourceType,
                    @resourceId, @details, @occurred, @rejection, NULL);
            """, connection, transaction);
        outbox.Parameters.AddWithValue("id", draft.EventId!.Value);
        outbox.Parameters.AddWithValue("type", draft.EventType);
        outbox.Parameters.AddWithValue("outcome", draft.Outcome.ToString());
        outbox.Parameters.AddWithValue("actorKind", context.Actor.Kind.ToString());
        outbox.Parameters.Add(new NpgsqlParameter("actorId", NpgsqlDbType.Uuid)
        {
            Value = context.Actor.Id is Guid actorId ? (object)actorId : DBNull.Value
        });
        outbox.Parameters.AddWithValue("correlation", context.CorrelationId);
        outbox.Parameters.Add(new NpgsqlParameter("course", NpgsqlDbType.Uuid)
        {
            Value = draft.CourseId is Guid courseId ? (object)courseId : DBNull.Value
        });
        outbox.Parameters.Add(new NpgsqlParameter("resourceType", NpgsqlDbType.Varchar)
        {
            Value = (object?)draft.Resource?.Type ?? DBNull.Value
        });
        outbox.Parameters.Add(new NpgsqlParameter("resourceId", NpgsqlDbType.Varchar)
        {
            Value = (object?)draft.Resource?.Id ?? DBNull.Value
        });
        outbox.Parameters.Add(new NpgsqlParameter("details", NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(details)
        });
        outbox.Parameters.AddWithValue("occurred", timeProvider.GetUtcNow().ToUniversalTime());
        outbox.Parameters.AddWithValue("rejection",
            result.Status == AuditWriteStatus.Rejected ? result.RejectionCode.ToString() : result.Status.ToString());

        await outbox.ExecuteNonQueryAsync(cancellationToken);
    }

    private static long ToMicroseconds(DateTimeOffset value) => value.UtcTicks / 10;
}

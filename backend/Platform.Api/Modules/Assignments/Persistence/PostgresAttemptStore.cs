using Npgsql;
using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.ValueObjects;

namespace Platform.Api.Modules.Assignments.Persistence;

// ASN-04 TIM 15: atomska provera limita i kreiranje pokušaja (port člana 1, koristi ga član 2).

/// <summary>
/// <see cref="IAttemptStore"/> nad PostgreSQL-om. Provera <c>MaxAttempts</c> i upis pokušaja su u
/// istoj transakciji, pod zaključanim redom brojača za par (dodela, student):
/// <list type="number">
/// <item>red brojača se napravi ako ne postoji (<c>ON CONFLICT DO NOTHING</c>);</item>
/// <item><c>SELECT ... FOR UPDATE</c> zaključa brojač, pa istovremeni zahtevi istog studenta idu jedan po jedan;</item>
/// <item>ponovljen <c>requestId</c> vraća postojeći pokušaj (idempotentnost), i kada je limit već dostignut;</item>
/// <item>iznad limita: <c>null</c>, ništa se ne upisuje i postojeći pokušaji se ne menjaju;</item>
/// <item>inače: novi pokušaj sa rednim brojem <c>used + 1</c> i brojač <c>+1</c>, pa commit.</item>
/// </list>
/// Jedinstveni ključevi u bazi (request, redni broj) su druga linija odbrane ako bi se redosled prekršio.
/// </summary>
public sealed class PostgresAttemptStore(NpgsqlDataSource dataSource, TimeProvider timeProvider) : IAttemptStore
{
    public async ValueTask<Guid?> TryStartAsync(
        Guid assignmentId,
        string studentId,
        string publishedLabRevisionId,
        int maxAttempts,
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        if (assignmentId == Guid.Empty || requestId == Guid.Empty)
        {
            throw new ArgumentException("Assignment and request IDs are required.");
        }

        if (!PrincipalId.IsValid(studentId))
        {
            throw new ArgumentException("Student ID is invalid.", nameof(studentId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(publishedLabRevisionId);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var ensure = new NpgsqlCommand("""
            INSERT INTO assignments.attempt_counters (assignment_id, student_id, attempts_used)
            VALUES (@assignment, @student, 0)
            ON CONFLICT (assignment_id, student_id) DO NOTHING;
            """, connection, transaction))
        {
            AddKey(ensure, assignmentId, studentId);
            await ensure.ExecuteNonQueryAsync(cancellationToken);
        }

        int used;
        await using (var lockCounter = new NpgsqlCommand("""
            SELECT attempts_used FROM assignments.attempt_counters
            WHERE assignment_id = @assignment AND student_id = @student
            FOR UPDATE;
            """, connection, transaction))
        {
            AddKey(lockCounter, assignmentId, studentId);
            used = Convert.ToInt32(await lockCounter.ExecuteScalarAsync(cancellationToken));
        }

        await using (var existing = new NpgsqlCommand("""
            SELECT id FROM assignments.attempts
            WHERE assignment_id = @assignment AND student_id = @student AND request_id = @request;
            """, connection, transaction))
        {
            AddKey(existing, assignmentId, studentId);
            existing.Parameters.AddWithValue("request", requestId);
            if (await existing.ExecuteScalarAsync(cancellationToken) is Guid existingAttemptId)
            {
                await transaction.CommitAsync(cancellationToken);
                return existingAttemptId;
            }
        }

        if (used >= maxAttempts)
        {
            // ASN-04: iznad limita se ništa ne kreira ni ne menja.
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var attemptId = Guid.NewGuid();

        await using (var insert = new NpgsqlCommand("""
            INSERT INTO assignments.attempts
                (id, assignment_id, student_id, request_id, attempt_number, published_lab_revision_id, started_at_utc)
            VALUES (@id, @assignment, @student, @request, @number, @revision, @started);
            UPDATE assignments.attempt_counters
            SET attempts_used = @number
            WHERE assignment_id = @assignment AND student_id = @student;
            """, connection, transaction))
        {
            AddKey(insert, assignmentId, studentId);
            insert.Parameters.AddWithValue("id", attemptId);
            insert.Parameters.AddWithValue("request", requestId);
            insert.Parameters.AddWithValue("number", used + 1);
            insert.Parameters.AddWithValue("revision", publishedLabRevisionId);
            insert.Parameters.AddWithValue("started", timeProvider.GetUtcNow().ToUniversalTime());
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return attemptId;
    }

    private static void AddKey(NpgsqlCommand command, Guid assignmentId, string studentId)
    {
        command.Parameters.AddWithValue("assignment", assignmentId);
        command.Parameters.AddWithValue("student", studentId);
    }
}

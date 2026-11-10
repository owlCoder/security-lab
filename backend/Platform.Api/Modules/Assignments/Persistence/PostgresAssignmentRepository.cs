using Npgsql;
using NpgsqlTypes;
using Platform.Assignments.Contracts;
using Platform.Assignments.Domain.Enums;
using Platform.Assignments.Domain.Models;

namespace Platform.Api.Modules.Assignments.Persistence;

// ASN-04 / ASN-06 TIM 15: PostgreSQL implementacija porta člana 1.

/// <summary>
/// <see cref="IAssignmentRepository"/> nad tabelom <c>assignments.assignments</c>. Redovi se vraćaju
/// kroz konstruktor <see cref="Assignment"/>, pa iste domenske provere važe i za podatke iz baze.
/// </summary>
public sealed class PostgresAssignmentRepository(NpgsqlDataSource dataSource) : IAssignmentRepository
{
    private const string SelectColumns = """
        SELECT id, course_id, published_lab_revision_id, available_from_utc, due_at_utc, max_attempts,
               allow_start_after_deadline, status
        FROM assignments.assignments
        """;

    public async ValueTask<Assignment?> GetByIdAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        if (assignmentId == Guid.Empty)
        {
            return null;
        }

        await using var command = dataSource.CreateCommand(SelectColumns + " WHERE id = @id;");
        command.Parameters.AddWithValue("id", assignmentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async ValueTask<IReadOnlyList<Assignment>> GetForCoursesAsync(
        IReadOnlyCollection<string> courseIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(courseIds);

        var ids = courseIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        await using var command = dataSource.CreateCommand(
            SelectColumns + " WHERE course_id = ANY(@courses) ORDER BY due_at_utc, id;");
        command.Parameters.Add(new NpgsqlParameter("courses", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = ids });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new List<Assignment>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(Read(reader));
        }

        return result;
    }

    public async ValueTask AddAsync(Assignment assignment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        await using var command = dataSource.CreateCommand("""
            INSERT INTO assignments.assignments
                (id, course_id, published_lab_revision_id, available_from_utc, due_at_utc, max_attempts,
                 allow_start_after_deadline, status)
            VALUES (@id, @course, @revision, @from, @due, @max, @late, @status);
            """);
        AddParameters(command, assignment);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException("Assignment already exists.");
        }
    }

    public async ValueTask UpdateAsync(Assignment assignment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        await using var command = dataSource.CreateCommand("""
            UPDATE assignments.assignments
            SET course_id = @course,
                published_lab_revision_id = @revision,
                available_from_utc = @from,
                due_at_utc = @due,
                max_attempts = @max,
                allow_start_after_deadline = @late,
                status = @status
            WHERE id = @id;
            """);
        AddParameters(command, assignment);

        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new KeyNotFoundException("Assignment not found.");
        }
    }

    private static void AddParameters(NpgsqlCommand command, Assignment assignment)
    {
        command.Parameters.AddWithValue("id", assignment.Id);
        command.Parameters.AddWithValue("course", assignment.CourseId);
        command.Parameters.AddWithValue("revision", assignment.PublishedLabRevisionId);
        command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz)
        {
            Value = assignment.AvailableFromUtc is DateTimeOffset from ? (object)from.ToUniversalTime() : DBNull.Value
        });
        command.Parameters.AddWithValue("due", assignment.DueAtUtc.ToUniversalTime());
        command.Parameters.AddWithValue("max", assignment.MaxAttempts);
        command.Parameters.AddWithValue("late", assignment.AllowStartAfterDeadline);
        command.Parameters.AddWithValue("status", (short)assignment.Status);
    }

    private static Assignment Read(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetInt32(5),
            reader.GetBoolean(6),
            (AssignmentStatus)reader.GetInt16(7));
}

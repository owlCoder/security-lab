using Npgsql;

namespace Platform.Api.Modules.Assignments.Persistence;

// ASN-04 / ASN-06 TIM 15: PostgreSQL šema za portove člana 1 (skladišta).
//
// Napisano uz dozvolu člana 1, jer recenzija PR-a traži stvarne adaptere i njihove integracione
// testove. Domen (Assignment, StudentDeadlineOverride) je i dalje njegov; ovde je samo mapiranje.
// Migracije su verzionisane SQL skripte (bez EF modela, da ne bi postojao drugi domenski model):
// svaka se primenjuje jednom, u transakciji, pod advisory lock-om, i upisuje se u schema_versions.

/// <summary>Šema <c>assignments</c>, migracije i razvojni seed.</summary>
public static class AssignmentsDatabase
{
    public const string ConnectionStringName = "Assignments";
    public const string Schema = "assignments";

    // Proizvoljan, stalan ključ: dva procesa ne mogu istovremeno da migriraju.
    private const long MigrationLockKey = 4_150_415;

    /// <summary>Demo dodela za lokalni razvoj (kurs AUT-04/AUT-07 demo kursa).</summary>
    public static readonly Guid DevelopmentAssignmentId = Guid.Parse("a5500000-0000-4000-8000-000000000015");
    public const string DevelopmentCourseId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    internal static readonly IReadOnlyList<(int Version, string Sql)> Migrations =
    [
        (1, """
            CREATE TABLE assignments.assignments (
                id uuid PRIMARY KEY,
                course_id varchar(64) NOT NULL,
                published_lab_revision_id varchar(128) NOT NULL,
                available_from_utc timestamptz NULL,
                due_at_utc timestamptz NOT NULL,
                max_attempts integer NOT NULL,
                allow_start_after_deadline boolean NOT NULL DEFAULT false,
                status smallint NOT NULL,
                CONSTRAINT ck_assignments_max_attempts CHECK (max_attempts >= 1),
                CONSTRAINT ck_assignments_status CHECK (status IN (1, 2)),
                CONSTRAINT ck_assignments_window CHECK (available_from_utc IS NULL OR available_from_utc <= due_at_utc)
            );
            CREATE INDEX ix_assignments_course ON assignments.assignments (course_id);

            -- ASN-04: jedan red po (dodela, student) služi kao brava i brojač; limit se proverava
            -- i povećava u istoj transakciji pod SELECT ... FOR UPDATE.
            CREATE TABLE assignments.attempt_counters (
                assignment_id uuid NOT NULL REFERENCES assignments.assignments (id),
                student_id varchar(64) NOT NULL,
                attempts_used integer NOT NULL,
                PRIMARY KEY (assignment_id, student_id),
                CONSTRAINT ck_attempt_counters_used CHECK (attempts_used >= 0)
            );

            CREATE TABLE assignments.attempts (
                id uuid PRIMARY KEY,
                assignment_id uuid NOT NULL REFERENCES assignments.assignments (id),
                student_id varchar(64) NOT NULL,
                request_id uuid NOT NULL,
                attempt_number integer NOT NULL,
                published_lab_revision_id varchar(128) NOT NULL,
                started_at_utc timestamptz NOT NULL,
                CONSTRAINT ck_attempts_number CHECK (attempt_number >= 1),
                -- Idempotentnost: isti Idempotency-Key istog studenta je uvek isti pokušaj.
                CONSTRAINT ux_attempts_request UNIQUE (assignment_id, student_id, request_id),
                -- Ni trka ni greška u kodu ne mogu da daju dva pokušaja sa istim rednim brojem.
                CONSTRAINT ux_attempts_number UNIQUE (assignment_id, student_id, attempt_number)
            );

            -- ASN-06 / ASN-19: istorija individualnih rokova; red se nikad ne menja, svako
            -- produženje je nova verzija koja pamti prethodni rok i ko ga je promenio.
            CREATE TABLE assignments.deadline_overrides (
                assignment_id uuid NOT NULL REFERENCES assignments.assignments (id),
                student_id varchar(64) NOT NULL,
                version integer NOT NULL,
                due_at_utc timestamptz NOT NULL,
                previous_due_at_utc timestamptz NOT NULL,
                extended_by_id varchar(64) NOT NULL,
                extended_at_utc timestamptz NOT NULL,
                PRIMARY KEY (assignment_id, student_id, version),
                CONSTRAINT ck_deadline_overrides_version CHECK (version >= 1),
                CONSTRAINT ck_deadline_overrides_later CHECK (due_at_utc > previous_due_at_utc)
            );

            -- Audit odluka D3 (change + outbox): uspešno produženje i njegov audit zapis su
            -- „oba ili nijedan”. Kada Audit modul ne može da primi zapis u istoj transakciji
            -- (npr. tip assignment.deadline.extended još nije u katalogu tima 12), zapis ostaje
            -- ovde, u istoj transakciji sa rokom, dok ga relay ne isporuči.
            CREATE TABLE assignments.audit_outbox (
                id uuid PRIMARY KEY,
                event_type varchar(64) NOT NULL,
                outcome varchar(16) NOT NULL,
                actor_kind varchar(16) NOT NULL,
                actor_id uuid NULL,
                correlation_id varchar(64) NOT NULL,
                course_id uuid NULL,
                resource_type varchar(32) NULL,
                resource_id varchar(64) NULL,
                details jsonb NOT NULL,
                occurred_at_utc timestamptz NOT NULL,
                rejection varchar(32) NOT NULL,
                delivered_at_utc timestamptz NULL,
                CONSTRAINT ck_audit_outbox_details CHECK (jsonb_typeof(details) = 'object')
            );
            CREATE INDEX ix_audit_outbox_pending ON assignments.audit_outbox (occurred_at_utc)
                WHERE delivered_at_utc IS NULL;
            """),
    ];

    /// <summary>
    /// Eksplicitna operacija za deploy i testove (kao <c>MigrateAuditDatabaseAsync</c>): web host ne
    /// migrira produkciju automatski; u Development okruženju je poziva Program.cs.
    /// </summary>
    public static async Task MigrateAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(connection, transaction, $"SELECT pg_advisory_xact_lock({MigrationLockKey});", cancellationToken);
        await ExecuteAsync(connection, transaction, """
            CREATE SCHEMA IF NOT EXISTS assignments;
            CREATE TABLE IF NOT EXISTS assignments.schema_versions (
                version integer PRIMARY KEY,
                applied_at_utc timestamptz NOT NULL DEFAULT now()
            );
            """, cancellationToken);

        var applied = new HashSet<int>();
        await using (var read = new NpgsqlCommand("SELECT version FROM assignments.schema_versions;", connection, transaction))
        await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                applied.Add(reader.GetInt32(0));
            }
        }

        foreach (var (version, sql) in Migrations.OrderBy(migration => migration.Version))
        {
            if (applied.Contains(version))
            {
                continue;
            }

            await ExecuteAsync(connection, transaction, sql, cancellationToken);

            await using var record = new NpgsqlCommand(
                "INSERT INTO assignments.schema_versions (version) VALUES (@version);", connection, transaction);
            record.Parameters.AddWithValue("version", version);
            await record.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Najviša verzija koju ovaj kod zna; koristi je test „sve migracije su primenjene”.</summary>
    public static int LatestVersion => Migrations.Max(migration => migration.Version);

    /// <summary>
    /// Samo za lokalni razvoj: jedna demo dodela na demo kursu (max 2 pokušaja, rok za 14 dana).
    /// Idempotentno; ne dira postojeće redove.
    /// </summary>
    public static async Task SeedDevelopmentAsync(
        string connectionString,
        TimeProvider timeProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(timeProvider);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO assignments.assignments
                (id, course_id, published_lab_revision_id, available_from_utc, due_at_utc, max_attempts,
                 allow_start_after_deadline, status)
            VALUES (@id, @course, 'revision-demo-1', NULL, @due, 2, false, 1)
            ON CONFLICT (id) DO NOTHING;
            """, connection);
        command.Parameters.AddWithValue("id", DevelopmentAssignmentId);
        command.Parameters.AddWithValue("course", DevelopmentCourseId);
        command.Parameters.AddWithValue("due", timeProvider.GetUtcNow().AddDays(14).ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

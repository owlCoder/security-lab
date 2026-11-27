using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProtectAuditHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION audit.reject_history_change() RETURNS trigger
                LANGUAGE plpgsql SET search_path = pg_catalog AS $$
                BEGIN
                    RAISE EXCEPTION 'Audit history is append-only.' USING ERRCODE = '55000';
                END;
                $$;
                REVOKE ALL ON FUNCTION audit.reject_history_change() FROM PUBLIC;
                CREATE TRIGGER audit_events_append_only
                BEFORE UPDATE OR DELETE ON audit.audit_events
                FOR EACH STATEMENT EXECUTE FUNCTION audit.reject_history_change();
                ALTER TABLE audit.audit_events ENABLE ALWAYS TRIGGER audit_events_append_only;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER audit_events_append_only ON audit.audit_events;
                DROP FUNCTION audit.reject_history_change();
                """);
        }
    }
}

\set ON_ERROR_STOP on
\getenv audit_runtime_password AUDIT_RUNTIME_PASSWORD
\if :{?audit_runtime_password}
\else
    \echo 'Set AUDIT_RUNTIME_PASSWORD before running this script.'
    \quit 1
\endif

BEGIN;

SELECT 'CREATE ROLE securitylab_audit_runtime LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS'
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'securitylab_audit_runtime')
\gexec

DO $$
DECLARE
    runtime_id oid;
BEGIN
    SELECT oid INTO runtime_id FROM pg_roles WHERE rolname = 'securitylab_audit_runtime';
    IF EXISTS (
        SELECT 1 FROM pg_roles WHERE oid = runtime_id
        AND (rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls OR rolinherit)
    ) OR EXISTS (
        SELECT 1 FROM pg_auth_members WHERE member = runtime_id
    ) OR EXISTS (
        SELECT 1 FROM pg_namespace WHERE nspname = 'audit' AND nspowner = runtime_id
    ) OR EXISTS (
        SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = 'audit' AND c.relowner = runtime_id
    ) THEN
        RAISE EXCEPTION 'Audit runtime role has unexpected ownership, privileges or memberships.';
    END IF;
END $$;

SELECT format('ALTER ROLE securitylab_audit_runtime LOGIN PASSWORD %L', :'audit_runtime_password')
\gexec
\unset audit_runtime_password

SELECT format('GRANT CONNECT ON DATABASE %I TO securitylab_audit_runtime', current_database())
\gexec
REVOKE ALL ON SCHEMA audit FROM PUBLIC;
REVOKE ALL ON SCHEMA audit FROM securitylab_audit_runtime;
GRANT USAGE ON SCHEMA audit TO securitylab_audit_runtime;
REVOKE ALL ON ALL TABLES IN SCHEMA audit FROM PUBLIC;
REVOKE ALL ON ALL TABLES IN SCHEMA audit FROM securitylab_audit_runtime;
GRANT SELECT, INSERT ON TABLE audit.audit_events TO securitylab_audit_runtime;

COMMIT;

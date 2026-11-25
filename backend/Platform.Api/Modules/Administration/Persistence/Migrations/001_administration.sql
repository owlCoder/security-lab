-- ADM-08 owns this schema only. This is not the ACC-01 course catalog.
CREATE SCHEMA IF NOT EXISTS coursework_admin;
CREATE TABLE IF NOT EXISTS coursework_admin.schema_versions (
    version integer PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE TABLE IF NOT EXISTS coursework_admin.course_settings (
    course_id uuid PRIMARY KEY CHECK (course_id <> '00000000-0000-0000-0000-000000000000'),
    administrative_label varchar(100) NOT NULL CHECK (
        length(trim(administrative_label)) > 0 AND administrative_label !~ '[[:cntrl:]]'),
    version integer NOT NULL DEFAULT 1 CHECK (version > 0)
);
CREATE TABLE IF NOT EXISTS coursework_admin.grants (
    grant_id uuid PRIMARY KEY CHECK (grant_id <> '00000000-0000-0000-0000-000000000000'),
    course_id uuid NOT NULL REFERENCES coursework_admin.course_settings(course_id),
    grantee_id uuid NOT NULL CHECK (grantee_id <> '00000000-0000-0000-0000-000000000000'),
    granted_by uuid NOT NULL CHECK (granted_by <> '00000000-0000-0000-0000-000000000000'),
    capabilities integer NOT NULL,
    state integer NOT NULL DEFAULT 0 CHECK (state IN (0, 1)),
    version integer NOT NULL DEFAULT 1 CHECK (version > 0),
    is_root boolean NOT NULL DEFAULT false,
    CHECK ((is_root AND capabilities = 7 AND state = 0) OR
           (NOT is_root AND capabilities IN (1, 3) AND grantee_id <> granted_by))
);
CREATE UNIQUE INDEX IF NOT EXISTS one_active_administrative_grant
    ON coursework_admin.grants(course_id, grantee_id) WHERE state = 0;

-- Transactional producer outbox. Delivery to Platform.Audit is a separate integration.
-- Neither answers, names, passwords nor cookies are stored here.
CREATE TABLE IF NOT EXISTS coursework_admin.outbox (
    event_id uuid PRIMARY KEY,
    occurred_at timestamptz NOT NULL,
    correlation_id varchar(64) NOT NULL,
    actor_id uuid NULL,
    action varchar(100) NOT NULL,
    target_id uuid NOT NULL,
    course_id uuid NULL,
    outcome varchar(16) NOT NULL CHECK (outcome IN ('ALLOW', 'DENY', 'CONFLICT')),
    reason varchar(80) NOT NULL,
    policy_version varchar(64) NOT NULL,
    version_before integer NULL,
    version_after integer NULL
);
INSERT INTO coursework_admin.schema_versions(version) VALUES (1) ON CONFLICT DO NOTHING;

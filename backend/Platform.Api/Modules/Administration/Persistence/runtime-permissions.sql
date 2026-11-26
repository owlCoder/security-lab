-- Operator: create the LOGIN role and its secret outside source control first.
-- Run in the migrated ADM-08 database using the schema owner, not the runtime user.
-- __RUNTIME_ROLE__ is quoted by AdministrationSchema.GrantRuntimeRoleAsync.
GRANT USAGE ON SCHEMA coursework_admin TO __RUNTIME_ROLE__;
GRANT SELECT ON coursework_admin.course_settings, coursework_admin.grants TO __RUNTIME_ROLE__;
GRANT UPDATE (administrative_label, version) ON coursework_admin.course_settings TO __RUNTIME_ROLE__;
GRANT INSERT (grant_id, course_id, grantee_id, granted_by, capabilities) ON coursework_admin.grants TO __RUNTIME_ROLE__;
GRANT UPDATE (state, version) ON coursework_admin.grants TO __RUNTIME_ROLE__;
GRANT INSERT ON coursework_admin.outbox TO __RUNTIME_ROLE__;
-- No CREATE, DELETE, TRUNCATE, outbox UPDATE/DELETE or root-provisioning columns.

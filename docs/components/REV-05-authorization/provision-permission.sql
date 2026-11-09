-- Run with migration/administration credentials using psql variables:
-- user_id, course_id, action. Action is one specific revision.* action, never a wildcard.
-- The application's runtime role must have SELECT only on this table.
\set ON_ERROR_STOP on
INSERT INTO revision_security."CoursePermissions" ("UserId", "CourseId", "Action", "IsActive", "ExpiresAt")
VALUES (:'user_id'::uuid, :'course_id'::uuid, :'action', true, NULL)
ON CONFLICT ("UserId", "CourseId", "Action") DO UPDATE SET "IsActive" = true, "ExpiresAt" = NULL;

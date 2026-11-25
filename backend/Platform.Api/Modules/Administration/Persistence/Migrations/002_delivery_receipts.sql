-- Receipts are separate from the immutable producer events. No data is deleted.
CREATE TABLE IF NOT EXISTS coursework_admin.outbox_deliveries (
    event_id uuid PRIMARY KEY REFERENCES coursework_admin.outbox(event_id),
    acknowledged_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
INSERT INTO coursework_admin.schema_versions(version) VALUES (2) ON CONFLICT DO NOTHING;

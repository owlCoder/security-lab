# Team 20 – LAB-02 / LAB-06 Persistence

## What we did

Our task was to save laboratory metadata and prerequisites in PostgreSQL using EF Core.

At first, our code was inside `Platform.Authoring`. After merging the latest `main`, we moved it to a separate project called `Platform.LaboratoryPersistence` so it doesn't interfere with Team 11's work.

We also updated the code to use the existing models from other teams.

## Integration

- **LAB-01:** We use the existing `Laboratory` model and `ILaboratoryRepository` interface. Our PostgreSQL repository supports saving, loading and updating laboratories, as well as revisions.
- **LAB-02:** We use the existing `LaboratoryMetadata` model. Metadata is saved using the same laboratory ID.
- **LAB-06:** Prerequisites are saved in PostgreSQL. The database prevents duplicate relationships, nonexistent references and direct self-references.
- **Team 11:** Our code is now separate from `Platform.Authoring`, so we don't change their existing models.

The integration still needs to be confirmed with Teams 11 and 14.

## Database

We use PostgreSQL 17 and EF Core 10.

Tables:
- `authoring.laboratories`
- `authoring.lab_prerequisites`
- `authoring.lab_revisions`

We kept the original migration and added `AlignLaboratoryPersistence` for the new fields and revisions.

Both migrations were successfully applied locally.

## Testing

| Test | Result |
|---|---|
| Backend build | Passed |
| Backend tests | 453 passed, 24 skipped |
| PostgreSQL tests | 9/9 passed |
| EF Core migrations | Passed |

All tests that ran passed. PostgreSQL integration tests were tested locally against a real database.

## Remaining work

- Confirm integration with Teams 11 and 14.
- Connect the storage interfaces to the application services.
- Add indirect cycle detection for prerequisites.
- Check migration handling for databases with existing data.
- Set up PostgreSQL tests in GitHub Actions.

The persistence part works locally, but some integration work is still needed.
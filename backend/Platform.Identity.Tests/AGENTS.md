# Platform.Identity.Tests: rules for writing tests

These rules apply to every test in this project, whether a person or a coding agent writes it. Claude Code reads
this file through `CLAUDE.md` in the same folder. Codex, Cursor and other agents read `AGENTS.md` directly.

Before you finish a change, check every test you added or edited against the checklist at the end.

The project uses NUnit, Moq 4.21 and Testcontainers PostgreSQL with Respawn.

## 1. Test names: `Action_Setup_ExpectedResult`

The name has three parts:

1. **Action:** the method, endpoint or flow under test. Tests of one action sort together.
2. **Setup:** the state or input that makes this case different.
3. **Expected result:** what must happen. Several results are joined with `_And`.

```text
Login_InvalidPayload_ShouldReturn400
Login_LockedAccount_ShouldReturn401_AndCreateNoSession
Logout_WithoutSession_ShouldReturn401
ChangeStatus_AdminChangesOwnStatus_ShouldReturnForbidden
Enroll_WithoutToken_IsUnauthorized_AndWritesNoAudit
```

Not allowed:

| Name | Why |
|---|---|
| `Admin_CannotChangeOwnStatus` | no action, setup and result run together |
| `AbandonNext` | no setup, no result |
| `Accounts_AreMappedToIdentitySchema` | two parts |
| `Test1`, `LoginWorks`, `ShouldReturn400` | no structure |

Other naming rules:

- **One wording per file.** Pick `ShouldReturn400`, `IsRejected` or `Throws` and use it the same way in the
  whole file.
- **No people in names or comments.** No reviewer, author or ticket-thread names. Name the requirement instead
  (`// US-18 replay: ...`).
- **Class names:**
  - `public sealed class <Subject>Tests` for unit tests;
  - `<Feature>IntegrationTests` for HTTP integration tests;
  - add `[TestFixture]` and a `[Category("<requirement ID>")]` such as `IDN-05` or `US-17`.

## 2. Arrange, Act, Assert

Every test separates its three parts with comments:

```csharp
[Test]
public async Task Login_InvalidPayload_ShouldReturn400()
{
    // Arrange
    var payload = new { email = "", password = "" };

    // Act
    var response = await Client.PostAsJsonAsync("/api/auth/login", payload);

    // Assert
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
}
```

- Use `// Act & Assert` only when the assertion is the act itself, for example
  `Assert.That(() => manager.ListForUser(Guid.Empty), Throws.ArgumentException)`.
- Use the constraint model: `Assert.That(actual, Is...)`.
- Other comments are welcome when they explain why the test is built the way it is.

## 3. No hand-written fakes, anywhere

Every test double is a `Mock<T>` from Moq. This holds for unit tests and integration tests alike.

**Do not write a class that implements a production interface or overrides a production base class only for a
test.** That includes nested `private sealed class` inside a test file and classes named `Fake...`, `Stub...`,
`Recording...`, `Capturing...`, `Successful...`, `Failing...`, `Throwing...`, `InMemory...` or `Movable...Clock`.

When the interface changes, every hand-written fake in every project has to change with it. A mock does not.

| Need | Do this |
|---|---|
| A dependency | `new Mock<ISessionStore>(MockBehavior.Strict)` + `Setup(...)`, check calls with `Verify(...)` |
| Capture an argument | `Callback<T>(arg => captured = arg)` or `It.Is<T>(x => ...)` |
| A service that fails | `mock.Setup(...).ThrowsAsync(new DbException(...))` |
| An impossible or rare state (race, broken row, store returning `null`) | set it up on the mock. Do not add a fake that "returns the wrong thing" |
| The clock | `Mock<TimeProvider>` with `Setup(t => t.GetUtcNow()).Returns(now)`. Move time by setting up a new value |
| A logger | `NullLogger<T>.Instance`. If you must assert on logs, use `Mock<ILogger<T>>` |

Allowed without Moq:

- real infrastructure in integration tests: PostgreSQL through `TestDatabaseFixture`, the real host through
  `CustomWebApplicationFactory`;
- framework types made for tests: `EphemeralDataProtectionProvider`, `NullLogger<T>`, `NullLoggerFactory`.

**Existing hand-written fakes are being removed.** For example `Helpers/SuccessfulAuditWriter.cs` and fakes in
`ChangeAccountStatusTests`, `UnlockAccountTests`, `LogoutTests` and `StoreSessionCreatorTests`. Do not copy them
and do not add new ones. When you touch one of those files, replace the fake in your test with a mock.

## 4. Unit tests (`Services/`, `Controllers/`, `Middleware/`)

- The class under test is real. **Every** dependency is a Moq mock: stores, other services, `TimeProvider`,
  `IOptions<T>` (`Options.Create(...)` is fine), audit writer, identity context.
- No database. **No EF Core InMemory provider and no SQLite.** A test that needs a real query belongs in the
  integration tests (section 5).
- No `ServiceCollection` / `BuildServiceProvider()` to assemble the class under test. Construct it with `new`
  and pass the mocks. A test of the DI registration itself (`IdentityModuleTests`) is the exception.
- Shared setup for one service goes into an abstract `<Service>TestBase` in the same folder. That base creates
  the mocks and offers `Setup...` / `Verify...` helpers.

## 5. Integration tests (`Integration/`)

Integration tests run the real API host against real PostgreSQL and go through HTTP.

**Every test that needs the database lives under `Integration/`**, including tests of an EF store that call the
store directly instead of going through HTTP. Those go to `Integration/Persistence/<Feature>/`, inherit
`IntegrationTestBase` and get the store with `GetService<TStore>()`.

The reason is `TestDatabaseFixture`. It is a `[SetUpFixture]` in the `Platform.Identity.Tests.Integration`
namespace, so NUnit starts the PostgreSQL container and runs Respawn only for classes in that namespace or below
it. A database test in `Persistence/` would get an empty `TestDatabaseFixture.ConnectionString`.

### 5.1 One host, one database, one base class

- Every integration test class inherits `Integration/IntegrationTestBase`, or a feature base that inherits it,
  such as `MfaIntegrationTestBase`.
- The host is `Factory` (`Helpers/CustomWebApplicationFactory<Program>`) and the HTTP client is `Client`.
- The database is `TestDatabaseFixture` (one Testcontainers PostgreSQL for the whole project). Respawn cleans it in
  the base `[SetUp]`.

**Never:**

- `new WebApplicationFactory<Program>()`;
- a class that derives from `WebApplicationFactory<Program>`;
- `WebApplication.CreateBuilder(...)`;
- your own `PostgreSqlBuilder` / container;
- a hand-assembled `ServiceCollection` that talks to a database;
- EF Core InMemory.

When `Program.cs` gains a required setting, only `CustomWebApplicationFactory` changes. That is the reason for
this rule.

**Exception: host tests that never reach the database.** Some tests only check how the host starts. They switch
the environment, a setting or logging, and never open a database connection: for example, migrations refuse to run
outside Development, or a missing connection string is skipped. Such a class may stay outside `Integration/`
(`Development/DevelopmentDatabaseTests.cs` is the example). It does not inherit `IntegrationTestBase`, but it may
create its own `new CustomWebApplicationFactory<Program>()` in `[OneTimeSetUp]` and dispose it in
`[OneTimeTearDown]`.

- Variants of that host still go through `WithWebHostBuilder`.
- Everything in the **Never** list above still applies.
- If the class needs the database after all, it belongs under `Integration/` and inherits the base.

Why the exception exists:

- `TestDatabaseFixture` is a `[SetUpFixture]` in the `Platform.Identity.Tests.Integration` namespace, so it only
  starts the container for classes in that namespace.
- Outside it, `TestDatabaseFixture.ConnectionString` is empty, and the base `[SetUp]` would call Respawn on a
  database that does not exist.
- Moving the fixture to the root namespace would make every unit test need Docker.

### 5.2 Use the base class helpers

| Need | Helper |
|---|---|
| a service from the running app | `GetService<TService>()` |
| insert a row | `InsertAsync(entity)` |
| read a row by id | `GetAsync<TEntity>(id)` |
| create test data for an entity | `Helpers/<Entity>TestData` (`UserTestData.AddAsync`, `SessionTestData.CreateAsync`, `MfaTestData...`, `AuditTestData...`) |

- If the helper you need does not exist, **add it**: a generic one to `IntegrationTestBase`, an entity-specific
  one to `Helpers/<Entity>TestData`.
- Do not write raw SQL or `db.Set<T>().Add(...)` in the test body.
- Do not create data by calling another module's endpoints.
- When an entity changes, only its helper changes.

### 5.3 Replace what you do not test, with Moq

- Services of other modules that your test only passes through (Audit writer, correlation context) are replaced
  in `CustomWebApplicationFactory` with a Moq mock:

  ```csharp
  services.RemoveAll<T>();
  services.AddSingleton(mock.Object);
  ```

- A variant of the host for one test or one class (a failing audit writer, a lower lockout threshold, a service
  that returns an impossible state) is a private helper in that test class:

  ```csharp
  private static HttpClient ClientWithFailingAuditWriter()
  {
      var writer = new Mock<ITrustedAuditWriter>();
      writer.Setup(w => w.WriteAsync(
              It.IsAny<AuditEventDraft>(), It.IsAny<AuditContext>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("audit store offline"));

      return Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
      {
          services.RemoveAll<ITrustedAuditWriter>();
          services.AddSingleton(writer.Object);
      })).CreateCsrfClient();
  }
  ```

  Replace only the service the test is about. A different setting goes through `builder.UseSetting(...)` in the
  same helper.

### 5.4 Shape of an integration test

1. **Arrange** with `<Entity>TestData` helpers.
2. **Act** with one HTTP call through `Client`, or through the client from your variant helper.
3. **Assert** on the response, and on the stored state read back through helpers or `GetAsync`.

A class that depends on shared database state is `[NonParallelizable]`.

## 6. Where files go

Tests mirror the production code by feature. The project is being reorganised into this layout, so put new files
here even if the folder does not exist yet. `git mv` / creating the file creates the folder; do not add
placeholder files.

| Test | Folder |
|---|---|
| unit test of a service | `Services/<Feature>/<Service>Tests.cs` |
| test of an EF store against PostgreSQL | `Integration/Persistence/<Feature>/<Store>Tests.cs` (needs `TestDatabaseFixture`, section 5) |
| unit test of a persistence helper without a database | `Persistence/<Helper>Tests.cs` |
| HTTP integration test | `Integration/<Feature>/<Feature>IntegrationTests.cs` |
| controller unit test | `Controllers/<Controller>Tests.cs` |
| middleware unit test | `Middleware/<Middleware>Tests.cs` |
| options validation | `Options/<Options>Tests.cs` |
| DI registration | `Composition/IdentityModuleTests.cs` |
| host, base class, data helpers | `Helpers/` and `Integration/` root |

Features:

- `Access`
- `Accounts`
- `Audit`
- `Credentials`
- `Csrf`
- `Development`
- `Federation`
- `Login`
- `Mfa`
- `PasswordReset`
- `Sessions`

The namespace follows the folder (`Platform.Identity.Tests.Services.Mfa`).

**Moving an existing test file:**

1. Use `git mv` in the terminal, in a commit that changes nothing else.
2. Change the namespace and the content in a second commit.
3. Check `git diff -M --stat --summary origin/main...HEAD`: the file must show as a rename (`R`), not as
   delete + create.

VS Code and Visual Studio do not run `git mv` when you move a file in their explorer. JetBrains Rider does.

## 7. Checklist before you finish

- [ ] Every test name is `Action_Setup_ExpectedResult`.
- [ ] Every test has `// Arrange`, `// Act`, `// Assert` (or `// Act & Assert`).
- [ ] No class in my change implements a production interface or overrides `TimeProvider`, `ILogger`,
      `ILoggerProvider` or another production base type. Every double is a `Mock<T>`.
- [ ] Unit tests: no database, no InMemory, no `ServiceCollection`.
- [ ] Integration tests:
  - inherit `IntegrationTestBase`;
  - use `Factory` / `Client` / `GetService` / `<Entity>TestData`;
  - no `new WebApplicationFactory`, no own container;
  - host variants go through `Factory.WithWebHostBuilder`.
- [ ] Host test without a database outside `Integration/`: its own `new CustomWebApplicationFactory<Program>()`,
      variants through `WithWebHostBuilder`, and no database access (section 5.1 exception).
- [ ] Missing test data helper → added to `Helpers/`, not inlined.
- [ ] New file is in `<Layer>/<Feature>/` and its namespace matches. Moved files were moved with `git mv` in a
      separate commit.
- [ ] `dotnet test backend/Platform.Identity.Tests` passes.

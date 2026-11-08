# IDN-10 Email confirmation and IDN-13 new-session alerts

## Requirements

- **IDN-10:** A one-time confirmation token is sent to the account email address. A token can only confirm the account once, and login is refused until confirmation.
- **IDN-13:** A user may opt in to an email notification when a new session is created. The message contains no session token or credentials.

## Email confirmation

`POST /api/admin/accounts` creates both the ASP.NET Identity user and the login account in the same PostgreSQL transaction. The new login account is unconfirmed, so the existing login flow returns the same generic `401 invalid_credentials` used for other denied credentials and does not create a session.

The API generates 256 bits of random token material, stores only its SHA-256 hash in `identity.email_confirmation_tokens`, and emails a link with the token in the URL fragment. The fragment is not sent to the API in the page request; the confirmation page removes it from browser history and submits the token in the request body. Tokens expire after 24 hours. Successful confirmation updates both account stores in one transaction. Reuse, expiry, revocation, and unknown tokens are rejected.

| Endpoint | Result |
|---|---|
| `POST /api/auth/email-confirmation/request` `{ "email": "..." }` | Always returns `202` with the same message, whether or not a matching unconfirmed account exists. |
| `POST /api/auth/email-confirmation/confirm` `{ "token": "..." }` | `204` on success; `400 invalid_or_expired_confirmation` for an invalid, expired, used, or revoked token. |

Confirmation requests use the login rate limit. Responses use `Cache-Control: no-store` and `Referrer-Policy: no-referrer`. SMTP credentials are read from configuration, not committed. If delivery fails after account creation, the account remains unconfirmed and the user can request another message.

## New-session alerts

`GET /api/auth/session-notifications` returns the authenticated caller's preference. `PUT /api/auth/session-notifications` accepts `{ "enabled": true|false }`. The API derives the account id from the validated session, never from client input. The preference is false by default. When enabled, a successful login sends an email after session creation; delivery errors are logged with the account id only and do not turn a successful login into a failure.

## Storage and migration

Migration `AddEmailConfirmationAndSessionNotifications` adds `email_confirmed` and `new_session_notifications_enabled` to `identity.accounts` and creates the token table. Existing accounts default to confirmed with alerts disabled. The token table has a unique index on the hash and an account foreign key.

In local development, the API applies pending migrations for both identity contexts at startup. Other environments must apply the migration as part of deployment.

## Configuration and checks

See [development configuration](../../DEVELOPMENT.md#local-identity-database) for `Email__Smtp__*`, `Email__FromAddress`, and `Frontend__BaseUrl`. Keep SMTP credentials in `.env` or user-secrets.

```powershell
dotnet test backend\Platform.Identity.Tests\Platform.Identity.Tests.csproj
Set-Location frontend
npm test
npm run build
```

The PostgreSQL integration tests use Testcontainers and require Docker.

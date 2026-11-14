# IDN-05: Ograničenje broja pokušaja (rate limiting)

Limit po IP adresi klijenta za prijavu i reset lozinke. Prekoračenje vraća `429 Too Many Requests`
sa `Retry-After` (sekunde) i generičkim telom `{ "code": "rate_limited", ... }` koje ne otkriva da li nalog postoji.

## Kako se koristi (IDN-02 / IDN-04)

Kontroler (npr. `LoginController`):

```csharp
[HttpPost("login")]
[AllowAnonymous] // Identity: prijava ne zahteva postojecu sesiju.
[RequireAccess(IdentityAccessActions.Login)] // ACC-05: registrovana Public akcija i politika.
[EnableRateLimiting(RateLimitPolicies.Login)]
public async Task<IActionResult> Login(...)
```

Minimal API:

```csharp
app.MapPost("/api/auth/password-reset", ...).RequireRateLimiting(RateLimitPolicies.PasswordReset);
```

## Konfiguracija (ADM-04)

| Ključ (env varijabla) | Podrazumevano |
|---|---|
| `RateLimiting__Login__PermitLimit` | 30 |
| `RateLimiting__Login__WindowSeconds` | 60 |
| `RateLimiting__PasswordReset__PermitLimit` | 10 |
| `RateLimiting__PasswordReset__WindowSeconds` | 900 |
| `RateLimiting__KnownProxies__0`, `__1`, ... | prazno |
| `RateLimiting__KnownNetworks__0`, ... (CIDR) | prazno |

Neispravna vrednost (npr. `PermitLimit=0`, loša IP adresa) zaustavlja pokretanje aplikacije.

## Iza reverse proxy-ja

`X-Forwarded-For` se prihvata samo kada zahtev stiže sa adrese iz `KnownProxies`/`KnownNetworks`,
i to samo poslednja adresa (ona koju je dopisao naš proxy). Ako proxy nije podešen, limit se računa
po adresi proxy-ja, pa svi korisnici dele isti brojač. Zato ga obavezno podesiti u produkciji, npr.:

```
RateLimiting__KnownProxies__0=10.0.0.5
```

IPv6 adrese se grupišu po /64 bloku.

## Odluke i poznata ograničenja

- **Sliding window** (6 segmenata): nema dvostrukog naleta zahteva na granici prozora.
- **Brojači su u memoriji**: restart ih resetuje, a više instanci API-ja bi brojalo odvojeno.
  Za više instanci bio bi potreban deljeni store (npr. Redis).
- **Distribuirani napad** sa mnogo IP adresa na jedan nalog zaustavlja IDN-05 blokada u
  `CredentialVerifier` / `EfCredentialStore`, sa brojacem i krajem blokade u `identity.accounts`.
  Zakljucan nalog, pogresna lozinka i nepostojeci email vracaju isti `401 invalid_credentials`.
  `429` i `Retry-After` pripadaju ogranicenju po IP adresi, pre provere lozinke.

## Provera integracije

Na grani `feature/ACC-05-deny-action-by-default` sve tri `/api/auth/*` rute imaju
tacno jednu `RequireAccess` akciju. `AddIdentityAuthenticationAccess()` registruje
`identity.login` i `identity.logout` kao `ActionScope.Public`, a `identity.session.read`
kao `Authenticated`, sa eksplicitnim politikama pre `AddAccessControl()`.
Stari `AllowPublic` i delegirani markeri nisu dozvola za pristup.
Login i logout ostaju dostupni bez sesije; `/me` dobija subjekat iz proverene sesije
kroz `IdentitySessionSubjectProvider`. WebApplicationFactory testovi koriste
ove iste Program registracije, bez zasebnog ili zaobilaznog Access sloja.

`LoginEndpointRateLimitingTests` proverava pravi login endpoint: `401` pre IP limita,
`429` sa `Retry-After` posle limita, `200` za uspesnu prijavu i odsustvo poziva
credential verifier-a za odbijen zahtev. Verifier i izdavanje sesije su u tim testovima zamenjeni.

`Platform.Identity.Tests.Controllers.LockoutEndpointTests` koristi pravi Identity modul,
sesije i privremeni PostgreSQL. Testovi `ManyFailuresForOneAccount_FromDifferentClients_BlockEvenTheCorrectPassword`
i `AccountBlock_AppliesToUnknownEmail_Identically` tamo proveravaju blokadu sa razlicitih IP adresa,
neizdavanje sesije i jednak javni odgovor za nepostojeci nalog. Nemaju `[Ignore]`.

# IDN-05: Ograničenje broja pokušaja (rate limiting)

Limit po IP adresi klijenta za prijavu, reset lozinke i promenu statusa naloga (IDN-03, `PUT /api/admin/accounts/{id}/status`). Prekoračenje vraća `429 Too Many Requests`
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
| `RateLimiting__AccountStatusChange__PermitLimit` | 20 |
| `RateLimiting__AccountStatusChange__WindowSeconds` | 60 |
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

## AUD-01: audit prekoračenja limita prijave

Za odbijanje politikom `RateLimitPolicies.Login` middleware poziva
`IdentityAuditProducer.LoginThrottledAsync`, koji koristi postojeći javni
`ITrustedAuditWriter` i događaj `auth.login.throttled` sa ishodom `Denied`.
Akter je uvijek `Anonymous`, čak i kada zahtjev nosi sesijski cookie. Korelacija
dolazi iz serverskog `TraceIdentifier` i odgovara `X-Correlation-ID` odgovoru.
Ne čita se tijelo prijave; događaj nema nalog, kurs, IP adresu ni dodatne detalje,
pa ne sadrži email, lozinku ili token. Postojeći tehnički log ostaje odvojen.

Produkcioni `Program.cs` već registruje Identity producent i trajni Audit writer.
Potrebna je postojeća Audit konfiguracija (`ConnectionStrings:Audit`) i postojeća
migracija; nema novih tabela ili tipova događaja. Izdvojeni hostovi koji koriste
samo limiter treba da registruju i ovaj ugovor ako žele trajni audit.

Pokušaj audita ima nezavisan rok od jedne sekunde i sopstveni scope. Prekid klijenta
ne otkazuje taj pokušaj. Nedostupan, odbijen, neispravan ili spor writer ne propušta
prijavu i ne mijenja `429`, `Retry-After` ili generičko tijelo odgovora; greška se
bilježi bez sadržaja iz izuzetka. Adapter koji ignoriše otkazivanje zadržava svoj
scope dok se ne završi, bez zadržavanja HttpContext-a i pristupa završenom zahtjevu.

Nema automatskog ponavljanja niti outbox-a: tokom nedostupnosti audita trajni zapis
nije garantovan. Timeout ne dokazuje da zakašnjeli upis nije uspio. Ovo povezivanje
važi za login politiku; politika za reset lozinke ne emituje login događaj.

## Odluke i poznata ograničenja

- **Sliding window** (6 segmenata): nema dvostrukog naleta zahteva na granici prozora.
- **Brojači su u memoriji**: restart ih resetuje, a više instanci API-ja bi brojalo odvojeno.
  Za više instanci bio bi potreban deljeni store (npr. Redis).
- **Distribuirani napad** sa mnogo IP adresa na jedan nalog zaustavlja IDN-05 blokada u
  `CredentialVerifier` / `EfCredentialStore`, sa brojacem i krajem blokade u `identity.accounts`.
  Zakljucan nalog, pogresna lozinka i nepostojeci email vracaju isti `401 invalid_credentials`.
  `429` i `Retry-After` pripadaju ogranicenju po IP adresi, pre provere lozinke.
- **CSRF provera (US-15) je pre limitera.** Prijava bez ispravnog `X-CSRF-TOKEN` dobija
  `403 csrf_invalid`, ne racuna se u limit i ne pravi `auth.login.throttled`, cak i kada je
  klijent vec ogranicen (tada `403`, ne `429`). Isto vazi za promenu statusa naloga. Vidi
  [`IDN-02_CSRF.md`](../../../../../docs/components/identity/IDN-02_CSRF.md).

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

Dodatni testovi provjeravaju jedan anoniman audit događaj, serversku korelaciju,
odsustvo tajni i očuvanje 429 pri odbijenom/neuspjelom/sporom auditu.
`IdentityAuditIntegrationTests.Login_ThrottledAccount_ShouldPersistOneAnonymousDeniedEventWithoutCredentials`
provjerava pravi HTTP pipeline i jedan trajni zapis u novom DB kontekstu, za poznat
i nepoznat nalog, sa stvarnim writer-om i ograničenim SELECT/INSERT audit nalogom.

`Platform.Identity.Tests.Controllers.LockoutEndpointTests` koristi pravi Identity modul,
sesije i privremeni PostgreSQL. Testovi `ManyFailuresForOneAccount_FromDifferentClients_BlockEvenTheCorrectPassword`
i `AccountBlock_AppliesToUnknownEmail_Identically` tamo proveravaju blokadu sa razlicitih IP adresa,
neizdavanje sesije i jednak javni odgovor za nepostojeci nalog. Nemaju `[Ignore]`.

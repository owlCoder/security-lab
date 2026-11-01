# IDN-05: Ograničenje broja pokušaja (rate limiting)

Limit po IP adresi klijenta za prijavu i reset lozinke. Prekoračenje vraća `429 Too Many Requests`
sa `Retry-After` (sekunde) i generičkim telom `{ "code": "rate_limited", ... }` koje ne otkriva da li nalog postoji.

## Kako se koristi (IDN-02 / IDN-04)

Kontroler (npr. `LoginController`):

```csharp
[HttpPost("login")]
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
- **Distribuirani napad** sa mnogo IP adresa na jedan nalog ne zaustavlja se po IP-u, već po nalogu (IDN-02).

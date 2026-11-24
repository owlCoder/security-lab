# S-17: test matrica i izvrsni dokazi

Autor: Nikola Zekovic (nikolazekovic), Tim 12.
Osnova: main `935d7703d3f3ac22f4a0c742db314df95c0f2008`, provereno 24.11.2026.

## Sta je spremno na GitHub-u

- S-09: [#82](https://github.com/owlCoder/security-lab/pull/82), spojeno.
- S-12: [#113](https://github.com/owlCoder/security-lab/pull/113), spojeno.
- S-13: centralni Access audit iz [#197](https://github.com/owlCoder/security-lab/pull/197)
  zamenjuje zatvoreni, nespojeni #151; ne registruju se oba adaptera.
- S-16: [#203](https://github.com/owlCoder/security-lab/pull/203), spojeno;
  IDN-03 zajednicka transakcija iz #148.
- S-14: [#182](https://github.com/owlCoder/security-lab/pull/182), otvoren Draft;
  u njemu je i dalje navedena otvorena S-15 integracija.

## Novi testovi

- `AuditRegressionPostgresTests`: paralelni DI scope-ovi ne mesaju aktere i
  korelaciju; otkazivanje pre upisa ne ostavlja red; otkazivanje nakon Recorded
  i retry cuvaju jedan originalni red; drugi akter ili korelacija ne prepisuju
  isti EventId; bez pouzdanog konteksta nema izmisljenog aktera.
- `AuditHostPostgresTests.HttpRetry_IsIdempotent_AndConflictingPayloadCannotOverwriteTheEvent`:
  isti HTTP zahtev daje jedan red, promenjeni payload se odbija.
- `AccessDenialAuditPostgresTests.Denial_ResponseCorrelationMatchesStoredEvent_AndIgnoresClientHeader`:
  korelacija odgovora odgovara trajnom zapisu, ne vrednosti klijentskog headera.
- `AuditRestorePostgresTests`: stvarni pg_dump/pg_restore popunjene baze;
  isti sadrzaj i vremena, aktivan append-only trigger, sacuvana ogranicenja
  runtime naloga i moguc novi upis nakon obnove. Dve baze imaju nasumicna imena
  i uklanjaju se u finally; izvor nije razvojna baza.

Svi podaci su sinteticki. Nema novih produkcijskih endpointa ili fake registracija.
Proba obnove koristi pg alate iz istog test kontejnera; za nativni lokalni
PostgreSQL moze se zadati `AUDIT_TEST_PG_BIN` sa putanjom do njegovog bin foldera.

## Pokretanje i rezultat

Uz .NET 10, PowerShell 7.4+ i Docker sa Linux kontejnerima, iz korena:

```powershell
pwsh ./scripts/audit/test-postgres.ps1
```

Skripta pokrece ceo backend, ukljucujuci postojeci S-12/S-16 Identity paket koji
koristi sopstvene Testcontainers baze. Zatim proverava obavezne Audit fixture-e.
CI vec koristi ovaj runner i cuva `TestResults/` i pri neuspehu.

`s17-evidence.json` pored TRX-a povezuje T01-T13 sa testovima, ishodima,
commit SHA-om, UTC vremenom i oznakom da li je lokalni kod nekomitovan.
Izvestaj ne sadrzi stdout/stderr, HTTP tela ni connection stringove.
Pisanje izvestaja se pokusava i kada `dotnet test` padne. Nedostajuci,
preskoceni ili pali obavezni testovi obaraju proveru.

Guard ima zasebnu proveru bez baze/Dockera:

```powershell
pwsh ./scripts/audit/test-s17-evidence.ps1
```

## Matrica i preostale zavisnosti

Izvrsna veza sa fixture-ima je u
[`s17-matrix.json`](../../../scripts/audit/s17-matrix.json).

| Grupa | Provera | Granica dokaza |
| --- | --- | --- |
| T01 | DI scope i stvarni startup, pozitivan i negativan slucaj | Ukljuceni registrovani servisi |
| T02 | Akter, UTC i Identity proizvodjaci | HTTP dokaz zahteva Identity PostgreSQL testove |
| T03 | Katalog, allowlist, ishodi i granice payload-a | Nepoznati tipovi/polja se odbijaju |
| T04 | Odbijene vrednosti i bezbedni logovi | Ne dokazuje sve logove drugih modula |
| T05 | SQL prava i append-only trigger | Administrator baze ostaje privilegovan |
| T06 | Greske, retry, otkazivanje i stvarni IDN-03 rollback | Svaki drugi privilegovani proizvodjac zahteva sopstveni dokaz |
| T07 | SQL izolacija K/L i politika | Zavrsni autorizovani AUD-06 API/query tok ceka #182/S-15 |
| T08 | Promena clanstva/delegacije izmedju odluka | Pravi provider i sledeci HTTP zahtev cekaju S-15 |
| T09 | HTTP -> odgovor -> DB korelacija, paralelni scope-ovi | Bez poverenja u klijentski header |
| T10 | Jedan zapis po odbijanju, pad skladista bez otvaranja pristupa | Centralni adapter iz #197, bez drugog forwardera |
| T11 | Validacija filtera i deterministicka paginacija | HTTP/503-before-data ceka #182; produkcijska merenja ostaju otvorena |
| T12 | Production nema test probe/identitet | Odbijanje je 401 ili 403, zavisno od vlasnika rute |
| T13 | Cista migracija, upgrade, health i stvarni restore | Lokalno/test okruzenje, ne produkcijski disaster recovery |

Dok su navedene integracije otvorene, zbirni status je **Partial**, cak i kad
svi dostupni testovi prodju. To ne znaci da je AUD-06 zavrsen.
`write-s17-evidence.ps1 -RequireComplete` odbija takav izvestaj.
Posle merge-a S-14/S-15 treba dodati njihove fixture-e u matricu, izvrsiti ih
i tek onda ukloniti odgovarajuce `pending` stavke.

## Lokalno provereno 24.11.2026.

- Release build resenja prolazi (11 postojecih obsolete-metadata upozorenja);
  zavrsni build Audit test projekta: 0 gresaka i 0 upozorenja.
- Audit: **336 proslih, 0 palih, 0 preskocenih**, ukljucujuci svih osam novih
  S-17 regresionih/restore testova, na PostgreSQL 17.11.
- Siri backend prolaz: **2.199 proslih, 0 palih, 75 preskocenih**.
  Preskoceni testovi zahtevaju zasebne baze ADM-08, LAB, PRG-04 i Team21.
- Docker nije dostupan na ovom racunaru: Identity Integration namespace,
  Identity Audit PostgreSQL i druge Docker-only fixture-e izostavili smo iz
  ovog lokalnog prolaza. Nisu ukljuceni u navedene brojeve niti proglaseni
  uspesnim. CI runner ih ne iskljucuje.
- Lokalni `s17-evidence.json` ispravno ima **NotVerified**: T02/T06 nemaju
  Identity PostgreSQL/rollback fixture-e. T07/T08/T11 ostaju Partial zbog
  integracija. Navedeni nedostatak nije pad izvrsenih Audit testova.
- PowerShell sintaksa, evidence guard i actionlint provera workflow-a prolaze.
  GitHub CI za ove izmene nije pokretan; nista nije poslato na GitHub.

Lokalni TRX i JSON su u ignorisanom direktorijumu
`TestResults/audit-s17-local-36dbcf83456f475b99a14c69146e09b3/`.

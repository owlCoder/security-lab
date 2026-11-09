# REV-05: testovi i dokaz po formatu 8.1

Datum: 2026-11-06, Europe/Sarajevo.
Verzija pune kampanje ispod: commit 83dbd3e. Naknadna audit dopuna je zasebno
evidentirana na kraju dokumenta. SOURCE-SHA256.txt identifikuje trenutno stanje
izvornog koda (osnova 6e310e9; SHA-256 UTF-8 koda sa LF zavrsecima redova).
Ovo su lokalni rezultati; nisu tvrdnja o novom GitHub CI izvrsavanju.

## Izvrsavanje

```powershell
./scripts/audit/test-postgres.ps1
# Zavrsna provjera politike i design-time migracije:
dotnet ef migrations has-pending-model-changes --project backend/Platform.Api --context RevisionSecurityDbContext --configuration Release
dotnet test backend/Platform.Tests/Platform.Tests.csproj --configuration Release --no-restore --filter FullyQualifiedName~RevisionAuthorization
dotnet test backend/Platform.Laboratories.IntegrationTests/Platform.Laboratories.IntegrationTests.csproj --configuration Release --no-restore --filter FullyQualifiedName~RevisionHttpWorkflowTests
# Frontend:
cd frontend
npm ci --no-audit --no-fund
npm run build
npm test
```

Design-time komanda koristi REVISION_SECURITY_MIGRATION_CONNECTION_STRING.
Provjera modela ne povezuje bazu. Prijavila je da nema promjena od posljednje
migracije. Snapshot je zamrznuta definicija, odvojena od runtime konfiguracije.
HTTP migration test je i izvrsio migraciju na novoj bazi i ponovio je uz ocuvane grantove.

Build cijelog backend rjesenja u Release konfiguraciji: 0 gresaka, 0 upozorenja.
Backend: 1336 razlicita testa prosla, 0 palo, kroz glavnu i dodatnu kampanju.
Frontend: build uspjesan i 60/60 testova proslo. npm je prijavio engine upozorenja
za lokalni Node 24.14.0, ali build i testovi su zavrsili uspjesno; zavisnosti nisu mijenjane.

| Projekat | Proslo | Palo |
|---|---:|---:|
| Laboratories.Tests | 124 | 0 |
| Platform.Access.Tests | 335 | 0 |
| Platform.Identity.Tests | 182 | 0 |
| Platform.Authoring.Tests | 84 | 0 |
| Platform.Audit.Tests | 150 | 0 |
| Platform.Kt1.Tests | 65 | 0 |
| Platform.LabLocalization.Tests | 54 | 0 |
| Platform.LabMetadata.Tests | 21 | 0 |
| Platform.LaboratoryPersistence.Tests | 9 | 0 |
| Platform.Prerequisites.Tests | 25 | 0 |
| Platform.RateLimiting.Tests | 24 | 0 |
| Platform.Tests | 163 | 0 |
| Platform.Laboratories.Tests | 62 | 0 |
| Platform.Laboratories.IntegrationTests | 38 | 0 |

Glavna kampanja izvrsila je 1327 testova, a devet Authoring persistence testova
preskocila jer njihov zaseban ConnectionStrings__Authoring nije bio podesen.
Svih devet je zatim izvrseno u posebnom jednokratnom PostgreSQL kontejneru sa
primijenjenim stvarnim Authoring migracijama: 9 proslo, 0 palo, 0 preskoceno.
Broj 1336 ne sabira ponovna izvrsavanja istih testova.
Audit PostgreSQL/runtime testovi su svi izvrseni, ukljucujuci zabranjene SQL
operacije sa ogranicenim runtime nalogom. Nema preskocenih audit testova u ovoj kampanji.
Zavrsna izdvojena REV-05 provjera: 81/81; novi HTTP/PostgreSQL scenariji: 10/10.

Lokalni sirovi izvjestaji (git-ignored TestResults):

- TestResults/audit-s09-2a399faa7d2d4e5ea7e3eb259273d3c4/*.trx (zavrsna kampanja)
- TestResults/rev05-final/authoring-final.trx
- TestResults/rev05-final/rev05-final.trx
- TestResults/rev05-final/rev05-http-final.trx (ranijih osam HTTP slucajeva; zavrsna kampanja sadrzi svih deset)

## Scenariji (8.1)

| Requirement ID | Preduslov | Akcija | Ocekivano | Izvrsivi dokaz |
|---|---|---|---|---|
| REV-05 / REV-01 / REV-02 | Stvarne sesije, trajni grantovi i validna definicija | HTTP create, read, mine, submit, pending, return, submit, approve i isti retry | 201/200, Published, jedan broj i snapshot, cetiri poslovne operacije i cetiri audit zapisa | RevisionHttpWorkflowTests.AuthorizedHttpFlow_CreatesReadsSubmitsReturnsAndPublishes_WithDurableAudit |
| REV-05 | Autor ima approve/read, kurs zahtijeva nezavisnost | HTTP samoodobravanje | 403, bez odobrene objave | RevisionHttpWorkflowTests.SelfApprovalHttp_FollowsPersistedCoursePolicy(true, Forbidden) |
| REV-05 | Autor ima approve/read, kurs ne zahtijeva nezavisnost | HTTP samoodobravanje | 200, trajna objava i audit | RevisionHttpWorkflowTests.SelfApprovalHttp_FollowsPersistedCoursePolicy(false, OK) |
| REV-05 / ACC-05 | Popustena politika, ali nedostaje approve pravo | Provjera politike | Deny | RevisionApprovalPolicyTests.RelaxedPolicy_DoesNotGrantMissingApprovalPermission |
| ACC-03 / ACC-05 | Read grant se opoziva | Sljedeci HTTP read | 404 bez sadrzaja, jedan trajni access.denied sa akterom i korelacijom odgovora | RevisionHttpWorkflowTests.RevokedCourseGrant_DeniesNextHttpRead_AndPersistsDenial |
| SEC-01 / ACC-04 | Grant je istekao ili pripada drugom kursu | HTTP read konkretne revizije | 404 bez tudjeg sadrzaja | RevisionHttpWorkflowTests.ExpiredOrOtherCourseGrant_DoesNotAuthorizeHttpRead (2 slucaja) |
| AUD-02 / REV-05 | Audit tabela privremeno nije dostupna u izolovanoj bazi | HTTP submit preko stvarnog writera | 503; status Draft, RowVersion=1, bez receipt-a i bez audit zapisa | RevisionHttpWorkflowTests.FailedAudit_RollsBackHttpSubmitAndReceipt |
| ADM-02 / OPS-01 | Postojece grantove treba sacuvati | Ponovno primijeni migraciju i uporedi snapshot | Nema pending promjena; grantovi sacuvani | RevisionHttpWorkflowTests.SecurityMigration_IsRepeatableAndPreservesExistingGrants |
| ACC-05 | Nepoznata akcija ili poznata bez politike | IAccessDecision poziv | Deny | RevisionDefaultDenyTests.UnknownAction_IsDenied; KnownActionWithoutPolicy_IsDenied |
| ACC-05 / IDN-05 | Sistemski unlock ili nedozvoljen resurs | Provjera registrovane politike | Samo ovlasteni administrator; resurs na sistemskoj akciji odbijen | RevisionPermissionIntegrationTests.Administrator_CanUnlock_ButCannotApproveWithoutCourseRights; UnlockWithUnexpectedResource_IsDenied |
| IDN-02 / SEC-01 | Nema sesije, opozvana sesija ili sesija bez prava | HTTP approve | 401, 401, 403 | ReviewAuthorizationTests.ProductionRevisionRoute_UsesRealSessionAdapter |
| IDN-03 / SEC-01 | Nalog je blokiran ili arhiviran nakon izdavanja jos neopozvane sesije | HTTP read revizije | 401, bez sadrzaja | RevisionHttpWorkflowTests.InactiveAccount_CannotUsePreviouslyIssuedSession (2 slucaja) |
| AUD-03 / AUD-04 | Lazni actor/correlation header | Formiranje audit konteksta | Potvrdjena sesija i serverska korelacija | RevisionAuditForwardingTests.Context_IgnoresClientHeadersAndUsesValidatedSession |

## Granice i integracija

HTTP fixture koristi stvarne PostgreSQL Identity sesije, persistent grant reader,
resource/policy reader, StoredPublicationSource i audit store. Nema fake Allow
masine, session manager-a ili uspjesnog audit writera. Sesije i korisnici su
sinteticki podaci u privremenoj bazi, bez stvarnih korisnika ili tajni. Kvar audita
simuliran je privremenom nedostupnoscu stvarne tabele, a ne laznim rezultatom.

Ovi testovi dokazuju REV-05 tok i njegovu integraciju. Ne proglasavaju zavrsenim
ostale vlasnicke zadatke poput Identity lockout implementacije ili upravljackog
interfejsa za dodjelu prava. Za instalaciju su obavezne dokumentovane migracije i
eksplicitna dodjela minimalnih prava; u produkciji se ne registruju testni adapteri.

## Dopuna pregleda: policy_denied (2026-11-06)

Ocekivano odbijanje (`PolicyDenied`) sada se mapira u `policy_denied`, dok
`PolicyError` ostaje `policy_error`. Audit validator prihvata oba koda. Dopuna
ne mijenja odluke o pristupu, javne HTTP statuse, schemu baze ili stare audit zapise.

Postojeci RevokedCourseGrant_DeniesNextHttpRead_AndPersistsDenial sada cita
details.denialReason iz stvarne baze i zahtijeva `policy_denied`, uz isti potvrdjeni
akter i serversku korelaciju. Provjera da je dogadjaj jedinstven ostaje ukljucena.
Prije ispravke test je pao sa stvarnom vrijednoscu `policy_error`; poslije prolazi.
Prvi pokusaj pokretanja bio je blokiran nepokrenutim Docker-om; nakon pokretanja
Docker-a potvrden je opisani pad na pogresnoj vrijednosti.

Zasebna provjera ove dopune (ne pribraja se prethodnoj punoj kampanji):

| Provjera | Proslo | Palo | Preskoceno |
|---|---:|---:|---:|
| RevisionAuthorization | 83 | 0 | 0 |
| AuditDraftValidatorTests i AuditEventCatalogTests | 72 | 0 | 0 |
| Postojeci PostgreSQL/HTTP test opozvane dozvole | 1 | 0 | 0 |

Rezultati: TestResults/policy-denied-review/revision-authorization-after.trx,
audit-contract-after.trx i policy-denied-after.trx. Regresioni padovi su u
reason-mapping-before.trx i policy-denied-before.trx. Naknadna puna kampanja nije
ponavljana; ova promjena je provjerena navedenim ciljanim testovima.

## Dopuna pregleda: revision.entry (2026-11-07)

Na osnovi 8ba98b2 novi HTTP/PostgreSQL test dobio je ocekivani 401 za zahtjev
bez sesije i zahtjev sa nepoznatom sesijom, ali bez trajnog audit zapisa. Oba
slucaja su pala bas na provjeri prisustva zapisa. Nakon dopune RevisionActions.IsKnown
ulaznom akcijom oba slucaja prolaze. Endpoint i filter dijele konstantu za ovu akciju;
kursne dozvole i sama odluka o pristupu nisu promijenjeni. Nema nove migracije.

| Requirement ID | Preduslov i akcija | Ocekivano | Izvrsivi dokaz |
|---|---|---|---|
| AUD-01 / AUD-03 / AUD-04 / SEC-01 | HTTP GET /api/revisions/mine bez sesije ili sa nepoznatom sesijom; poslat lazni X-Correlation-ID | 401; tacno jedan trajni access.denied za revision.entry, Anonymous bez actor_id, Denied, unauthenticated i serverska korelacija iz odgovora | RevisionHttpWorkflowTests.DeniedRevisionEntry_PersistsAnonymousAuditWithServerCorrelation (2 slucaja) |

Ciljana provjera poslije ispravke: 85/85 RevisionAuthorization testova i 2/2
nova HTTP/PostgreSQL slucaja. Regresioni rezultati su u:
TestResults/revision-entry-review/entry-denial-before.trx, entry-denial-after.trx i
entry-authorization-after.trx.

Uskladjenost grane je provjerena nakon git fetch: origin/main je predak radne
grane (0 commit-a iza main), a PR grana odgovara lokalnom HEAD-u prije dopune.
Nije bilo konflikata niti potrebe za dodatnim merge commitom.

Puna lokalna CI provjera nakon ove dopune:

- Release build cijelog rjesenja: 0 gresaka i 0 upozorenja.
- scripts/audit/test-postgres.ps1: 1335 izvrsenih testova proslo, 0 palo;
  devet zasebnih Authoring persistence testova preskoceno zbog nepodesene
  ConnectionStrings__Authoring konekcije, kao u postojecem CI podesavanju.
- Svih 40 REV-05 PostgreSQL integracionih testova i svi Audit runtime testovi su izvrseni.
- Frontend build i svih 60 testova: proslo.
- LaboratoriesDbContext: nema pending model changes.

Sirovi izvjestaji pune kampanje: TestResults/audit-s09-ee75a87cd4c44e1999af4b0963f7ef4d/*.trx.

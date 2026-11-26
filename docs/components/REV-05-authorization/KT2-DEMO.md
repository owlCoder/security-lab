# KT2 — demonstracija Lukinog dijela REV-05 / IDN-05

Osnova: main `e2c97fe`, 23.11.2026. Obim: eksplicitna prava i Audit integracija.

## Priprema

Komande se izvrsavaju iz korijena repozitorijuma, sa projektnim .NET SDK-om.
Za HTTP/PostgreSQL dokaze potreban je Docker sa Linux kontejnerima. Testcontainers
pravi zasebne baze sa sintetskim korisnicima; razvojna baza se ne mijenja.
Ovo je automatizovana demonstracija stvarnog API toka; browser prikaz je zaseban
timski korak. Za browser koristiti cookie prijavu i pripremljene serverske grantove,
a ne stari Bearer mock UI prototipa.

## Tehnicka uloga nije pravo odobravanja

```powershell
dotnet test backend/Platform.Tests/Platform.Tests.csproj --configuration Release --filter "FullyQualifiedName~RevisionAuthoritativePermissionTests"
```

Administrator, nastavnik i saradnik sa imenovanom approve delegacijom ne zaobilaze
autoritativni izvor grantova. Nedostajuci, opozvani, istekli i grant drugog kursa
daju Deny. Nova odluka ponovo cita pravo. Ova grupa koristi stvarni permission
reader i politike uz EF InMemory; ne dokazuje SQL ili HTTP.

## Predaja, vracanje, odobrenje i trajan audit

```powershell
dotnet test backend/Platform.Laboratories.IntegrationTests/Platform.Laboratories.IntegrationTests.csproj --configuration Release --filter "FullyQualifiedName~RevisionHttpWorkflowTests.AuthorizedHttpFlow|FullyQualifiedName~RevisionHttpWorkflowTests.SelfApprovalHttp|FullyQualifiedName~RevisionHttpWorkflowTests.FailedAudit_RollsBackHttpSubmitAndReceipt|FullyQualifiedName~RevisionHttpWorkflowTests.RevokedCourseGrant|FullyQualifiedName~RevisionHttpWorkflowTests.CreateRevision_UsesPersistedLaboratoryCourse"
```

Markov API pokrece operaciju, Lukine politike provjeravaju pravo i nezavisnost,
a Audit ugovor cuva dogadjaj u istoj transakciji. Pokazati submitted/returned/approved,
ID revizije, autora, ID operacije, korelaciju i broj objave. Retry ne pravi drugu
objavu. Samoodobravanje zavisi od politike kursa; uloga ne nadoknadjuje nedostajuce
pravo. Kada audit mutacije ne uspije, nema commit-a.

## IP limit i zakljucavanje

```powershell
dotnet test backend/Platform.RateLimiting.Tests/Platform.RateLimiting.Tests.csproj --configuration Release
dotnet test backend/Platform.Identity.Tests/Platform.Identity.Tests.csproj --configuration Release --filter "FullyQualifiedName~IdentityAuditPostgresIntegrationTests"
```

Prva grupa koristi pravi Program pipeline sa testnim credential/session adapterima;
druga potvrdjuje upise u izolovanom PostgreSQL-u sa ogranicenim Audit runtime nalogom.
IP limit daje 429 + Retry-After prije provjere lozinke. auth.login.throttled ima
Anonymous aktera i serversku korelaciju, bez naloga, emaila, lozinke i tokena.

AccountLockout testovi dokazuju prag i kasniji pokusaj: auth.login/Failure sa
account_locked, nalog kao cilj, Anonymous akter, genericki 401 i bez nove sesije.
To nije poseban dogadjaj nastanka blokade. Stvarno administratorsko otkljucavanje
jos nije povezano; dogovor sa Milanom i Timom 12 opisan je u [KT2-STATUS](KT2-STATUS.md).

## Evidencija ove provjere

Lokalna Release provjera 23.11.2026, osnova e2c97fe + ovaj paket:

| Grupa | Proslo | Palo | Preskoceno |
| --- | ---: | ---: | ---: |
| Sva REV autorizacija, ukljucujuci 9 novih slucajeva | 109 | 0 | 0 |
| IdentityAuditProducerTests | 11 | 0 | 0 |
| Platform.RateLimiting.Tests | 38 | 0 | 0 |
| Ukupno | 158 | 0 | 0 |

`git diff --check` i provjera svih lokalnih linkova ova cetiri dokumenta prolaze.
Kompilacija je prijavila postojeca CS0618 upozorenja u testovima migracionih
Access markera; novi test nije dodao upozorenja.

Naknadno je pokrenut Docker Desktop i izvrsena kompletna backend skripta:

```powershell
./scripts/audit/test-postgres.ps1
```

Rezultat: **2370 proslo, 0 palo, 70 preskoceno**, od ukupno 2440 testova. Svih
58 Platform.Laboratories.IntegrationTests i svih 441 Platform.Identity.Tests
proslo je bez preskakanja. Skripta je potvrdila obavezne Audit PostgreSQL fixture-e.
Preskoceno je 52 Coursework, 9 LAB persistence, 8 Competencies persistence i jedan
Team21 test, za koje ovaj postupak nema zasebnu konfiguraciju. Nema novog preskakanja
u REV, Identity ili Audit grupama. Ovo je lokalna puna backend kampanja; novi
GitHub CI treba potvrditi na PR-u. Ciljanih 158 testova vec je obuhvaceno punom
kampanjom i ne dodaje se ponovo zbiru.

Lokalni log: TestResults/rev05-audit-kt2-finalization/backend-validation.log;
TRX: TestResults/audit-s09-521703e62a134c8a95cf768e475a70db (oba git-ignored).
Istorijski izvjestaji ostaju vezani za svoje datume i commit-e.

Za individualnu odbranu pokazati RevisionApprovalPolicy, RevisionPermissions,
RevisionAuditEventFactory i IdentityAuditProducer, pa odnos ReviewService
transakcije prema javnom ITransactionalAuditWriter ugovoru.

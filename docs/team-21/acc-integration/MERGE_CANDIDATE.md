# Team 21 — završni lokalni review-v2

Pripremljen je **pending merge bez commit-a** na novoj grani `feature/acc-integration-review-v2`, u `TestResults/t21-review-v2-fe6507f-8b7a2d`.

- HEAD: `00b3c47cb652fd9ad70e446b35e420e320031479`.
- MERGE_HEAD: `fe6507fcd873b4e9cc73b53c961cd2e0dc23ed15`, potvrđen posle novog fetch-a.
- Referenca je nepromenjena testirana detached proba `TestResults/t21-refresh-fe6507f-9e35c7`.
- Preneto je tačno 15 pregledanih putanja; van njih svih 863 index zapisa već je bilo identično referenci.
- Produkcioni i testni sadržaj ostaje identičan referenci. Razlike prema referenci ograničene su na šest naših dokumenata: oznake nove grane i poreklo dokaza. Mermaid dijagrami su isti.
- Test/build rezultati ispod jesu sačuvani dokazi referentne probe, ne nova izvršavanja na review-v2. Dokumentacija i scoped whitespace proveravaju se ponovo na review-v2.
- Izvorna grana, stari review sa četiri radne dokumentacione korekcije, referentni kandidat i stariji worktree-i ostaju sačuvani. Predatih 39 Part 8 slučajeva nije vraćeno.
- Korisnik potvrđuje odobren KT1 dizajn; **završni lokalni merge commit zahteva zasebno odobrenje**. Push, PR i Production ostaju blokirani.

U nastavku je sačuvan izveštaj prethodne osvežene detached probe. Njene putanje i opis tadašnjeg zadatka predstavljaju poreklo prenetih dokaza; aktuelna review grana navedena je iznad.

## Zapis osvežene izolovane KT1 probe

**Prethodno odobreni Access/IDN dizajn kompatibilan je sa novim roditeljem u izvršenim bezbednim proverama. Nema novog commit-a.** Proba od 2026-11-08 ne menja originalnu ili staru review granu. Puni CI, PostgreSQL workflow-i, objava, PR i produkciona spremnost nisu potvrđeni.

## Osnove, odobrenja i očuvanje

- Original: `feature/acc-integration`, čist na `00b3c47cb652fd9ad70e446b35e420e320031479`.
- Uspešan `git fetch origin` potvrdio je roditelja `origin/feature/acc06-groups` na **`fe6507fcd873b4e9cc73b53c961cd2e0dc23ed15`**.
- Nova proba: `TestResults/t21-refresh-fe6507f-9e35c7`, detached HEAD `00b3c47`, pending MERGE_HEAD `fe6507f`. Merge commit nije napravljen.
- Stari `feature/acc-integration-review` u `TestResults/t21-review-00b3c47-6a4dc7` ostaje na `00b3c47`, sa pending MERGE_HEAD `3b1e6ff0ea293df8a617ea22fd06719806a553d5`. Njegov razrešen index i četiri nestageovane dokumentacione korekcije nisu menjani.
- Prethodni kandidat, PoC i baseline worktree-i sačuvani su. Nijedan postojeći worktree nije resetovan, abortovan ili očišćen.
- Devet originalnih commitova ostaje u istoriji HEAD-a: `ae726c8`, `3418f8c`, `0bc0011`, `5339a4a`, `f43e754`, `c94882e`, `0c05a16`, `3fca69c`, `00b3c47`.
- Prema ranijoj potvrdi korisnika od 2026-11-08 Team 21, shared host, Access i IDN vlasnici odobrili su KT1 dizajn i ograničenja stare kombinacije. Četiri korekcije o tom odobrenju prenete su iz radnih, ne samo index verzija starog review-a. To je korisnikova potvrda, ne nezavisno proveren remote review.
- Aktuelni zadatak odobrava novu proveru kompatibilnosti, **ne commit nove osnove**, push, PR ili deploy. Prethodno odobrenje lokalnog commit-a protiv starog roditelja ne primenjuje se automatski na novi pending merge.

## Deset novih roditeljskih commitova

Od `3b1e6ff` do `fe6507f` postoji deset dodatnih commitova, bez udaljavanja starog roditelja iz istorije. Njihov neto diff ima 56 fajlova, 3095 dodatih i 105 uklonjenih redova; merge commitovi i zajednička istorija ne predstavljaju deset nezavisnih funkcionalnih promena.

| Commit | Sadržaj |
| --- | --- |
| `fe6507f` | Merge main u feature/acc06-groups |
| `3f6d945` | Merge PR #138, feat/lab-03-mihailo |
| `42d4d7d` | Merge PR #111, feature/LAB-03-provera-konteksta |
| `f07742b` | IDN-02 integrity tests, PR #120 |
| `f064ab6` | IDN-05 identity lockout, PR #99 |
| `5eec968` | LAB-03 provera konteksta prilikom otvaranja |
| `1773e54` | LAB-03 popravke build-a posle merge-a |
| `fbd776f` | Merge main u feat/lab-03 granu |
| `a565c6b` | LAB-03 zahteva kurs pri kreiranju |
| `e1859bd` | LAB-03 provera istog kursa |

Relevantne promene:

1. **IDN-05:** konfigurabilan login lockout, podrazumevano pet pokušaja i 15 minuta; validacija opcija pri startu, credential store/verifier izmene i lockout podaci na nalogu. CredentialVerifier sada zavisi i od IOptions/TimeProvider. Dodata je migracija `20261105212557_AddAccountLockout`, designer i snapshot. Migracija nije izvršena.
2. **IDN-02:** LoginRequest prelazi iz Models u Contracts/Identity, dodati su LoginResponse/MeResponse i integracioni testovi sa PostgreSQL/Testcontainers, MigrateAsync i Respawn resetovanjem. Ti DB fixture-i nisu pokretani.
3. **LAB-03:** CourseId pri kreiranju laboratorije, politike vlasništva/članstva/nastavnika pri kreiranju i otvaranju; legacy rehidrirani zapisi bez kursa odbijaju se. Poznata granica sa Team 20 čuvanjem CourseId ostaje vlasnicima LAB/persistence modula.
4. **Program.cs:** novi roditelj dodaje samo četiri Development demo reda za drugi LAB kurs i studenta. Sva četiri sačuvana su uz našu prethodno pregledanu kompoziciju.
5. Nema novih izmena SessionCheckMiddleware-a, Access modula, Team21AccModule-a, ACC-01/06 izvora/testova/dokumentacije ili CourseStateReader-a u ovih deset commitova. Nema novih CI workflow fajlova. Ipak, novi IDN DB testovi i migracija povećavaju zahteve punog CI/runtime postupka.
6. Roditeljski IDN test projekat dodaje Respawn i TestHost; ima dupliran MvcTesting PackageReference. To je roditeljski NU1504 nalaz, bez popravke u Part 9 probi.

## Konflikti i prenos pregledanog patch-a

Dva tekstualna konflikta bila su u [Program.cs](../../../backend/Platform.Api/Program.cs) i [Team README](../README.md).

U Program-u je napravljen union potrebnih imports, sačuvan REV, sve roditeljske registracije/politike i četiri nova LAB demo reda. Preneta je ista minimalna Part 9 kompozicija: AddTeam21SessionAccess posle REV dekoratora, ranija session grana posle korelacije i pre Access-a, kasnija session grana samo za ostale endpoint-e. Nije kopiran stari Program preko nove osnove.

Roditeljski Team README nije menjan u deset novih commitova. Konfliktni odeljak spojen je pregledanim rešenjem koje čuva ACC-01 opise i session-cookie razvojni tok. Potom su ažurirani isključivo naši dokazi i granice nove probe.

Pre poređenja dokumentacije potvrđeno je da 16 relevantnih produkcionih/testnih fajlova odgovara starom pregledanom rešenju uz normalizaciju završetaka redova. Program se razlikuje tačno za četiri nova roditeljska LAB reda. Nijedna funkcionalnost drugog tima nije popravljana radi ove probe.

### Dodatni Part 9 delta

| Fajl / grupa | Sadržaj |
| --- | --- |
| `backend/Platform.Api/Program.cs` | Minimalna shared-host kompozicija uz sačuvan novi roditeljski sadržaj |
| `backend/Platform.Api/Modules/Team21/Integration/Team21AccessSubjectProvider.cs` | Pregledani scoped subject dekorator |
| `backend/Platform.Api/Modules/Team21/Integration/Team21SessionAccessExtensions.cs` | Keyed očuvanje originalnog provider-a i selektivni session pipeline |
| `backend/Team21.Acc.Api/Team21SessionRequiredMetadata.cs` | Serverski endpoint marker |
| `backend/Team21.Acc.Api/Team21AccModule.cs` | Marker bez izmene ruta i RequireAccess |
| `backend/Team21.Acc.Tests/Api/SessionCurrentActorHostIntegrationTests.cs` | Dve asercije 404 → 403 za tri zadržana account-status slučaja |
| `backend/Team21.Acc.Tests/Api/Team21AccessSubjectProviderTests.cs` | 19 prethodno pregledanih slučajeva |
| `backend/Team21.Acc.Tests/Api/Team21SessionAccessPipelineTests.cs` | 15 prethodno pregledanih slučajeva |
| `backend/Team21.Acc.Tests/Api/Team21EnvironmentCompositionTests.cs` | Tri postojeće provere stvarnih module DI registracija |
| Šest Team 21 dokumenata | Sačuvana odobrenja, nova osnova, sveži rezultati i ograničenja |

Ovo su dopune posle spajanja istorije; SessionCurrentActor, ograničeno uklanjanje Development bypass-a, audit testovi, helper i test projektna referenca već dolaze iz devet originalnih commitova. Root README se automatski spaja iz istorije. Za ukupan naš obim koristiti `git diff --cached fe6507f --name-status` u novom worktree-u, a ne diff prema HEAD-u koji uključuje sve roditeljske promene.

### Ukupan obim prema novom roditelju

Ista 24 fajla kao u prethodnom review-u, uključujući ranije commitovan Part 9 rad:

~~~text
README.md
backend/Platform.Api/Middleware/SessionCheckMiddleware.cs
backend/Platform.Api/Modules/Team21/Integration/SessionCurrentActor.cs
backend/Platform.Api/Modules/Team21/Integration/Team21AccessSubjectProvider.cs
backend/Platform.Api/Modules/Team21/Integration/Team21SessionAccessExtensions.cs
backend/Platform.Api/Program.cs
backend/Team21.Acc.Api/Team21AccModule.cs
backend/Team21.Acc.Api/Team21SessionRequiredMetadata.cs
backend/Team21.Acc.Tests/Api/HttpCorrelationContextTests.cs
backend/Team21.Acc.Tests/Api/SessionCurrentActorHostIntegrationTests.cs
backend/Team21.Acc.Tests/Api/SessionCurrentActorTests.cs
backend/Team21.Acc.Tests/Api/Team21AccessSubjectProviderTests.cs
backend/Team21.Acc.Tests/Api/Team21EnvironmentCompositionTests.cs
backend/Team21.Acc.Tests/Api/Team21SessionAccessPipelineTests.cs
backend/Team21.Acc.Tests/Groups/LoggingVisibilityAuditWriterTests.cs
backend/Team21.Acc.Tests/Infrastructure/LoggingAuditTrailTests.cs
backend/Team21.Acc.Tests/Support/RecordingLogger.cs
backend/Team21.Acc.Tests/Team21.Acc.Tests.csproj
docs/team-21/README.md
docs/team-21/acc-integration/ARCHITECTURE.md
docs/team-21/acc-integration/DFD.md
docs/team-21/acc-integration/INTEGRATION_PLAN.md
docs/team-21/acc-integration/KT1_READINESS.md
docs/team-21/acc-integration/MERGE_CANDIDATE.md
~~~

## Bezbednosni ugovori i preostale runtime granice

- Označene Team 21 rute: routing → AUD korelacija → postojeći IDN session middleware → Access → servis. Kasnija session grana ih preskače; token se validira jednom. Neoznačene rute čuvaju stari redosled i originalni provider, uključujući REV.
- RequireAccess ostaje uključen. IIdentityContext registracija ostaje SessionIdentityContext. SessionCurrentActor prihvata samo validirani Active session identitet sa nepraznim UserId; klijentski identity header-i nisu fallback.
- Provider mapira Team 21 Active → Access Active, Blocked → Blocked, Inactive/nepoznato → Unknown. Unknown nikad ne postaje Active. Kvar reader-a dolazi do postojećeg Access catch-a i zahtev se odbija.
- Access vraća 401 za neprihvaćen identitet, 403 za autentifikovan non-Active nalog. Servisne provere vlasništva, članstva i publike ostaju; tuđi zaštićeni resursi i dalje imaju servisni 404. Sačuvani su isti session token, statusne tranzicije, provere odsustva privatnih podataka i neizmenjenog stanja posle odbijenog upisa.
- Originalni provider zadržava lifetime/disposal preko keyed registracije; Team 21 zavisnosti rešavaju se samo na označenim endpoint-ima. Testovi proveravaju izolaciju ostalih ruta i scoped zahteva.

| Team 21 zavisnost | Development | Testing / Production |
| --- | --- | --- |
| ICurrentActor | SessionCurrentActor | SessionCurrentActor |
| IAccountStatusReader | FakeAccountStatusReader | DenyAllAccountStatusReader |
| ICourseStateReader | Stvarni EF CourseStateReader | Stvarni EF CourseStateReader |
| ICourseMembershipReader | FakeCourseMembershipReader | NoCourseMembershipReader |
| ICourseAuthorization | DevelopmentCourseAuthorization | DenyAllCourseAuthorization |

Production deny-all reader vraća Inactive čak i legitimnom aktivnom korisniku; Access tada vraća 403. Nema autoritativnog status adaptera. IDN ima javni FindAsync ugovor i EfAccountStatusReader nad Accounts, ali host status write put koristi InMemoryAccountRepository. Nisu potvrđeni autoritativnost i sinhronizacija tih putanja. Novi login lockout i EF polja ne rešavaju tu neusaglašenost.

Prazno članstvo i deny-all course permissions ostaju sledeće prepreke. HTTP testovi koriste kontrolisane ulaze i ne dokazuju legitiman produkcioni tok. Stvarni GroupMembershipReader/MaterialAccessRuleReader i CourseStateReader nisu zamenjeni duplikatima. Centralni AUD katalog, garantovan transakcioni upis, konačna LAB/ASN sadržajna autorizacija i zajednički correlation ID kroz module ostaju ranije dokumentovane KT2 granice.

## Sveže provere na fe6507f

| Provera | Stari kandidat protiv 3b1e6ff — istorijski | Nova detached proba |
| --- | --- | --- |
| Bezbedni Team 21 izbor | 259/259 | **259/259** |
| Ciljani identity / Access / DI izbor | 68/68 | **68/68** |
| Smanjeni HTTP fixture | 19/19 | 19/19 |
| SessionCurrentActor | 12/12 | 12/12 |
| Provider / pipeline / environment | 19 + 15 + 3 | 19 + 15 + 3 |
| IDN session middleware / context | 19/19 | **19/19** |
| Access, bez dva Development-host testa | 356/356 | **356/356** |
| AUD correlation | 17/17 | **17/17** |
| REV authorization | 5/5 | **5/5** |
| Dodatni bezbedni IDN lockout/login izbor | Nije poređen u starom zadatku | **67/67** |
| LAB bez Development HTTP fixture-a | Nije poređen u starom zadatku | **106/106** |
| Backend Release | Prolazio na staroj osnovi | **0 grešaka, 46 NU1504/NU1900 upozorenja** |

Svi izvršeni izbori imaju nula padova i nula preskočenih. Skupovi se preklapaju: 68 je podskup Team 21, a LAB 106 uključuje pet REV i 11 novih LAB policy slučajeva. Dodatni IDN izbor sadrži sedam IdentityLockoutOptions, 26 PasswordAndCredential, 22 Login i 12 AuthController slučajeva.

TRX class/name poređenje potvrđuje isti skup od 259 slučajeva na starom review-u i novoj probi, bez dodatih/izgubljenih slučajeva. Stari čisti roditeljski baseline imao je 182 bezbedna slučaja; to nije novo izvršavanje samostalnog baseline-a fe6507f. Realna grana i dalje ima 158, a stari pre-handoff PoC imao je 295. Od 79 originalnih Part 9 slučajeva zadržano je 40; 39 predatih delu 8 nije vraćeno. Na kombinaciji 182 + 40 + 34 PoC + 3 environment = 259.

Početni sandbox restore/build nije uspeo: NU1101 za novi Respawn paket, uz nedostupan NuGet izvor. Odobreno ponavljanje izvan sandboxa preuzelo je zavisnost i ceo backend Release build je uspeo. Ostaju NU1900 zbog nedostupnih podataka o ranjivostima i NU1504 zbog roditeljskog duplog PackageReference-a. Bezbednosni audit paketa nije potvrđen.

Sveži artefakti su u ignorisanom `TestResults/refresh-verification/`: TRX, test-summary.json, oba build loga, parent-whitespace.txt i dokumentaciona provera. Stari artefakti nisu prepisani. Posle uspešnih testova menjani su samo dokumentacioni dokazi.

### Provere isključene pre izvršavanja

1. Team 21 CoursePostgreSqlTests.MigrationArchiveAndConstraintsWorkOnPostgreSql — migracija i spoljašnja baza.
2. CoursePersistenceTests.SampleDataIsRepeatableAndNeverOverwritesExistingOrDeletedCourses i tri SampleDataIsRejectedOutsideDevelopmentAndTesting varijante — ukupno četiri seeder ulaza.
3. Dva Access ApiEndpointCoverageTests slučaja — puni Development host i startup seeder-i.
4. Novi IDN namespace Platform.Identity.Tests.Integration: LoginIntegrationTests, LogoutIntegrationTests i MeIntegrationTests, sa zajedničkim PostgreSQL/Testcontainers fixture-om, MigrateAsync i Respawn resetima.
5. Novi IDN Services.AccountLockoutTests i Controllers.LockoutEndpointTests — PostgreSQL kontejneri i migracije.
6. LAB LaboratoriesHttpTests — puni Development host sa seeder-ima.

Team 21 filter je `FullyQualifiedName!~CoursePostgreSqlTests&FullyQualifiedName!~SampleData`; Access isključuje ApiEndpointCoverageTests, LAB LaboratoriesHttpTests. IDN 19 i dodatnih 67 su eksplicitni fixture izbori; ostali IDN DB/seeder fixture-i nisu izvršeni. Ovo nije pun IDN projekat niti pun CI. Nisu pokretani migracija, seeder, PostgreSQL workflow, live Development demo ili frontend provera. Isključeni testovi nisu TRX skipped slučajevi: nisu ni izabrani za izvršavanje.

## Whitespace i dokumentacija

Naš scoped integracioni diff prema fe6507f mora biti bez whitespace grešaka. Poređenje roditelja fe6507f prema originalnom 00b3c47 ima 24 nasleđene dijagnostike: četiri ranija REV nalaza i 20 novih trailing-whitespace nalaza u IDN Helpers/Integration testovima. Detaljne putanje i redovi sačuvani su u parent-whitespace.txt. Tuđi fajlovi nisu popravljani radi zelenog rezultata; nasleđeni nalazi nisu dokaz prolaska punog CI-ja.

Proveravaju se sedam relevantnih Markdown fajlova, lokalni linkovi/anchor-i i dva postojeća Mermaid dijagrama. Dijagrami, KT1 kriterijumi, istorijski testni dokazi, Part 8 predaja i KT2 blokade sačuvani su. Dokumentacija nema korisničke apsolutne lokalne putanje.

## Odluka i naredni koraci

1. U bezbednom izvršenom opsegu nema novih testnih padova niti semantičke promene našeg pregledanog shared-host dizajna. Nije identifikovana potreba za novim funkcionalnim Part 9 patch-em ili dodatnim vlasničkim odobrenjem tog istog dizajna samo zbog ovih deset commitova.
2. Nova osnova i sveži dokazi moraju biti pregledani; lokalni merge commit može se razmatrati tek u narednom, izričito odobrenom koraku. U ovom zadatku nema commit-a ili izmene postojeće review grane.
3. IDN/IDN-03 treba da potvrde autoritativni status, mapiranje, nedostupnost i usklađivanje write/revocation putanje. ACC vlasnici potvrđuju stvarno članstvo i course permissions. LAB/persistence vlasnici rešavaju sopstvenu CourseId persistence granicu.
4. Pun CI treba da proveri nove IDN lockout/HTTP scenarije, Docker/PostgreSQL i migracionu kompatibilnost uz posebno odobren bezbedan DB postupak. Potrebna je i potvrda vulnerability podataka, parent warning/whitespace kriterijuma i tada aktuelnih remote referenci.
5. Push, PR, konačni merge u roditelja i deploy ostaju blokirani. KT1 dizajn i lokalna testna kompatibilnost nisu odobrenje produkcione upotrebe. Production deny-all nije oslabljen privremenim podacima.

Novi detached kandidat ostaje sačuvan za pregled, kao i svi prethodni worktree-i. Nema prepisivanja istorije ili vraćanja predatih Part 8 testova.

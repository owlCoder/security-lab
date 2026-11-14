# Team 21, deo 9 — KT1 dokazi i spremnost

Lokalni `feature/acc-integration-review-v2` polazi od `00b3c47` i necommitovano uključuje `fe6507f`. Originalna grana ostaje čista, a stari review i njegove četiri nestageovane dokumentacione korekcije ostaju sačuvani. Odobren KT1 dizajn prenet je bez promene produkcionog/testnog koda u odnosu na proverenu detached probu; njeni rezultati se koriste kao postojeći dokazi. Merge commit čeka zasebno završno odobrenje; push, PR i deploy nisu odobreni.

> **Lokalni review-v2:** `00b3c47` + `fe6507f`, u zasebnom novom worktree-u. [Izveštaj kandidata](MERGE_CANDIDATE.md) beleži identičnost izvora sa testiranom detached probom i razdvaja njene rezultate od istorijskih provera protiv `3b1e6ff`.

## Aktuelni izolovani kandidat

U referentnoj detached probi ponovljeno je Team 21 259/259, ciljani skup 68/68, IDN 19/19, Access 356/356, AUD 17/17 i REV 5/5. Dodatni bezbedni IDN izbor ima 67/67, a LAB izbor bez Development HTTP fixture-a 106/106 (uključuje 11 novih policy i pet REV slučajeva). Backend Release posle odobrenog restore-a prolazi sa 0 grešaka i 46 NU1504/NU1900 upozorenja. Integracioni whitespace diff prema `fe6507f` prolazi; roditeljski diff prema `00b3c47` ima 24 nasleđena nalaza, od kojih je 20 novih u IDN testovima. Tuđi fajlovi nisu popravljani. Review-v2 preuzima ove dokaze zbog identičnih produkcionih/testnih izvora; to nije novo izvršavanje testova.

Stvarna grana ostaje na `00b3c47` sa 158 testova; ovaj worktree sadrži probni spoj sa roditeljem `fe6507f` i prilagođeni Access/IDN PoC. Aktuelni bezmigracioni Team 21 izbor kandidata prolazi **259/259**, uključujući 34 preuzeta PoC slučaja i tri nove DI provere okruženja. Pet PostgreSQL/SampleData slučajeva isključeno je pre izvršavanja. Predatih 39 dodatnih testova nije vraćeno.

Roditeljski `CourseStateReader` je stvaran u svim okruženjima. Access se za naše označene rute izvršava posle jedne IDN session validacije; ostale rute zadržavaju prethodni redosled i subject provider. Production account reader ostaje deny-all, članstvo je prazno, a ACC-01 course authorization deny-all. Zato nije dokazan funkcionalan pristup legitimnog produkcionog korisnika. **Ranije je odobren KT1 dizajn; osvežena proba nije commitovana. Objava, PR, konačni merge u roditelja i Production ostaju BLOCKED.**

## Obuhvat i ocena

Prema [KT1 kontrolnoj listi](../../CHECKPOINT_1.md), KT1 traži početni kod, individualni doprinos, modele, demonstraciju i plan zavisnosti, a ne završetak svih integracija. Deo 9 ima početnu implementaciju i automatizovan HTTP tok za prezentaciju. Potpuna produkciona integracija i prolaz pune CI procedure **nisu završeni**.

| Kriterijum | Postojeći dokaz | Granica / preostali dokaz |
| --- | --- | --- |
| Funkcionalni tok | Stvarni host u Testing, IDN `SessionManager`, `SessionCurrentActor` i Team 21 servisi u HTTP testovima | InMemory skladišta i kontrolisani account/membership/course čitači; ručna prezentacija nije izvršena u ovom pregledu |
| Individualni doprinos | Lokalni milestone commitovi za ugovore, korelaciju, logger-e, adapter, aktivaciju i account HTTP testove | Nije provereno stanje Tapiz Boards zadataka niti formalno prihvatanje KT1 |
| Serverska autorizacija kandidata | Roditeljski `RequireAccess`, sesijski subject adapter, `GroupService` i `MaterialVisibilityService` | 12 označenih ruta provereno u Testing; originalni Development inventar nije izvršen zbog seedera |
| Negativni bezbednosni testovi | Nedostajuća/istekla/opozvana sesija, MFA, spoof zaglavlja, drugi kurs/grupa, promena statusa, odsustvo curenja identiteta | Nisu dokaz stvarnog IDN account reader-a ili zaštite sadržaja na LAB/ASN endpointima |
| Arhitektura, DFD, granice i pretnje | [Arhitektura](ARCHITECTURE.md), [DFD](DFD.md) i tabela pretnji ispod | Tačno dva nova Mermaid dijagrama; postojeći dijagrami drugih članova nisu menjani |
| Subject–Action–Resource–Allow/Deny | Matrica ispod prema Access i servisnom kodu kandidata | ACC-01 je uključen iz roditelja, ali non-Development course permissions i dalje deny-all |
| Međutimski ugovor | Postojeći [IDN `IIdentityContext` v1.0](../../contracts/IIdentityContext.md), korišćen kroz adapter; korisnik je prethodno potvrdio odobrenje minimalnih host/IDN izmena | Korisnik je 2026-11-08 potvrdio vlasnička odobrenja KT1 dizajna i ograničenja; to ne predstavlja remote PR review ili odobrenje drugih adaptera; MFA politika platforme ostaje nepotpuna |
| Strukturisani audit bez nepotrebnih tajni | `LoggingAuditTrailTests`, `LoggingVisibilityAuditWriterTests`, `HttpCorrelationContextTests` | Samo application log; nema potvrđenog centralnog AUD upisa ili garancije uz poslovnu transakciju |
| Setup i test instrukcije | [Team README](../README.md), komande ispod, postojeći HTTP test fixture | Razvojni PostgreSQL setup je odvojeni postupak; u ovom pregledu nema migracija |
| Backlog i plan | [Plan ugovora](INTEGRATION_PLAN.md), KT2 redosled ispod | Više izvora još nije dostupno/potvrđeno na ovoj grani |
| Git i PR tok | Lokalna podgrana ACC-06; ciljni PR prema `feature/acc06-groups` | Nema upstream-a/push-a/PR-a; roditelj je napredovao, rezultat budućeg PR-a nije proveren |
| Početni CI | Bezbedni Team 21 izbor i Release build kandidata prolaze; raniji širi backend/frontend, EF snapshot i live KT1 dokazi sačuvani su ispod | Devet starih padova nije aktuelni rezultat kandidata; puni CI, Development inventar i PostgreSQL ostaju neprovereni; objava je blokirana |

## Matrica serverskih odluka

Subject je isključivo validirani serverski actor. Pored navedenih uslova moraju proći provere identifikatora, postojanja i pripadnosti resursa kursu. HTTP ishodi izvedeni su iz postojećeg koda; ovo nisu nova pravila.

| Subject / stanje | Action | Resource | Allow / Deny i HTTP |
| --- | --- | --- | --- |
| Bez prihvaćene sesije; istek/opoziv/nepoznat token | Bilo koji Team 21 endpoint | Grupe i materijalne reference | Deny, 401 pre endpointa |
| MFA pending ili nekonzistentan identitet | Bilo koja servisna operacija | Grupe / materijali | Adapter daje anonimnog actora, Deny 401 |
| Active nalog, aktivno Teacher članstvo u traženom kursu | Čitanje grupa / članova | Grupe svog kursa | Allow, 200; grupni read ne traži posebno stanje kursa |
| Isti nastavnik, aktivan kurs/grupa i validan ulaz | Kreiranje grupe / dodavanje / uklanjanje člana | Grupa svog kursa | Allow, 201 / 201 / 204; cilj dodavanja mora biti aktivni student tog kursa |
| Blocked, Inactive ili nepoznat nalog uz važeći identitet | Bilo koji označeni Team 21 endpoint | Kurs / grupa / materijal | Access Deny, 403 pre servisa, bez zaštićenih podataka |
| Aktivan student, asistent ili nastavnik drugog kursa | Upravljanje grupama / članovima | Tuđi kurs / grupa | Deny, 404 |
| Ovlašćeni nastavnik, arhiviran kurs ili grupa | Promena grupe / članstva | Arhivirani resurs | Deny, 409 prema konkretnoj operaciji; istorija se čuva |
| Active nalog, aktivno članstvo Teacher ili Assistant sa `CanReview` | `MaterialAction.Read` / lista | Materijali poznatog aktivnog ili arhiviranog kursa | Allow, 200; ista course pripadnost je obavezna |
| Active nalog i članstvo, bez privilegije pregleda svih | Read / lista | Aktivan kurs, CourseWide ili dozvoljena aktivna grupa | Allow za odgovarajuće reference, 200 |
| Isti član, pogrešna/uklonjena grupa ili bez pravila | Read / lista | Zatvoren materijal | Read Deny 404; lista izostavlja nedozvoljene reference i može dati 200 sa praznom listom |
| Active nalog bez članstva ili nepoznat kurs | Read / lista | Materijal / kurs | Servisni Deny, 404 |
| Član bez privilegije pregleda svih, arhiviran kurs | Read / lista | Arhivirani kurs | Deny, 404 |

Mapiranje je u [ResultHttpExtensions](../../../backend/Team21.Acc.Api/Extensions/ResultHttpExtensions.cs), [GroupEndpoints](../../../backend/Team21.Acc.Api/Groups/GroupEndpoints.cs) i [MaterialEndpoints](../../../backend/Team21.Acc.Api/Groups/MaterialEndpoints.cs). Nevažeći ulaz može dati 422; nepostojanje resursa 404. Ova matrica nije dokaz sprovedenih prava u tuđim endpointima.

## Pretnje i granice poverenja

| Pretnja | Postojeća kontrola / dokaz | Preostalo ograničenje |
| --- | --- | --- |
| Lažno predstavljanje preko zaglavlja ili izmenjenog cookie-ja | IDN validira sesiju, adapter čita samo `IIdentityContext`; negativni adapter/HTTP testovi | Team 21 ne preuzima MFA, login ili politiku sesija |
| Važeća sesija za blokiran nalog | Account reader proverava se kroz servise pri svakom pozivu; isti token u kandidatu daje 200 → 403 → 200 | Testni reader; autoritativni IDN status još nije integrisan |
| IDOR: zamena course/group/material ID-a | Servisna autorizacija i čitači pravila koriste serverske odnose; pogrešan resurs ne daje sadržaj | Nema potvrđene primene ove provere u stvarnom LAB/ASN sadržaju |
| Curenje postojanja ili međugrupnih podataka | Generički 404, filtrirane liste, testovi bez zaštićenih ID-eva/naziva u odbijenim odgovorima | Nije urađena analiza vremenskih bočnih kanala |
| Curenje identiteta između zahteva | Scoped actor/context; HTTP testovi istog klijenta sa različitim/nedostajućim sesijama | Nije test opterećenja ili svih budućih registracija |
| Podmetanje korelacije i tajne u logu | Serverski AUD correlation middleware; log testovi proveravaju polja i odsustvo testnih tajni/header/body podataka | Logger nije opšti sanitizer; odabrani događaji, bez garantovanog centralnog čuvanja |
| Nepoznat/nedostupan izvor prava | Postojeće non-Development zamene vraćaju Inactive/null i ne daju pristup | Nema stvarnih adaptera, timeout/retry ugovora ili potvrđenog mapiranja kvarova u HTTP odgovor; ne obećava se 404 za svaki kvar |

Privilegovana serverska konfiguracija i kod koji formira `HttpContext.Items` ostaju u granici poverenja hosta. Klijent ne bira `ActorId`, ulogu ili korelacioni ID. Audit beleži GUID metapodatke potrebne za odluku; ne sme beležiti cookie, session token, lozinku, zahteve ili sadržaj materijala.

`LoggingAuditTrail` piše Information sa `OccurredAt`, `ActorId`, `Action`, `ResourceId`, `Outcome`, `CorrelationId`; `LoggingVisibilityAuditWriter` piše Warning sa `OccurredAt`, `ActorId`, `CourseId`, `MaterialType`, `MaterialId`, `Action`, `Reason`, `CorrelationId`. Visibility interfejs podržava samo odbijanja. Nisu sva odbijanja, uspešna čitanja ili HTTP 401 pokriveni ovim writer-ima.

## Reprodukcija i kratka prezentacija

Komande iz korena repozitorijuma koriste .NET 10 i ne zahtevaju PostgreSQL. Testovi sami formiraju sintetičke sesije, EF InMemory baze i kontrolisane spoljne čitače; ne zahtevaju stvarne kredencijale. Pre host testova proveriti da lokalni `.env`, user secrets i `ConnectionStrings:Identity` ne omogućavaju automatske Development startup migracije. Glavni HTTP fixture koristi Testing; mali Development testovi koriste izolovani pipeline bez punog startup-a.

```powershell
dotnet build backend/Team21.Acc.slnx --configuration Release
dotnet test backend/Team21.Acc.Tests/Team21.Acc.Tests.csproj --configuration Release --no-build --filter FullyQualifiedName~SessionCurrentActorHostIntegrationTests
dotnet test backend/Team21.Acc.Tests/Team21.Acc.Tests.csproj --configuration Release --no-build --filter 'FullyQualifiedName!~CoursePostgreSqlTests&FullyQualifiedName!~SampleData'
```

Za prezentaciju prikazati `ValidatedTeacherReachesRealServiceAndSuppliesCreatedBy`, `MaterialVisibilityUsesSessionUserDespiteSpoofedGroupMemberHeader` i `AccountStatus_ChangesAreEnforcedOnNextRequestDespiteValidSession`. Objasniti da poslednji slučaj menja samo testni status reader, zadržava isti realno validirani token i u kandidatu dobija 200 → 403 → 200 bez zaštićenih podataka pri odbijanju. To je izvršiv početni HTTP tok, a ne ručno demonstriran login sa stvarnim statusom naloga. Produkcioni pokušaj sa demo header-om ne predstavlja ovu demonstraciju.

## Predaja testova delu 8

Commit `3fca69c` smanjio je samo testove prvobitno dodate u sedam commitova dela 9: od 79 slučajeva zadržano je 40 ključnih, a 39 dodatnih predato je članu 8. Svih 118 ranijih slučajeva, uključujući testove drugih članova za deo 5, ostalo je neizmenjeno. Ukupno na stvarnoj grani sada postoji **158 slučajeva**, umesto ranijih 197. Deo 5 ostaje ACC-06 backend upravljanje grupama; deo 9 integracije, ugovori i audit; deo 8 sveobuhvatno automatizovano bezbednosno i integraciono testiranje.

Putanje u tabeli su unutar `backend/Team21.Acc.Tests/`:

| Test fajl | Pre predaje | Zadržano | Predato delu 8 |
| --- | --- | --- | --- |
| `Api/HttpCorrelationContextTests.cs` | 9 | 4 | 5 |
| `Api/SessionCurrentActorTests.cs` | 20 | 12 | 8 |
| `Api/SessionCurrentActorHostIntegrationTests.cs` | 41 | 19 | 22 |
| `Infrastructure/LoggingAuditTrailTests.cs` | 4 | 3 | 1 |
| `Groups/LoggingVisibilityAuditWriterTests.cs` | 5 | 2 | 3 |
| Ukupno iz dela 9 | 79 | 40 | 39 |

Pre prve izmene sačuvani su kompletni originali ovih pet fajlova i `Support/RecordingLogger.cs` u spoljašnjem **`Team21-Part8-Handoff.zip`**. Arhiva ima 12 stavki: izvore, projektni kontekst, poreklo commitova, README za integraciju, klasifikaciju svih slučajeva i kontrolne sume. ZIP i hash-evi svih stavki su provereni. Arhiva je izvan repozitorijuma i privremenih worktree-a, dobija se od vlasnika predaje; ne sadrži kredencijale ni build izlaze. Originali ostaju i u Git istoriji pre smanjenja, na primer na `0c05a16`; autorstvo prethodnih Part 9 commitova se ne menja.

Zadržani su osnovni pozitivni i negativni tokovi za grupe i dozvole, validirani identitet, odbijanje lažnih/nedostajućih sesija, IDOR, audit i serversku korelaciju. Od account-status HTTP matrice ostala su dva prelaza Active → Blocked/Inactive → Active i jedna zabrana kreiranja grupe blokiranom nastavniku. Preostale asercije i zajednički fixture-i nisu menjani; dodatnu pokrivenost član 8 integriše bez dupliranja postojećih klasa i uz očuvanje zavisnosti ciljne grane. Arhiva ne uključuje nove testove privremenog Access/IDN PoC-a. Smanjenje prolaznih testova nije rešilo CI ili merge blokade i ne dokazuje stvarni IDN status reader.

## Lokalna verifikacija i CI blokade

### Rezultati stvarne grane posle predaje — istorijski za ovaj kandidat

Za commit `3fca69c` ponovljene su provere na stvarnoj integracionoj grani. Lokalni ignorisani TRX dokaz za kompletan skup je pod `TestResults/test-handoff-388c8b4c/commit-review/`; nije deo dokumentacionog commita.

| Provera | Aktuelni rezultat / ograničenje |
| --- | --- |
| Svi preostali Team 21 testovi | **158/158 prolazi, 0 neuspešnih i 0 preskočenih** |
| Zadržani slučajevi dela 9 | 40/40 prolazi u pregledu predaje; podskup navedenih 158 |
| Ciljani deo 5: `GroupServiceCreateTests` i `GroupServiceReadAuthorizationTests` | 21/21 prolazi u pregledu predaje; neizmenjeni testovi, podskup navedenih 158 |
| Team 21 Release, `backend/Team21.Acc.slnx` | 0 upozorenja / 0 grešaka; korišćeni izdvojeni artefakti zbog zaključanih DLL-ova standardnog API izlaza |

Kod izdvojenog testnog izlaza korišćen je lokalni `runsettings` sa eksplicitnim API content root-om; nije menjan produkcioni ili testni kod radi tog podešavanja. Nisu izvršavane migracije ni produkcioni seeder-i. Predatih 39 slučajeva nije preskočeno tokom završnog run-a: oni su uklonjeni iz ovog skupa nakon proverene arhive. Širi backend, frontend, PostgreSQL i puni CI nisu ponovo proveravani u ovoj dokumentacionoj dopuni.

### Istorijski KT1 rezultati pre predaje

Dokazi ranijeg KT1 pregleda su lokalni ignorisani TRX fajlovi pod `TestResults/team21-kt1/`, nisu deo commita. Sledeća tabela i postupci beleže istorijske rezultate na `c94882e`, pre predaje testova; nisu nova verifikacija na `3fca69c`.

| Provera | Rezultat / ograničenje |
| --- | --- |
| Backend Release, `backend/SecurityLab.slnx` i `backend/Team21.Acc.slnx` | Obe komande uspešne, po 0 upozorenja / 0 grešaka |
| Team 21 testovi pre predaje | Istorijski: 197/197 prolazi, 0 preskočenih; uključeni u tadašnji širi bezmigracioni skup |
| Širi backend skup bez migracionih/DB scenarija | 1.349 prolazi, 9 pada, 0 preskočenih; dva projekta bez izabranih testova; nije kompletan CI skup |
| REV-05 domain/model | 61/61 prolazi u navedenom skupu |
| REV-05 snapshot, `dotnet ef migrations has-pending-model-changes` | Nema modelskih promena; ne primenjuje migracije niti menja bazu |
| Frontend `npm ci --no-audit --no-fund`, `npm run build`, `npm test` | Sve tri komande uspešne; 60/60 testova, osam test fajlova. Install prijavljuje engine upozorenja: deo zavisnosti traži Node 24.15+ ili 22.22.2+, lokalno je 24.14.1 |
| Mermaid i lokalni linkovi | Mermaid parser 12.3.0 prihvata oba flowchart bloka; provereno 67 lokalnih linkova i zatvoreni code fence blokovi u šest dokumentacionih fajlova. Nije objavljen remote GitHub prikaz |
| PostgreSQL, Audit runtime permissions i REV-05 persistence | Nisu izvršeni: zahtevaju migracije, zabranjene u ovom zadatku; lokalni Docker klijent nije uspeo da se poveže na `docker_engine` |
| Zaseban workflow live KT1 demo | `scripts/kt1-demo.ps1`: 41/41 HTTP provera prolazi na izolovanom lokalnom Development hostu; 65/65 `Platform.Kt1.Tests` takođe prolazi. To je postojeći ACC-04/ADM-08 demo, nije novi Team 21 end-to-end dokaz |

Bezmigracioni izbor je izvršen komandom:

```powershell
dotnet test backend/SecurityLab.slnx --configuration Release --no-build --filter 'TestCategory!=Integration&FullyQualifiedName!~Postgres&FullyQualifiedName!~AuditRuntimePermissionsTests&FullyQualifiedName!~AccountRegistrationTests&FullyQualifiedName!~DevelopmentDatabaseTests&FullyQualifiedName!~LaboratoryPersistenceIntegrationTests' --logger trx --results-directory TestResults/team21-kt1/backend-nonmigration
```

Isključeni su i pojedini testovi koji se mogu bezbedno izvršiti zasebno; broj nije ukupan broj testova repozitorijuma. Pre izvršavanja potvrđeno je odsustvo `.env`, relevantnih IDN/API user-secrets i DB environment konfiguracije koja bi dozvolila automatske migracije u Development host testovima.

Za zaseban CI demo provereno je odsustvo svih `ConnectionStrings*`/`AUDIT_TEST*` environment podešavanja i relevantnih secrets; host je pokrenut sa `Kt1Demo__Enabled=true`, apsolutnim `contentRoot` i slobodnim lokalnim portom, pa ugašen. Nije podešena DB konekcija, pa postojeći startup preskače IDN migracije; nijedna migracija nije primenjena. Rezultat skripte je u ignorisanom `TestResults/acc04-adm08-http-demo.json`. Prvi lokalni pokušaj sa relativnim `contentRoot` stao je pre startup-a; ponovljen je sa ispravljenom apsolutnom putanjom, bez promene aplikacije ili CI skripte.

Devet padova ponovo potvrđenih tokom tog ranijeg KT1 pregleda:

- ACC-05 `ACC05_Every_real_route_is_protected_or_approved_public`: osam Team 21 ruta nije u `ApprovedDelegatedRoutes`; potrebno odobrenje inventara sa Access vlasnikom, uz očuvanje servisne autorizacije.
- REV `Approve_WithoutRegisteredPolicy_DeniesBeforeDatabase(True,Forbidden)`: očekivan 403, dobijen 401; test nema validiranu IDN sesiju.
- AUD `Request_CorrelationIdIsAcceptedByAuditValidator`, `Request_LogsWrittenDuringRequestCarryCorrelationScope`, `TwoRequests_GetDifferentCorrelationIds`: 401 na probnoj ruti.
- AUD `Request_GetsServerIdInContextTraceIdentifierAndHeader` i tri slučaja `Request_WithClientCorrelationHeader_IgnoresClientValue` (`attacker-chosen-id`, `corr-0001`, `0123456789abcdef0123456789abcdef`): JSON parsiranje pada jer probna ruta vraća prazan 401 odgovor.

Ove zabeležene integracione blokade ostaju otvorene na stvarnoj grani. **Nisu dokazano pre-existing baseline padovi**: u tom KT1 pregledu nije testirana neizmenjena roditeljska grana. Uzroci i vlasnički fajlovi su navedeni u [planu, odeljak 9.1](INTEGRATION_PLAN.md#91-zatečene-integracione-blokade-van-dokumentacionog-zadatka); nisu popravljani tuđi testovi. Predaja 39 prolaznih slučajeva ne rešava te padove i ne predstavlja novu proveru punog CI-ja.

[Starter checks](../../../.github/workflows/ci.yml) zahteva backend build, `scripts/audit/test-postgres.ps1 -NoBuild`, izvršene TRX testove, live KT1 demo i frontend install/build/test. PostgreSQL skripta primenjuje migracije; nije pokrenuta. [REV-05 checks](../../../.github/workflows/rev05.yml) zahteva domain/model, PostgreSQL integration i EF snapshot proveru. To su zahtevi verzionisanih workflow-a; remote branch-protection pravila nisu proverena. Lokalni Windows/.NET 10 i Node 24.14.1 nisu isto okruženje kao Ubuntu/Node 22 u CI.

### Prethodni privremeni merge eksperiment

Prethodni bezmigracioni izbor od **295 Team 21 slučajeva** pripada privremenom merged worktree-u sa roditeljskim izmenama i Access/IDN PoC-om, na osnovi `0c05a16` pre predaje testova. To nije broj slučajeva na stvarnoj grani niti dokaz prolaska svih 295 na njoj. Aktuelnih **158/158** odnosi se samo na stvarnu granu na `3fca69c`.

Access/IDN PoC i njegovi novi testovi nisu preneti niti spojeni u stvarnu granu. U starom PoC-u i novom kandidatu Access odbija neaktivne naloge sa 403; na stvarnoj grani ostaju prethodna HTTP očekivanja 404. Novi kandidat prilagođava samo preostala tri account-status slučaja, uz očuvanje servisnih 404 zabrana i svih provera privatnosti i upisa. Izvezene slučajeve član 8 mora naknadno uskladiti sa odobrenim Access ugovorom. Nijedan eksperiment ne potvrđuje autoritativni IDN account-status izvor niti produkcionu spremnost. Postojeći stari worktree-i nisu menjani; objava i merge ostaju blokirani.

## KT2 redosled i vlasništvo

1. Zadržati odobrenu zajedničku ACC-01/06 osnovu u lokalnoj review integraciji. Roditelj već ima model, read-only `CourseStateReader` i DI; ne implementirati duplikat. Posebno dokazati PostgreSQL ponašanje kada bude odobreno.
2. Sa IDN-03 potvrditi autoritativni status reader i mapiranje Active/Blocked/Archived; host-ov privremeni `InMemoryAccountRepository` nije odobren produkcioni izvor. Ne implementirati IDN skladište ili updates. `IIdentityContext.Current` ne sadrži status naloga.
3. Sa ACC-02/03/07 potvrditi stvarno članstvo, uloge i semantiku delegiranog `CanReview`, zatim zameniti fake/deny-all čitače. Sa ACC-02 povezati zabranu upisa u arhiviran kurs.
4. Sa LAB/ASN/AUT primeniti autorizaciju pre vraćanja sadržaja i dogovoriti upis publike; sadašnji referentni endpoint nije zaštita svih putanja sadržaja.
5. Sa AUD dogovoriti katalog događaja/resursa/razloga, adapter i pouzdanost upisa uz promene. Ne praviti paralelnu centralnu audit infrastrukturu.
6. KT1 selektivni middleware redosled i subject dekorator odobreni su prema potvrdi korisnika od 2026-11-08; zadržati dokumentovane granice. AUD/REV relevantne provere prolaze na starom roditeljskom baseline-u i novom kandidatu; samostalni fe6507f baseline nije pokretan. Development ACC-05 inventar nije izvršen. Uz posebno odobren DB postupak proveriti PostgreSQL, potpune workflow-e i budući PR rezultat.

## Git i objavljivanje

Istorijska Git provera za KT1 zabeležila je `origin` fetch/push URL `https://github.com/owlCoder/security-lab.git`, lokalnu granu bez upstream-a, zajedničkog pretka sa `origin/feature/acc06-groups` na `736b8f2`, roditelja na `3b1e6ff` i udaljeni `main` na `e744fab` sa ACC-01. Tada je roditelj imao 106 svojih commitova, a radna grana šest. Posle dokumentacionog `0c05a16` i predaje testova u `3fca69c` to više nije aktuelni broj commitova; u ovoj dopuni nije rađen novi fetch niti objava grane.

Preporučeni PR target je **`feature/acc06-groups`**, prema stvarnom poreklu grane i dogovorenom Team 21 toku. Veliko napredovanje roditelja zahteva pregled buduće integracije i ponovno testiranje; roditeljske/PoC izmene nisu spojene u stvarnu granu niti je ona rebase-ovana. Odobrenje lokalnog KT1 merge-a nije odobren rezultat PR-a i ne dokazuje da će kasnije spajanje biti bez konflikata. Formalni remote review, Tapiz statusi i branch-protection podešavanja nisu potvrđeni.

**Push ostaje zabranjen**: istorijskih devet padova ne predstavlja aktuelni rezultat kandidata, ali puni CI nije potvrđen, produkcioni account/membership/permission izvori blokiraju pristup a odobrenje obuhvata samo lokalni merge, ne objavu. Ne pravi se PR. Tek nakon rešavanja blokada sa vlasnicima, kompletnog propuštenog testiranja i provere tada aktuelnih referenci može se razmotriti normalan `git push -u origin feature/acc-integration`, bez force-a. Postojeći workflow-i se pokreću na PR i push na `main`; sam push ove feature grane ne garantuje CI izvršavanje.

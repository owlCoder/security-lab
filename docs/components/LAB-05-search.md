# Komponenta projekta: LAB-05, pretraga laboratorija po nazivu

**Tim:** 19

**Student:** Zorana Jošić PR57/2023

**Zahtevi iz specifikacije:** LAB-05 (Pretraga i filtriranje, nivo J). Kriterijum prihvatanja: *„Rezultati poštuju prava pristupa korisnika.”* Ovaj deo pokriva dve korisničke priče iz LAB-05:

- *„Kao korisnik, želim da pretražujem laboratorije po nazivu ili delu naziva, kako bih lakše pronašao vežbu.”*
- *„Kao korisnik, želim da u rezultatima pretrage vidim samo laboratorije kojima imam pravo pristupa.”* (Bezbedan pristup rezultatima pretrage)

Zajednički zahtevi koje deo pokriva: SEC-01 (serverska autorizacija, nema IDOR-a kroz pretragu), SEC-05 (validacija ulaza i bezbedan prikaz teksta), LAB-04 (student ne vidi nacrt, ni kroz pretragu), ACC-05 (podrazumevano odbijanje nepoznate radnje), OPS-02 (pozitivni i negativni automatski testovi), OPS-07 (pristupačnost i poruke o grešci bez tajni).

**Grane:** `feature/LAB-05-search-by-name` (pretraga po nazivu), `feature/LAB-05-secure-search-results` (bezbedan pristup rezultatima, nastavlja se na prethodnu)

## Odgovornosti i granice

- **Procesi i scenariji:**
  - Korisnik unese naziv ili deo naziva: `GET /api/laboratories/search?q=<tekst>` (isto i preko aliasa `/api/labs/search`).
  - Server vraća `200 { "items": [...], "truncated": false }`. Stavka sadrži samo ono što je potrebno za prikaz rezultata: `id`, `title`, `status`, `createdAt`. Interni ID autora (`createdBy`) se ne šalje u rezultatima pretrage. Odgovor ima `Cache-Control: no-store`, pa ga browser i proxy ne čuvaju.
  - Poređenje ne razlikuje velika i mala slova, kvačice (`sifr` nalazi „Šifrovanje”, `kljuc` nalazi „ključevi”, `đ` = `d`) ni višestruke razmake. Upit se traži kao podniz naziva (deo naziva), a ne kao šablon: `%`, `*` i `' OR '1'='1` su običan tekst.
  - Redosled: prvo nazivi koji počinju upitom, zatim abecedno; najviše 50 rezultata (`truncated: true` kad ih ima više).
  - Frontend: panel „Pretraga laboratorija” (oznaka LAB-05) u `LabsSection`, ispod forme za kreiranje i prikaza.
- **Šta ovaj deo NE radi:** ne uvodi filtere po osobinama (tip, težina, oznake) i dodatne filtere; to su naredne priče iz LAB-05. Ne menja ko sme da vidi koju laboratoriju: koristi postojeće pravilo vidljivosti i postojeću politiku čitanja. Ne menja skladište (`ILaboratoryRepository`, `Platform.LaboratoryPersistence`) ni Identity/Access module drugih timova.
- **Ugovori sa drugim delovima:**

| Veza | Svrha | Stanje |
| --- | --- | --- |
| `IIdentityContext` (lokalni port LAB modula) | ko je pozivalac | razvoj: `FakeIdentityContext` (`X-Test-User`); van Development-a `AnonymousIdentityContext` |
| `IAccessDecision` (lokalni port LAB modula) | (1) sme li se pretraga pozvati: radnja `laboratory.search`; (2) sme li pozivalac da čita **svaku konkretnu** laboratoriju: radnja `laboratory.read` sa ID-em laboratorije | razvoj: `FakeAccessDecision`; van Development-a `DenyAllAccessDecision`. Planiran adapter na `Platform.Access` (predlog u `LAB-01-LAB-07.md`) |
| `LaboratoryVisibility` (novo, deljeno sa LAB-01) | jedno pravilo vidljivosti za čitanje po ID-u i za pretragu | trenutno: laboratoriju vidi samo autor. Proširenje (kurs, članstvo, objavljena revizija, LAB-03) menja se samo ovde |
| `ILaboratoryRepository.ListAsync` | izvor laboratorija | koristi se postojeći ugovor bez izmena (radi i sa `InMemory` i sa `Postgres` adapterom) |
| Frontend `api/labs.ts` → `searchLabs` | jedino mesto za poziv pretrage | prevodi `INVALID_QUERY`, 401, 403 i greške mreže u naše poruke |

- **Stanje implementacije:** endpoint, servis, pravila upita, frontend panel i testovi su implementirani. Pretraga se radi u servisu nad `ListAsync` (dovoljno za KT1). Spuštanje upita u bazu je naredni korak koji zahteva dogovor sa timom za skladište, a provera prava po laboratoriji ostaje u servisu.

## Matrica odluka o pristupu (Subject – Action – Resource)

| Subjekat | Radnja | Resurs | Odluka |
| --- | --- | --- | --- |
| anoniman / neispravno `X-Test-User` / više zaglavlja | `laboratory.search` | pretraga | Deny, 401 (pre servisa) |
| Student, Nastavnik, Asistent, Administrator | `laboratory.search` | pretraga | Allow: poziv je dozvoljen, ali rezultat se filtrira po laboratoriji |
| Nastavnik, autor | `laboratory.read` | sopstvena laboratorija | u rezultatu, ako naziv odgovara upitu |
| Nastavnik, nije autor | `laboratory.read` | tuđa laboratorija | nije u rezultatu (ni naslov ni ID se ne pojavljuju) |
| Student (ista osoba i kad je autor u drugoj ulozi) | `laboratory.read` | nacrt | nije u rezultatu (LAB-04) |
| Asistent, Administrator (i kad je ista osoba autor) | `laboratory.read` | nacrt | nije u rezultatu; tehnička uloga ne daje automatski pristup sadržaju (specifikacija, pogl. 2) |
| bilo ko, posle oduzimanja prava | `laboratory.read` | ta laboratorija | u sledećoj pretrazi više nije u rezultatu; prava se proveravaju u svakom zahtevu |
| bilo ko, van Development okruženja | bilo koja | bilo koji | Deny, 401 |

Ključno pravilo: **laboratorija je u rezultatu pretrage samo ako bi je isti korisnik mogao i da otvori preko `GET /api/laboratories/{id}`.** Zato pretraga koristi isto pravilo vidljivosti (`LaboratoryVisibility`) i istu radnju `laboratory.read` kao čitanje po ID-u. To dokazuje test `Search_EveryResultCanBeOpenedBySameCaller`.

## Dijagram arhitekture

Puna strelica označava postojeću vezu, isprekidana planiranu integraciju.

```mermaid
flowchart TB
  subgraph FE["frontend/src (React / TypeScript)"]
    SEC["LabsSection"] --> PICK["TestUserPicker (FAKE)"]
    SEC --> SEARCH["LabSearch (LAB-05)<br/>forma role=search, lista rezultata"]
    SEARCH --> API["api/labs.ts: searchLabs()<br/>URLSearchParams, poznati kodovi grešaka"]
  end

  API -->|"GET /api/laboratories/search?q="| ROUTES

  subgraph BE["Platform.Api / Modules/Laboratories"]
    ROUTES["LaboratoriesEndpoints<br/>GET /search (i /api/labs/search)"]
    SAFE["SafeErrors<br/>genericki 500 + traceId"]
    FILTER["AccessCheckFilter<br/>RequireLabAccess(laboratory.search): 401/403"]
    QUERY["čitanje parametra q<br/>tačno jedna vrednost (400)"]
    SVC["LaboratorySearchService<br/>provera prava po laboratoriji, poređenje, redosled, limit 50"]
    TERM["LabTitleSearch<br/>validacija upita, Fold: mala slova, bez kvačica"]
    VIS["LaboratoryVisibility<br/>(deljeno sa LaboratoryService, LAB-01)"]
    REPO[["ILaboratoryRepository.ListAsync"]]

    ROUTES --> SAFE --> FILTER --> QUERY --> SVC
    SVC --> TERM
    SVC --> VIS
    SVC --> REPO
  end

  FILTER --> IDC["IIdentityContext<br/>Development: Fake / ostalo: Anonymous"]
  FILTER --> ACD["IAccessDecision<br/>Development: Fake / ostalo: DenyAll"]
  SVC -->|"laboratory.read za svaku laboratoriju"| ACD
  REPO --> MEM[("InMemoryLaboratoryRepository")]
  REPO --> PG[("PostgresLaboratoryRepository<br/>ako postoji ConnectionStrings:Authoring")]
  ACD -.-> ACC["Platform.Access (planiran adapter)"]
  VIS -.-> COURSE["LAB-03 / ACC: kurs i članstvo (planirano)"]
```

## DFD sa granicom poverenja klijent–server

```mermaid
flowchart LR
  U["Korisnik<br/>(nepouzdana zona)"] -->|"tekst pretrage"| FE

  subgraph CLIENT["Klijent: nepouzdana zona"]
    FE["LabSearch + api/labs.ts<br/>šalje q i X-Test-User"]
  end

  subgraph SERVER["Server: granica poverenja"]
    P1(("1. Identitet<br/>IIdentityContext"))
    P2(("2. Sme li pretragu<br/>laboratory.search"))
    P3(("3. Validacija upita<br/>jedan q, 1–200 znakova,<br/>bez kontrolnih znakova"))
    P4(("4. Filtriranje po pravima<br/>LaboratoryVisibility +<br/>laboratory.read po laboratoriji"))
    P5(("5. Poređenje naziva<br/>Fold, redosled, limit 50"))
    D1[("Laboratorije<br/>ILaboratoryRepository")]
    LOG[("Serverski log<br/>pozivalac, dužina upita,<br/>broj rezultata (bez upita)")]

    P1 -->|"subjekat ili anoniman"| P2
    P2 -->|"dozvoljeno"| P3
    P3 -->|"ključ upita"| P4
    D1 -->|"sve laboratorije (ListAsync)"| P4
    P4 -->|"samo dozvoljene laboratorije"| P5
    P5 -->|"metapodaci"| LOG
    P2 -->|"odbijanje i razlog"| LOG
  end

  FE ==>|"HTTP GET preko granice poverenja"| P1
  P1 -->|"401"| FE
  P2 -->|"401 / 403"| FE
  P3 -->|"400 INVALID_QUERY"| FE
  P5 -->|"200 items, truncated"| FE
```

Sve što dolazi preko granice poverenja je nepouzdano: parametar `q`, eventualni dodatni parametri i `X-Test-User`. Tuđe laboratorije se uklanjaju **pre** poređenja naziva, pa nijedan podatak o njima ne stiže do koraka 5 ni do odgovora. Upit se ne vraća klijentu u poruci o grešci i ne upisuje se u log.

## Model pretnji: imovina → zloupotreba → kontrola → test

| Imovina | Moguća zloupotreba | Kontrola | Test |
| --- | --- | --- | --- |
| Tuđi nacrti (naslov, ID) | nastavnik pretragom „prelista” tuđe laboratorije (IDOR preko liste) | `LaboratoryVisibility` + `laboratory.read` za svaku laboratoriju, pre poređenja | `Search_DoesNotReturnLabsOfAnotherTeacher` (HTTP i servis) |
| Nacrti pred studentima (LAB-04) | student pretragom otkrije nacrt | ista politika `laboratory.read` kao za `GET /{id}`: student ne čita nacrt | `Search_WhenStudentSearches_DoesNotSeeDrafts`, `Search_WhenSameUserHasStudentRole_DoesNotSeeOwnDraft` |
| Doslednost prava | pretraga pokaže nešto što se ne može otvoriti | zajedničko pravilo vidljivosti i ista radnja čitanja | `Search_EveryResultCanBeOpenedBySameCaller`, `Search_ChecksReadPermissionForEachLaboratory` |
| Razvojni identitet | `X-Test-User: marko:Teacher` u produkciji | fake samo u Development-u; drugde `Anonymous` + `DenyAll` | `OutsideDevelopment_TestTeacherHeaderDoesNotGrantSearch` |
| Integritet upita | `?q=a&q=b`, prazan, predug ili upit sa `\n`/`\0` | tačno jedan `q`, 1–200 znakova posle `Trim()`, kontrolni znakovi se odbijaju | `Search_WithMissingBlankOrInvalidQuery_Returns400WithoutInternalDetails`, `LabTitleSearchTests` |
| Svi zapisi | „džoker” upit (`%`, `*`, `' OR '1'='1`) vrati sve | upit je običan podniz, nije SQL ni šablon | `Search_WithWildcardOrInjectionLikeText_TreatsItAsPlainText`, `Search_WithInjectionLikeQuery_...` |
| Prikaz u browseru (XSS) | naslov `<img onerror=...>` izvrši skriptu | JSON odgovor; React prikazuje naslov kao tekst, bez `dangerouslySetInnerHTML` | `Search_HtmlInTitle_IsReturnedAsJsonData`, `LabSearch.test.tsx` „HTML u nazivu se prikazuje kao tekst” |
| Interni detalji servera | greška skladišta otkrije connection string ili stack trace | `SafeErrors`: generički 500 + `traceId` | `Search_WhenStorageFails_Returns500WithoutInternalDetails` |
| Resursi servera | ogroman odgovor za širok upit | najviše 50 rezultata, `truncated` | `Search_ReturnsAtMostMaxResultsAndMarksTruncated` |
| Rezultati prethodnog korisnika | posle promene korisnika na ekranu ostanu tuđi rezultati | `LabSearch` briše rezultate pri promeni korisnika i posle odbijanja | `LabSearch.test.tsx` „promena korisnika briše rezultate”, „posle odbijanja (401)…” |
| Identitet autora | rezultati pretrage otkriju interni ID drugog korisnika | stavka pretrage (`LaboratorySearchItem`) nema `createdBy` | `Search_ResultsDoNotExposeAuthorId` |
| Rezultati na deljenom računaru | browser ili proxy sačuva rezultate i pokaže ih sledećem korisniku | `Cache-Control: no-store` na svakom odgovoru pretrage (i 200 i 400) | `Search_ResponseIsNeverCached` |
| Nacrti pred tehničkim ulogama | asistent ili administrator pretragom vidi tuđi nacrt | ista politika `laboratory.read`: samo nastavnik-autor čita nacrt | `Search_WhenAssistantOrAdministratorSearches_DoesNotSeeDrafts` (HTTP i servis) |
| Oduzeto pravo | posle oduzimanja prava laboratorija i dalje stiže u rezultatima | provera prava u svakom zahtevu, bez keširanja odluke | `Search_RechecksAccessOnEveryRequest` |
| Broj skrivenih laboratorija | `truncated` ili broj rezultata otkrije da postoje tuđe laboratorije sa istim nazivom | limit i `truncated` se računaju tek posle filtriranja po pravima | `Search_HiddenLaboratoriesDoNotAffectLimitOrTruncation` |
| Delimični rezultati | greška pri proveri prava za jednu laboratoriju vrati ostale bez provere | greška prekida celu pretragu; klijent dobija generički 500 bez rezultata | `Search_WhenAccessCheckFails_ReturnsNoPartialResults`, `Search_WhenAccessCheckFails_Returns500WithoutResults` |

## Bezbednosni zahtevi

- **Provere na serveru:** ruta ima `RequireLabAccess(laboratory.search)` (401/403 pre servisa). Servis ponovo proverava identitet i za svaku laboratoriju traži `laboratory.read` od `IAccessDecision` sa ID-em te laboratorije. Frontend ne filtrira po pravima; on samo prikazuje ono što server vrati (specifikacija, pogl. 1: „Klijentski interfejs nije bezbednosna granica”).
- **Validacija ulaza:** parametar `q` se čita ručno: tačno jedna vrednost, posle `Trim()` od 1 do 200 znakova (najduži dozvoljeni naslov), bez ijednog kontrolnog znaka. Inače `400 INVALID_QUERY` sa fiksnom porukom koja ne ponavlja upit.
- **Osetljivi podaci:** odgovor ne sadrži tuđe laboratorije ni broj tuđih pogodaka. Stavka rezultata nema interni ID autora. Odgovor se ne kešira (`Cache-Control: no-store`). Log beleži pozivaoca, dužinu upita i broj rezultata, ali ne i sam upit (proizvoljan korisnički tekst).
- **Odbijanje u slučaju greške:** ako provera prava za bilo koju laboratoriju ne uspe, pretraga se prekida i ne vraća delimične rezultate; klijent dobija generički 500 sa `traceId`.
- **Poznata ograničenja:**
  - Pravilo vidljivosti je za sada „samo autor”, kao u LAB-01. Kad LAB-03/ACC uvedu kurs i članstvo, menja se `LaboratoryVisibility` i pretraga to automatski nasleđuje.
  - `X-Test-User` je simulacija identiteta (isto ograničenje kao LAB-01).
  - Pretraga čita sve laboratorije kroz `ListAsync` i filtrira u memoriji. Za veliki broj laboratorija upit treba spustiti u skladište (npr. normalizovana kolona i indeks), uz dogovor sa timom za skladište.
  - Ograničenje broja zahteva (SEC-04) za pretragu nije dodato: postojeći rate limiter pripada Identity modulu (IDN-05), pa je politiku za pretragu potrebno dogovoriti sa tim timom.

## Dokaz rada

- **Fajlovi (backend):**
  - `backend/Platform.Api/Modules/Laboratories/Application/LaboratorySearchService.cs` (slučaj upotrebe, provera prava po laboratoriji, redosled, limit)
  - `backend/Platform.Api/Modules/Laboratories/Application/LabTitleSearch.cs` (validacija upita i normalizacija)
  - `backend/Platform.Api/Modules/Laboratories/Application/LaboratoryVisibility.cs` (zajedničko pravilo vidljivosti; `LaboratoryService` ga sada koristi umesto privatne metode, bez promene ponašanja)
  - `backend/Platform.Api/Modules/Laboratories/Application/LabActions.cs` (`laboratory.search`)
  - `backend/Platform.Api/Modules/Laboratories/Infrastructure/FakeAccessDecision.cs` (politika za `laboratory.search`)
  - `backend/Platform.Api/Modules/Laboratories/Api/LaboratoriesEndpoints.cs` (ruta `GET /search`), `LaboratoryContracts.cs` (`LaboratorySearchResponse`, `INVALID_QUERY`)
  - `backend/Platform.Api/Modules/Laboratories/LaboratoriesModule.cs` (registracija servisa)
- **Fajlovi (frontend):** `frontend/src/api/labs.ts` (`searchLabs`), `frontend/src/components/labs/LabSearch.tsx`, `labSearch.css`, `LabsSection.tsx` (novi panel).
- **Testovi:**
  - `backend/Laboratories.Tests/LabTitleSearchTests.cs`, `LaboratorySearchServiceTests.cs`, `LaboratorySearchHttpTests.cs`
  - `frontend/src/components/labs/labSearchApi.test.ts`, `LabSearch.test.tsx`
- **Pokretanje testova** (iz korena repozitorijuma):

```powershell
dotnet test backend/Laboratories.Tests/Laboratories.Tests.csproj --configuration Release
dotnet test backend/SecurityLab.slnx --configuration Release
cd frontend; npm ci; npm run build; npm test
```

- **Ručna demonstracija** (Development, API na portu 5080):

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project backend/Platform.Api/Platform.Api.csproj --no-launch-profile --urls http://localhost:5080

# drugi terminal: dve laboratorije nastavnika marko, jedna nastavnice jelena
curl.exe -s -X POST http://localhost:5080/api/laboratories -H "X-Test-User: marko:Teacher" -H "Content-Type: application/json" -d '{\"title\":\"RBAC i kontrola pristupa\"}'
curl.exe -s -X POST http://localhost:5080/api/laboratories -H "X-Test-User: marko:Teacher" -H "Content-Type: application/json" -d '{\"title\":\"Analiza logova\"}'
curl.exe -s -X POST http://localhost:5080/api/laboratories -H "X-Test-User: jelena:Teacher" -H "Content-Type: application/json" -d '{\"title\":\"RBAC - Jelenin nacrt\"}'

# 1) marko trazi deo naziva -> 200, samo "RBAC i kontrola pristupa"
curl.exe -i "http://localhost:5080/api/laboratories/search?q=rbac" -H "X-Test-User: marko:Teacher"
# 2) jelena isti upit -> 200, samo njen nacrt (Markov se ne vidi)
curl.exe -i "http://localhost:5080/api/laboratories/search?q=rbac" -H "X-Test-User: jelena:Teacher"
# 3) student -> 200, prazna lista (nacrti nisu za studente)
curl.exe -i "http://localhost:5080/api/laboratories/search?q=rbac" -H "X-Test-User: ana:Student"
# 4) bez identiteta -> 401; prazan upit -> 400 INVALID_QUERY
curl.exe -i "http://localhost:5080/api/laboratories/search?q=rbac"
curl.exe -i "http://localhost:5080/api/laboratories/search?q=" -H "X-Test-User: marko:Teacher"
```

### Izvršena lokalna provera

Provera je izvršena 5. novembra 2026. nad lokalnim izmenama, .NET SDK 10.0.301 i Node.js 24.14.1. To su lokalni rezultati, ne GitHub CI provera.

- `dotnet build backend/SecurityLab.slnx --configuration Release`: uspešno, bez upozorenja.
- `dotnet test backend/Laboratories.Tests/...`: 191 test prošao (112 postojećih + 79 novih za LAB-05), nema neuspešnih.
- `npm run build` (TypeScript + Vite) prošao; `npm test`: 69 testova prošlo (13 novih za LAB-05).
- Ručni HTTP smoke test na `http://127.0.0.1:5080` (Development, skladište u memoriji):

| Zahtev | X-Test-User | Dobijeno |
| --- | --- | --- |
| `GET /api/laboratories/search?q=rbac` | `marko:Teacher` | 200, samo Markova „RBAC i kontrola pristupa” |
| `GET /api/laboratories/search?q=rbac` | `jelena:Teacher` | 200, samo Jelenin „RBAC - Jelenin nacrt” |
| `GET /api/laboratories/search?q=rbac` | `ana:Student` | 200, `items: []` |
| `GET /api/laboratories/search?q=rbac` | nema | 401 |
| `GET /api/laboratories/search?q=` | `marko:Teacher` | 400 `INVALID_QUERY` |
| `GET /api/laboratories/search?q=a&q=b` | `marko:Teacher` | 400 `INVALID_QUERY` |
| `GET /api/laboratories/search?q=' OR '1'='1` | `marko:Teacher` | 200, `items: []` |
| `GET /api/laboratories/search?q=sifr` i `?q=KLJUC` | `marko:Teacher` | 200, „Šifrovanje i ključevi” |
| `GET /api/labs/search?q=analiza` | `marko:Teacher` | 200, „Analiza logova” (alias) |

- Frontend je proveren u browseru (`npm run dev`): nastavnik pronalazi svoju laboratoriju, a posle prelaska na studenta rezultati nestaju i pretraga vraća poruku da nema dostupnih laboratorija.

### Bezbedan pristup rezultatima pretrage

- **Fajlovi:** `backend/Platform.Api/Modules/Laboratories/Api/LaboratoryContracts.cs` (`LaboratorySearchItem` bez `createdBy`), `LaboratoriesEndpoints.cs` (`ToSearchItem`, `Cache-Control: no-store`).
- **Novi testovi (11):**
  - `LaboratorySearchServiceTests`: `Search_WhenAssistantOrAdministratorSearches_DoesNotSeeDrafts` (2), `Search_RechecksAccessOnEveryRequest`, `Search_HiddenLaboratoriesDoNotAffectLimitOrTruncation`, `Search_WhenAccessCheckFails_ReturnsNoPartialResults`
  - `LaboratorySearchHttpTests`: `Search_ResultsDoNotExposeAuthorId`, `Search_ResponseIsNeverCached` (2), `Search_WhenAssistantOrAdministratorSearches_DoesNotSeeDrafts` (2), `Search_WhenAccessCheckFails_Returns500WithoutResults`
- **Provera 6. novembra 2026** (grana spojena sa `main`, lokalno):
  - `Laboratories.Tests`: 210 od 211 prolazi; svih 90 LAB-05 testova prolazi. Jedini pad je `ReviewAuthorizationTests.Approve_WithoutRegisteredPolicy_DeniesBeforeDatabase` (REV-05), koji pada i na čistom `main`.
  - Frontend: `npm run build` i `npm test` 73/73.
- **Šta ova priča ne može dok ne postoji LAB-03:** prava po kursu i članstvu (npr. student vidi objavljene laboratorije svog kursa). LAB-01 laboratorija još nema kurs ni stanje „objavljena”; kad ih dobije, menja se samo `LaboratoryVisibility`, a pretraga i ovi testovi ostaju isti.

## Sledeći koraci

- Filtriranje po osobinama i dodatni filteri (naredne LAB-05 priče) mogu da prošire isti endpoint novim parametrima i isti servis, uz isto pravilo: prvo prava, pa poređenje.
- Proširenje `LaboratoryVisibility` kad LAB-03/ACC obezbede kurs, članstvo i objavljene revizije (student tada vidi objavljene laboratorije svog kursa).
- Dogovor sa timom za skladište oko pretrage u bazi i sa Identity timom oko rate limit politike za pretragu (SEC-04).

# LAB-02 — timski dijagrami (Tim 20)

Ovaj fajl prikazuje ceo tok LAB-02: model i validaciju metapodataka, perzistenciju, nastavničku formu, API metapodataka, katalog i njegov korisnički interfejs. Ne zamenjuje lični opis Osobe 6 u [LAB-02-frontend.md](LAB-02-frontend.md) i ne obuhvata LAB-06, koji ima zaseban dokument.

**Osnova za status:** lokalno pregledan `main` (`e744fab`). Puna linija označava kod prisutan u `main`; isprekidana linija označava rad na grani/PR-u ili integraciju koja još nije potvrđena u tom `main`.

## Dijagram arhitekture

```mermaid
flowchart LR
  subgraph CLIENT["React / TypeScript — nepouzdana zona"]
    TEACHER[Nastavnik]
    STUDENT[Student]
    FORM[TeacherMetadataForm<br/>Osoba 6: unos i izmena]
    CATALOG_UI[LabSearch / prikaz kataloga<br/>Osoba 7: kartice i filteri]
    FORM_CLIENT[api/labs.ts + api/labMetadata.ts]
    CATALOG_CLIENT[api/labs.ts: pretraga i filteri]
    TEACHER --> FORM --> FORM_CLIENT
    STUDENT --> CATALOG_UI --> CATALOG_CLIENT
  end

  subgraph API["Platform.Api — serverska zona"]
    ACCESS[Identitet, autorizacija<br/>i provera prava]
    LAB01[LAB-01 Laboratories API<br/>POST laboratorija<br/>PATCH naziv — main]
    META_API[LAB-02 Metadata API<br/>POST/PATCH metapodataka<br/>PR #130; nema GET nastavničkog nacrta]
    CATALOG_API[LAB-05 katalog / detalji<br/>PR #139; CatalogVisibilityPolicy]
    SEARCH_API[LAB-05 pretraga i filteri<br/>/api/laboratories/search<br/>/search/filters — grana]
    VISIBILITY[Provera vidljivosti svake laboratorije]
    LAB01_REPO[ILaboratoryRepository]
    INMEM[InMemoryLaboratoryRepository<br/>registrovan u main]
    PG_REPO[PostgresLaboratoryRepository<br/>adapter postoji; nije podrazumevani]
    ACCESS --> LAB01
    LAB01 --> LAB01_REPO --> INMEM
    LAB01_REPO -.-> PG_REPO
    ACCESS -.-> META_API
    ACCESS -.-> CATALOG_API
    ACCESS -.-> SEARCH_API
    CATALOG_API -.-> VISIBILITY
    SEARCH_API -.-> VISIBILITY
  end

  subgraph DOMAIN["LAB-02 domen i ugovori"]
    MODEL[LaboratoryMetadata<br/>ID reference na LAB-01, cilj,<br/>težina, tip, oznake — main]
    VALIDATOR[LaboratoryMetadataValidator<br/>serverska validacija — main]
    API_REPO[ILaboratoryMetadataRepository<br/>in-memory implementacija na PR #130]
    STORE_CONTRACT[ILaboratoryMetadataStore<br/>ugovor perzistencije — main]
    META_API -.-> VALIDATOR --> MODEL
    META_API -.-> API_REPO
    API_REPO -.->|potrebno usaglasiti ugovor| STORE_CONTRACT
  end

  subgraph PERSISTENCE["LAB-02 perzistencija"]
    LAB_STORE[ILaboratoryStore]
    PGSTORE[PostgresLaboratoryStore<br/>čita i upisuje metapodatke — main]
    DB[(authoring.laboratories<br/>title/status + cilj/težina/tip/oznake)]
    STORE_CONTRACT --> PGSTORE
    LAB_STORE --> PGSTORE
    PGSTORE --> DB
    PG_REPO --> LAB_STORE
  end

  FORM_CLIENT -->|POST kreiranje / PATCH naziv| ACCESS
  FORM_CLIENT -.->|POST/PATCH metapodataka| ACCESS
  FORM_CLIENT -.->|GET pri izmeni| ACCESS
  CATALOG_CLIENT -.->|pretraga i filteri| ACCESS
  CATALOG_CLIENT -.->|detalji laboratorije| ACCESS
  DB -.->|zapisi za proveru| VISIBILITY
  VISIBILITY -.->|samo dozvoljeni zapisi| CATALOG_CLIENT
  META_API -.->|greške polja / uspeh| FORM_CLIENT
  CATALOG_API -.->|metapodaci ili 404| FORM_CLIENT
  SEARCH_API -.->|kartice i opcije filtera| CATALOG_CLIENT
```

**Napomena o ugovorima:** `LaboratoryMetadata` ne poseduje naziv niti novi ID. Naziv i identitet laboratorije pripadaju LAB-01; LAB-02 metapodaci koriste postojeći `LaboratoryId`. LAB-01 API u `main` trenutno koristi `InMemoryLaboratoryRepository`; PostgreSQL adapter postoji, ali nije podrazumevano registrovan. PR #130 koristi `ILaboratoryMetadataRepository`, dok je u `main` perzistencioni ugovor `ILaboratoryMetadataStore`; adapter/usaglašavanje između njih je integraciona zavisnost.

## Dijagram toka podataka (DFD)

```mermaid
flowchart LR
  TEACHER[Nastavnik] -->|naziv, cilj, težina, tip, oznake| FORM
  STUDENT[Student] -->|tekst pretrage i izabrani filteri| CATALOG_UI

  subgraph BROWSER["Granica 1 — pregledač (nepouzdan unos)"]
    FORM[TeacherMetadataForm<br/>klijentska pomoćna provera]
    FORM_CLIENT[API klijent forme]
    CATALOG_UI[Katalog: pretraga, filteri, kartice]
    CATALOG_CLIENT[API klijent kataloga]
    FORM --> FORM_CLIENT
    CATALOG_UI --> CATALOG_CLIENT
    FORM_CLIENT --> FORM
    CATALOG_CLIENT --> CATALOG_UI
  end

  subgraph SERVER["Granica 2 — Platform.Api"]
    AUTH[Identitet i pravo pristupa]
    P1[LAB-01: kreiranje laboratorije<br/>ili promena naziva]
    P2[LAB-02: validacija i čuvanje metapodataka<br/>POST/PATCH — PR #130]
    P3[LAB-05: pretraga i kombinovanje filtera<br/>grana search-filter]
    P4[LAB-05: detalji kataloga<br/>CatalogVisibilityPolicy — PR #139]
    CHECK[Serverska provera vidljivosti<br/>pre vraćanja rezultata]
    VALIDATOR[LaboratoryMetadataValidator]
    REPO[ILaboratoryMetadataRepository<br/>in-memory na PR #130]
    AUTH --> P1
    AUTH -.-> P2
    AUTH -.-> P3
    AUTH -.-> P4
    P2 -.-> VALIDATOR --> REPO
    P3 -.-> CHECK
    P4 -.-> CHECK
  end

  subgraph DATA["Granica 3 — skladišta"]
    D0[(InMemoryLaboratoryRepository<br/>LAB-01: podrazumevano u main)]
    D1[(PostgreSQL authoring.laboratories<br/>LAB-02 store postoji u main)]
    D2[(InMemoryLaboratoryMetadataRepository<br/>PR #130)]
  end

  FORM_CLIENT -->|POST title / PATCH title| AUTH
  P1 -->|ID i naziv: main podrazumevano| D0
  P1 -.->|Postgres adapter postoji; nije podrazumevani| D1
  FORM_CLIENT -.->|POST/PATCH cilj, težina, tip, oznake| AUTH
  REPO -.->|upis/čitanje po LaboratoryId| D2
  D2 -.->|potreban adapter/usaglašavanje ugovora| D1
  VALIDATOR -.->|400 greške polja| FORM_CLIENT
  P2 -.->|uspeh ili 403| FORM_CLIENT

  CATALOG_CLIENT -.->|GET pretraga i filteri| AUTH
  P3 -.->|dostupni ID-jevi i kriterijumi| CHECK
  D1 -.->|title + metapodaci| CHECK
  CHECK -.->|samo laboratorije koje korisnik sme da vidi| CATALOG_CLIENT
  CATALOG_CLIENT -.->|GET detalji pri izmeni| AUTH
  P4 -.->|vidljivi metapodaci ili 404| CATALOG_CLIENT
  CATALOG_CLIENT --> STUDENT
  FORM_CLIENT --> TEACHER
```

Puna linija prikazuje tok koji postoji u `main`; isprekidani tokovi pripadaju granama/PR-ovima ili integraciji koja još nije potvrđena. LAB-01 trenutno čuva kroz memorijski repozitorijum; PostgreSQL store i adapter postoje, ali nisu podrazumevani u API-ju. Server odlučuje o pravima, validaciji i vidljivosti; pregledač samo šalje unos i prikazuje odgovor. Katalog ne treba koristiti kao potvrđen način za učitavanje nastavnikovog skrivenog nacrta: `/api/catalog/{id}` može vratiti 404, a PR #130 nema GET za nastavnički nacrt.

## Stanje i preostalo usaglašavanje

- **U `main`:** LAB-02 model, validator, `ILaboratoryMetadataStore`, PostgreSQL store i LAB-01 identitet/naziv laboratorije. LAB-01 modul podrazumevano registruje memorijski repozitorijum.
- **Na granama/PR-ovima:** nastavnički metadata API (PR #130), forma Osobe 6 (PR #145), katalog API (PR #139) i LAB-05 pretraga/filteri i interfejs (grana `feature/search-filter`).
- **Pre integracionog prikaza:** usaglasiti API metadata repozitorijum sa PostgreSQL store ugovorom; potvrditi endpoint za učitavanje sopstvenog nacrta; uskladiti enum vrednosti API-ja sa formom; potvrditi da pretraga i katalog koriste isti serverski odgovor o vidljivosti.
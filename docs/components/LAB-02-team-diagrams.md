# LAB-02 — timski dijagrami (Tim 20)

Ovaj fajl prikazuje ceo tok LAB-02: model i validaciju metapodataka, perzistenciju, nastavničku formu, API metapodataka, katalog i njegov korisnički interfejs. Ne zamenjuje lični opis Osobe 6 u [LAB-02-frontend.md](LAB-02-frontend.md) i ne obuhvata LAB-06, koji ima zaseban dokument.

**Osnova za status:** `main` u trenutku pregleda (`13dae720`). Puna linija označava kod prisutan u `main`; isprekidana linija označava rad na grani/PR-u ili integraciju koja još nije potvrđena u tom `main`.

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
    LAB01[LAB-01 Laboratories API<br/>POST title + courseId<br/>PATCH naziv]
    META_API[LAB-02 Metadata API<br/>POST/GET/PATCH metapodataka<br/>GET je zaštićen nacrt]
    CATALOG_API[LAB-05 katalog / detalji<br/>CatalogVisibilityPolicy]
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
    API_REPO[ILaboratoryMetadataRepository<br/>InMemory u Development; persistent adapter kad je konfigurisan]
    STORE_CONTRACT[ILaboratoryMetadataStore<br/>ugovor perzistencije — main]
    META_API -.-> VALIDATOR --> MODEL
    META_API -.-> API_REPO
    API_REPO -->|PersistentMetadataRepository adapter| STORE_CONTRACT
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

  FORM_CLIENT -->|GET kursevi; POST title + courseId; PATCH naziv| ACCESS
  FORM_CLIENT -->|POST/GET/PATCH metapodataka nacrta| ACCESS
  CATALOG_CLIENT -.->|pretraga i filteri| ACCESS
  CATALOG_CLIENT -.->|detalji laboratorije| ACCESS
  DB -.->|zapisi za proveru| VISIBILITY
  VISIBILITY -.->|samo dozvoljeni zapisi| CATALOG_CLIENT
  META_API -.->|greške polja / uspeh| FORM_CLIENT
  CATALOG_API -.->|samo vidljivi detalji| CATALOG_CLIENT
  META_API -.->|nacrt za nastavnika ili 404| FORM_CLIENT
  SEARCH_API -.->|kartice i opcije filtera| CATALOG_CLIENT
```

**Napomena o ugovorima:** `LaboratoryMetadata` ne poseduje naziv niti novi ID. Naziv i identitet laboratorije pripadaju LAB-01; LAB-02 metapodaci koriste postojeći `LaboratoryId`. `PersistentMetadataRepository` povezuje API ugovor `ILaboratoryMetadataRepository` sa perzistencionim ugovorom `ILaboratoryMetadataStore`; `MetadataCatalogStore` izlaže metapodatke katalogu. U lokalnom Development režimu koristi se memorijsko skladište, a PostgreSQL putanja zavisi od konfiguracije konekcije.

## Dijagram toka podataka (DFD)

```mermaid
flowchart LR
  TEACHER[Nastavnik] -->|kurs, naziv, cilj, težina, tip, oznake| FORM
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
    P1[LAB-01: kreiranje laboratorije uz courseId<br/>ili promena naziva]
    P2[LAB-02: validacija, čuvanje i učitavanje nacrta<br/>POST/GET/PATCH]
    P3[LAB-05: pretraga i kombinovanje filtera<br/>grana search-filter]
    P4[LAB-05: detalji kataloga<br/>CatalogVisibilityPolicy — PR #139]
    CHECK[Serverska provera vidljivosti<br/>pre vraćanja rezultata]
    VALIDATOR[LaboratoryMetadataValidator]
    REPO[ILaboratoryMetadataRepository<br/>API skladišni ugovor]
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
    D2[(InMemoryLaboratoryMetadataRepository<br/>Development)]
  end

  FORM_CLIENT -->|GET kursevi / POST title + courseId / PATCH title| AUTH
  P1 -->|ID i naziv: main podrazumevano| D0
  P1 -.->|Postgres adapter postoji; nije podrazumevani| D1
  FORM_CLIENT -->|POST/GET/PATCH cilj, težina, tip, oznake| AUTH
  REPO -.->|upis/čitanje po LaboratoryId u Development| D2
  REPO -.->|PersistentMetadataRepository| D1
  VALIDATOR -.->|400 greške polja| FORM_CLIENT
  P2 -.->|uspeh ili 403| FORM_CLIENT

  CATALOG_CLIENT -.->|GET pretraga i filteri| AUTH
  P3 -.->|dostupni ID-jevi i kriterijumi| CHECK
  D1 -.->|title + metapodaci| CHECK
  CHECK -.->|samo laboratorije koje korisnik sme da vidi| CATALOG_CLIENT
  CATALOG_CLIENT -.->|GET javni detalji| AUTH
  P4 -.->|vidljivi metapodaci ili 404| CATALOG_CLIENT
  CATALOG_CLIENT --> STUDENT
  FORM_CLIENT --> TEACHER
```

Puna linija prikazuje tokove potvrđene u implementaciji. LAB-01 čuva laboratoriju, a LAB-02 zapisuje metapodatke. Nastavnički edit čita nacrt kroz zaštićeni `GET /api/laboratories/{id}/metadata`, jer javni katalog može vratiti 404 za nacrt bez javnog sadržaja. Server proverava prava i validaciju; pregledač šalje `courseId` i metapodatke i prikazuje odgovor.

## Stanje i preostalo usaglašavanje

- **U `main`:** LAB-02 model, validator, metadata API, `ILaboratoryMetadataStore`, PostgreSQL store i LAB-01 identitet/naziv laboratorije. API sloj koristi adapter `PersistentMetadataRepository` kada je PostgreSQL konfigurisan, a razvojno memorijsko skladište lokalno.
- **LAB-02 tok u ovoj grani:** nastavnik bira aktivni kurs; server proverava članstvo; forma kreira laboratoriju, čuva metapodatke i koristi zaštićeni metadata GET za učitavanje nacrta.
- **Tok kataloga:** student koristi zasebni javni katalog i politiku vidljivosti; katalog nije izvor podataka za nastavničku izmenu.
- **Perzistencija:** lokalni razvoj koristi memorijska skladišta; trajno čuvanje koristi adaptere ka PostgreSQL skladištu kada su konekcije konfigurisane.

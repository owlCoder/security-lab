
# ACC-01 | Component Architecture Diagram

Dijagram prikazuje implementiranu arhitekturu React/TypeScript
frontenda za upravljanje kursevima i njegovu komunikaciju
sa Team21 ACC-01 backendom.

```mermaid
flowchart LR
    USER([Korisnik])

    subgraph FRONTEND["React / TypeScript frontend"]
        direction TB

        APP["App.tsx<br/>Zajednicka integraciona tacka"]
        PAGE["CoursesPage.tsx<br/>UI stanje i koordinacija"]
        LIST["CourseList.tsx"]
        CARD["CourseCard.tsx"]
        FORM["CourseForm.tsx"]
        TYPES["types/course.ts"]
        RULES["courseRules.ts<br/>Normalizacija i validacija"]
        SERVICE["courseService.ts<br/>HTTP komunikacija"]
        CSS["CoursesPage.css"]
        STATE[("React state<br/>Podaci dobijeni sa servera")]

        APP --> PAGE
        PAGE --> LIST
        LIST --> CARD
        PAGE --> FORM
        FORM --> RULES
        PAGE --> SERVICE
        PAGE --> STATE
        STATE --> PAGE

        TYPES -.-> PAGE
        TYPES -.-> SERVICE
        TYPES -.-> FORM
        TYPES -.-> CARD
        CSS -.-> PAGE
    end

    subgraph BACKEND["ASP.NET Core backend"]
        direction TB

        ACCESS["ACC-05<br/>Kontrola pristupa"]
        API["CourseEndpoints"]
        ACTOR["ICurrentActor"]
        AUTH["ICourseAuthorization"]
        CS["CourseService"]
        EF["CoursesDbContext"]

        ACCESS --> API
        API --> CS
        ACTOR --> CS
        CS --> AUTH
        CS --> EF
    end

    DB[("PostgreSQL<br/>courses.Courses")]

    USER --> APP

    SERVICE -->|"GET list / detail<br/>POST create / archive"| ACCESS
    API -->|"HTTP response / ProblemDetails"| SERVICE
    ACCESS -->|"Denied response"| SERVICE

    EF -->|"SELECT / INSERT / UPDATE"| DB

    style FRONTEND fill:#edf5ff,stroke:#9ebde9,color:#24476a
    style BACKEND fill:#effaf3,stroke:#a4d3b7,color:#286b4b
    style STATE fill:#f3e9ff,stroke:#c9a9ed,color:#4c3575
    style DB fill:#fff3de,stroke:#e4b357,color:#775117
    style PAGE fill:#d6e8ff,stroke:#84b4f3,color:#24476a
    style SERVICE fill:#d6e8ff,stroke:#84b4f3,color:#24476a
```

## Napomene

- App.tsx je zajednicka integraciona tacka platforme.
- CoursesPage koordinira ucitavanje, kreiranje, pretragu,
  filtriranje i arhiviranje kurseva.
- CourseForm koristi courseRules za klijentsku validaciju.
- courseService komunicira sa backendom preko HTTP zahteva.
- Servis implementira GET pojedinacnog kursa, ali ga
  trenutna stranica ne poziva direktno.
- React state nije trajno skladiste niti izvor autorizacije.
- Backend je odgovoran za identitet, dozvole, validaciju
  i trajno cuvanje podataka.
- Integracija stvarne autentikacije i autorizacije
  ostaje otvorena zavisnost backend komponenti.

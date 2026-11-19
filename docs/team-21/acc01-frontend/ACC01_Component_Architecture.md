# ACC-01 | Component Architecture Diagram

Dijagram prikazuje glavne React/TypeScript komponente, servis za HTTP pozive, ASP.NET Core slojeve i PostgreSQL bazu.

```mermaid
flowchart LR
    USER([Korisnik])

    subgraph FRONTEND["React / TypeScript frontend"]
        direction LR
        APP["App.tsx<br/>(zajednička integracija)"]
        PAGE["CoursesPage.tsx<br/>(UI stanje i upravljanje)"]
        LIST["CourseList.tsx"]
        CARD["CourseCard.tsx"]
        FORM["CourseForm.tsx"]
        TYPES["types/course.ts<br/>(tipovi)"]
        RULES["courseRules.ts<br/>(validacija)"]
        SERVICE["courseService.ts<br/>(GET / POST pozivi)"]
        STATE[("React useState<br/>(kursevi u sesiji)")]

        APP -->|prikaz stranice| PAGE
        PAGE -->|prikaz| LIST
        LIST -->|kurs| CARD
        PAGE -->|unos| FORM
        PAGE -->|tipovi| TYPES
        PAGE -->|validacija| RULES
        PAGE -->|API pozivi| SERVICE
        PAGE -->|lokalno stanje| STATE
    end

    subgraph BACKEND["ASP.NET Core / Team21 ACC-01"]
        direction LR
        API["Courses API<br/>(autorizacija i validacija)"]
        APP_SERVICE["Course application<br/>service"]
        CONTEXT["CoursesDbContext"]
        API -->|obrada| APP_SERVICE
        APP_SERVICE -->|pristup podacima| CONTEXT
    end

    DB[("PostgreSQL<br/>courses.Courses")]

    USER -->|navigacija| APP
    SERVICE -->|HTTP / JSON| API
    CONTEXT -->|SQL| DB

    style FRONTEND fill:#edf5ff,stroke:#9ebde9,color:#24476a
    style BACKEND fill:#effaf3,stroke:#a4d3b7,color:#286b4b
    style STATE fill:#f3e9ff,stroke:#c9a9ed,color:#4c3575
    style DB fill:#fff3de,stroke:#e4b357,color:#775117
    style PAGE fill:#d6e8ff,stroke:#84b4f3,color:#24476a
    style SERVICE fill:#d6e8ff,stroke:#84b4f3,color:#24476a
```

**Napomena:** `App.tsx` je zajednička integraciona tačka tima. `courseService.ts` sadrži metode za GET i POST zahteve; dostupnost GET endpointa zavisi od implementacije na backendu.

# ACC-01 | Data Flow Diagram (DFD)

Dijagram prikazuje tok podataka za kreiranje i arhiviranje kurseva, uz granicu poverenja između klijenta i servera.

```mermaid
flowchart LR
    U([Korisnik])

    subgraph CLIENT["Granica poverenja: browser (nepouzdan klijent)"]
        direction LR
        P1["1. React forma / akcija<br/>(create ili archive)"]
        P2["2. Klijentska validacija<br/>(samo UX)"]
        P3["3. courseService.ts<br/>(HTTP zahtev)"]
        D1[("D1: React state<br/>(prikaz tokom sesije)")]
        P4["4. Prikaz rezultata<br/>ili greške"]
        P1 -->|unos| P2
        P2 -->|validan zahtev| P3
        D1 -->|trenutni podaci| P4
    end

    subgraph SERVER["Granica poverenja: server (ASP.NET Core)"]
        direction LR
        P5["5. Identitet / dozvole<br/>+ serverska validacija"]
        P6["6. Kreiranje / arhiviranje<br/>Course service"]
        P5 -->|odobren zahtev| P6
    end

    D2[("D2: PostgreSQL<br/>courses.Courses")]

    U -->|podaci / arhiviranje| P1
    P3 -->|"POST /api/courses<br/>POST /api/courses/{id}/archive"| P5
    P5 -->|4xx greška| P3
    P6 -->|INSERT / UPDATE| D2
    D2 -->|rezultat| P6
    P6 -->|"201 + CourseDto / 204"| P3
    P3 -->|uspešan odgovor| D1
    P4 -->|status / greška| U

    style CLIENT fill:#eef5ff,stroke:#8eb8ee,color:#24476a
    style SERVER fill:#edf9f1,stroke:#9ed3b0,color:#286b4b
    style D1 fill:#f3e9ff,stroke:#c9a9ed,color:#4c3575
    style D2 fill:#fff3de,stroke:#e4b357,color:#775117
```

**Napomena:** Validacija u React-u poboljšava korisničko iskustvo, ali serverska validacija i provera ovlašćenja ostaju obavezne. React stanje predstavlja samo kurseve dostupne tokom tekuće sesije; učitavanje pune liste zahteva odgovarajući GET API.

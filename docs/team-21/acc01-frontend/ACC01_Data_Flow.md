
# ACC-01 | Data Flow Diagram (DFD)

Dijagram prikazuje ucitavanje, kreiranje, pretragu,
filtriranje i arhiviranje kurseva, kao i obradu
uspesnih i neuspesnih HTTP odgovora.

```mermaid
flowchart LR
    U([Korisnik])

    subgraph CLIENT["Granica poverenja A - Browser"]
        direction TB

        P1["1. CoursesPage<br/>Korisnicke akcije"]
        P2["2. CourseForm<br/>Normalizacija i validacija"]
        P3["3. courseService<br/>HTTP zahtevi"]
        P4["4. Obrada HTTP odgovora"]
        D1[("D1: React state")]
        P5["5. Pretraga i filtriranje"]
        P6["6. Prikaz kurseva"]
        P7["7. Prikaz greske"]
        P8["8. Potvrda arhiviranja"]

        P1 -->|Ucitavanje ili osvezavanje| P3
        P1 -->|Novi kurs| P2
        P2 -->|Validni podaci| P3
        P2 -->|Neispravan unos| P7

        P1 -->|Arhiviranje| P8
        P8 -->|Potvrda| P3
        P8 -->|Otkazivanje| P6

        P4 -->|GET 200| D1
        P4 -->|POST 201 ili 204 - ponovni GET| P3
        P4 -->|4xx ili 5xx| P7
        P4 -->|GET nije uspeo - praznjenje liste| D1

        D1 --> P5
        P1 -->|Pretraga ili filter| P5
        P5 --> P6
    end

    subgraph SERVER["Granica poverenja B - Backend"]
        direction TB

        P9["9. ACC-05<br/>Provera pristupa"]
        P10["10. CourseEndpoints"]
        P11["11. CourseService<br/>Identitet, dozvole i validacija"]
        P12["12. CoursesDbContext"]

        P9 -->|Dozvoljen zahtev| P10
        P10 --> P11
        P11 -->|Dozvoljena operacija| P12
    end

    D2[("D2: PostgreSQL<br/>courses.Courses")]

    U -->|Korisnicka akcija| P1

    P3 -->|"GET /api/courses<br/>POST /api/courses<br/>POST /api/courses/{id}/archive"| P9

    P9 -->|Odbijen pristup| P4
    P11 -->|Rezultat ili greska| P10
    P10 -->|HTTP odgovor| P4

    P12 -->|"SELECT / INSERT / UPDATE"| D2
    D2 -->|Rezultat| P12
    P12 --> P11

    P6 -->|Lista i statusi| U
    P7 -->|Poruka o gresci| U

    style CLIENT fill:#eef5ff,stroke:#8eb8ee,color:#24476a
    style SERVER fill:#edf9f1,stroke:#9ed3b0,color:#286b4b
    style D1 fill:#f3e9ff,stroke:#c9a9ed,color:#4c3575
    style D2 fill:#fff3de,stroke:#e4b357,color:#775117
    style P7 fill:#ffebeb,stroke:#d47878,color:#822a2a
```

## Napomene

- Lista kurseva ucitava se sa servera pri otvaranju stranice.
- Pretraga i filtriranje izvrsavaju se nad ucitanim
  kursevima u React stanju.
- Kreiranje kursa zahteva klijentsku i serversku validaciju.
- Arhiviranje zahteva eksplicitnu potvrdu korisnika.
- Nakon uspesnog kreiranja ili arhiviranja frontend
  ponovo ucitava listu sa servera.
- Arhiviranje predstavlja promenu statusa, a ne brisanje.
- Ako ucitavanje ne uspe, lokalna lista se prazni.
- Klijentska validacija ne zamenjuje serversku autorizaciju.
- Razvojni identitet nije produkcioni mehanizam autentikacije.

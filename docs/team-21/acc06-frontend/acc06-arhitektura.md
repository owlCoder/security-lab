# ACC-06 — Dijagram arhitekture

Dijagram prikazuje komponente ACC-06 frontenda, njihove zavisnosti i vezu sa postojećim backendom, nakon uklanjanja dodele materijala grupi.

```mermaid
flowchart TD
    subgraph Browser["Pregledač — React frontend"]
        App["App.tsx"]
        Acc06["Acc06.tsx"]

        subgraph Views["Komponente prikaza"]
            Groups["GroupManagement.tsx"]
            Materials["StudentMaterials.tsx"]
        end

        Api["acc06.ts"]
    end

    subgraph Server["Postojeći backend"]
        GroupsApi["API za grupe"]
        MembersApi["API za članove"]
        MaterialsApi["API za materijale"]
    end

    App -->|"Uključuje ACC-06 prikaz"| Acc06

    Acc06 -->|"Nastavnik: korisnik, kurs, status kursa i demo studenti"| Groups
    Acc06 -->|"Student: korisnik i kurs"| Materials

    Groups -->|"Grupe i članstvo"| Api
    Materials -->|"Lista i čitanje materijala"| Api

    Api -->|"HTTP GET i POST"| GroupsApi
    Api -->|"HTTP GET, POST i DELETE"| MembersApi
    Api -->|"HTTP GET"| MaterialsApi
```

`Acc06.tsx` čuva izbor demo korisnika i kursa, a prikazuje se samo u razvojnom režimu. `acc06.ts` šalje zahteve sa zaglavljem `X-Test-User` i obrađuje API greške.


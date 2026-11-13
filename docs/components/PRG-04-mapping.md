# PRG-04: Predloženi API za mapiranje kompetencija

**Status: Proposed API — not implemented.**

**Zahtev:** Jedna laboratorija može da proverava više kompetencija, a ista kompetencija može biti povezana sa više laboratorija.

Arhitektonski predlog i granice odgovornosti nalaze se u [PRG-04-competencies.md](PRG-04-competencies.md).

## Predloženi HTTP ugovor

| Metod i putanja | Očekivani odgovor | Značenje |
| --- | --- | --- |
| `GET /api/competencies` | `200 { competencies: [...] }` | Čitanje kataloga |
| `GET /api/laboratories/{laboratoryId}/competencies` | `200 { laboratoryId, competencies: [...] }` | Čitanje veza |
| `PUT /api/laboratories/{laboratoryId}/competencies/{competencyId}` | `201` ili `204` | Dodavanje veze |
| `DELETE /api/laboratories/{laboratoryId}/competencies/{competencyId}` | `204` | Uklanjanje veze |

Svaka kompetencija treba da sadrži `id`, `name` i opcioni `description`.

Predloženo ponašanje:

- `201 Created` za novu vezu.
- `204 No Content` ako veza već postoji.
- `204 No Content` prilikom uklanjanja, čak i kada veza ne postoji.
- `400 Bad Request` za nevažeće identifikatore.
- `401 Unauthorized` za neautentifikovane zahteve.
- `403 Forbidden` za nedostatak prava pristupa laboratoriji.
- `404 Not Found` za nepostojeću laboratoriju ili kompetenciju.
- `503 Service Unavailable` ako skladište nije dostupno.

Ovo su predloženi odgovori. Njihovo ponašanje treba potvrditi i implementirati.

## Predloženi tok dodavanja veze

```mermaid
flowchart TB
  START["PUT: LaboratoryId i CompetencyId"]
  AUTH{"Identitet je autentifikovan?"}
  ACCESS{"Laboratorija postoji<br/>i pristup je dozvoljen?"}
  VALID{"Identifikatori su validni?"}
  STORE{"Skladište je dostupno?"}
  EXISTS{"Kompetencija postoji?"}
  INSERT["Dodavanje veze<br/>ON CONFLICT DO NOTHING"]
  CREATED{"Nova veza?"}

  START --> AUTH
  AUTH -->|"ne"| E401["401 Unauthorized"]
  AUTH -->|"da"| VALID
  VALID -->|"ne"| E400["400 Bad Request"]
  VALID -->|"da"| ACCESS
  ACCESS -->|"ne"| E403["403 Forbidden"]
  ACCESS -->|"da"| STORE
  STORE -->|"ne"| E503["503 Service Unavailable"]
  STORE -->|"da"| EXISTS
  EXISTS -->|"ne"| E404["404 Not Found"]
  EXISTS -->|"da"| INSERT
  INSERT --> CREATED
  CREATED -->|"da"| R201["201 Created"]
  CREATED -->|"ne"| R204["204 No Content"]
```

Provera prava pristupa mora da se izvrši pre pristupa skladištu.

## Predložena sekvenca: dve kompetencije na jednoj laboratoriji

Preduslov: laboratorija i kompetencije postoje, a pozivalac ima odgovarajuća prava.

```mermaid
sequenceDiagram
  actor C as HTTP klijent
  participant E as CompetencyEndpoints
  participant I as IdentityContext
  participant A as LAB / Access
  participant S as CompetencyStore
  participant DB as PostgreSQL

  C->>E: GET /api/competencies
  E->>I: Provera identiteta
  I-->>E: Identitet
  E->>S: ListAsync()
  S->>DB: SELECT katalog
  DB-->>S: Kompetencije
  S-->>E: Katalog
  E-->>C: 200 competencies

  loop Za K1 i K2
    C->>E: PUT /api/laboratories/L/competencies/K
    E->>I: Provera identiteta
    I-->>E: Identitet
    E->>A: Provera laboratorije i prava
    A-->>E: Pristup dozvoljen
    E->>S: AddAsync(L, K)
    S->>DB: Provera kompetencije i INSERT
    DB-->>S: Rezultat
    S-->>E: Rezultat dodavanja
    E-->>C: 201 ili 204
  end

  C->>E: GET /api/laboratories/L/competencies
  E->>I: Provera identiteta
  E->>A: Provera prava
  E->>S: GetForLaboratoryAsync(L)
  S->>DB: SELECT povezanih kompetencija
  DB-->>S: K1 i K2
  S-->>E: Lista
  E-->>C: 200 laboratoryId i competencies

  C->>E: DELETE /api/laboratories/L/competencies/K1
  E->>I: Provera identiteta
  E->>A: Provera prava
  E->>S: RemoveAsync(L, K1)
  S->>DB: DELETE veze L-K1
  DB-->>S: Rezultat
  E-->>C: 204 No Content
```

U ovom scenariju uklanjanje veze L-K1 ne treba da utiče na vezu L-K2 ili na katalog kompetencija.

## Planirana autorizacija i korisnički interfejs

1. Dogovoriti LAB ugovor za proveru postojanja laboratorije.
2. Dogovoriti Access ugovor za proveru prava pozivaoca nad laboratorijom.
3. Obezbediti da odbijeni zahtevi ne pristupaju skladištu.
4. Implementirati React ekran za izbor postojećih kompetencija i prikaz veza.
5. Omogućiti dodavanje i uklanjanje pojedinačnih veza.
6. Sprečiti prikaz zastarelih podataka nakon promene korisnika ili laboratorije.

## Planirani testovi

| Scenario | Očekivani rezultat |
| --- | --- |
| Neautentifikovan zahtev | `401` |
| Pozivalac bez prava pristupa | `403` |
| Nevažeći identifikatori | `400` |
| Nepostojeća kompetencija | `404` |
| Dodavanje dve kompetencije istoj laboratoriji | Obe veze postoje |
| Ista kompetencija u više laboratorija | Obe veze postoje |
| Ponovljeno dodavanje iste veze | Nema duplikata |
| Uklanjanje jedne veze | Ostale veze ostaju |
| Ponovno pokretanje aplikacije | Podaci ostaju sačuvani |

Testove treba napisati i izvršiti nakon implementacije.

**Napomena:** Ovaj dokument opisuje predloženi HTTP ugovor, tokove i kriterijume testiranja. Ne tvrdi da navedeni endpoint-i, skladište ili testovi trenutno postoje.
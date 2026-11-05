# Frontend

- `npm run dev` — pravi backend (`http://localhost:5080` preko Vite proxy-ja)
- `npm run dev:mock` — mock API dok IDN-05 / REV-05 backend nije gotov (samo dev, ne ulazi u build)
- `npm test` — unit testovi (vitest)

## Mock nalozi (lozinka `demo-lozinka`)

| Nalog | Uloga |
|-|-|
| `student@example.com` | student, nema pristup revizijama (404) |
| `nastavnik@example.com` | Petar, autor `rev-xss`, `rev-pwd` (nacrt), `rev-csrf` |
| `nastavnik2@example.com` | Ana, autor `rev-sql`, odobrava Petrove revizije |

- 5 pogrešnih lozinki zaredom: `429` + `Retry-After: 30` (IDN-05)
- `rev-csrf` pri odobrenju vraća `422` (REV-02, neispravan evaluator)
- `409 revision_changed`: u konzoli browsera `__mock.editRevision("rev-xss")` dok je revizija otvorena

Mock se uključuje samo sa `npm run dev:mock` (`vite --mode mock`) i ne ulazi u produkcioni build.

API ugovor za revizije je predlog (v0.2) poslat Marku. Kad ga potvrdi, biće u `docs/contracts/REV-05-API.md`.
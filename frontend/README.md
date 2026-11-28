# Frontend

- `npm run dev` — frontend uz pravi backend (`http://localhost:5080` preko Vite proxy-ja); pokretanje backenda i baze je u `docs/DEVELOPMENT.md`
- `npm run dev:mock` — mock API u browseru (samo dev, ne ulazi u build); koristi ga i LAB demo
- `npm test` — unit testovi (vitest)
- `npm run build` — provera tipova i produkcioni build

## IDN-05 / REV-05 UI

- Forma za prijavu (`src/components/LoginForm.tsx`): na `429` prikazuje „try again later" sa odbrojavanjem po `Retry-After` i blokira slanje do isteka. Sesija je HttpOnly kolačić; UI ne čuva token.
- Revizije (posle prijave): lista na čekanju, moje revizije, detalj sa radnjama Predaj / Odobri / Vrati sa komentarom i istorija odluka.
- API: `docs/components/REV-05-use-cases-api.md`. Opis komponente: `docs/components/IDN-05-REV-05-UI.md`.

## Mock za revizije (`src/api/mock.ts`)

Prijava u aplikaciji uvek ide na pravi backend (`/api/auth/me`, kolačić), pa se ekrani za revizije ručno proveravaju uz `npm run dev`. Mock pravila za revizije prate pravi API i proveravaju se u `src/__tests__/mock.test.ts`:

- `rev-csrf` pri odobrenju vraća `422` (REV-02, neispravan evaluator)
- autor ne može da odobri sopstvenu reviziju (`403 self_approval_forbidden`)
- zastarela verzija daje `409 revision_changed`; komentar uz odobrenje daje `400 invalid_input`

## AUT-01 nacrti zadataka

Stranica `/drafts` (lista nacrta i forma za kreiranje) ima dva režima. Izbor je jedan build-time prekidač, a kod stranice se ne menja.

- **Demo (podrazumevano):** `npm run dev`. Nacrti žive samo u memoriji otvorene stranice, ništa se ne šalje serveru, a stranica to stalno prikazuje. Ruta postoji samo u razvojnom režimu, ne i u produkcionom buildu.
- **Server:** `VITE_DRAFTS_API=true npm run dev` (PowerShell: `$env:VITE_DRAFTS_API = "true"; npm run dev`). Stranica koristi `GET` i `POST` na `/api/authoring/drafts` preko zajedničkog zahteva sesije (kolačić; `401` odjavljuje korisnika), a posle kreiranja ponovo učitava listu. Ovaj režim zahteva da backend izlaže te rute (ugovor je opisan u `src/api/drafts.ts`).

Pozivi ka serveru su samo u `src/api/drafts.ts`, a izbor izvora u `src/pages/DraftsRoute.tsx`. Opis komponente, arhitektura i tok podataka: `docs/components/AUT-01 AUT-06/AUT-01-frontend.md`.

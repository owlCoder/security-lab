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
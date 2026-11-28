# LAB-02 — nastavnički ekran za metapodatke

**Student:** Teodora Vasiljević  
**Tim:** 20  
**Zaduženje:** nastavnički ekran za unos i izmenu metapodataka laboratorije  
**Grana:** `feature/LAB-02-teacher-form`  
**Zahtev:** LAB-02 — naziv, cilj, težina, tip i oznake; metapodaci moraju biti validirani i dostupni u katalogu.

## Opseg

Teodora Vasiljević izrađuje nastavničku React formu za unos i izmenu metapodataka laboratorije. Forma šalje podatke API-ju i prikazuje greške uz odgovarajuća polja. Klijentska provera pomaže pri unosu, ali API ostaje merodavan za autorizaciju i validaciju.

## Doprinos zajedničkim dijagramima

Ovaj opis daje doprinos Osobe 6 zajedničkim dijagramima tima.

### Deo za dijagram arhitekture

- **React aplikacija:** `TeacherMetadataPage` otvara `TeacherMetadataForm`.
- **Forma:** pri kreiranju učitava aktivne kurseve i prikuplja kurs, naziv, cilj, težinu, tip i oznake; pri izmeni učitava sačuvane vrednosti nacrta.
- **API klijenti:** `api/labs.ts` kreira laboratoriju uz obavezne `title` i `courseId`; `api/labMetadata.ts` čuva, učitava i menja metapodatke i naziv.
- **Zavisnosti:** `/api/courses`, `/api/laboratories` i zaštićeni API metapodataka. Server proverava članstvo nastavnika na izabranom kursu, prava pristupa i validnost metapodataka.
- **Granica odgovornosti:** frontend ne čuva podatke i ne odlučuje o pravima pristupa; autorizaciju i konačnu validaciju sprovodi server.

### Deo za DFD

- **Akter:** nastavnik.
- **Ulaz:** naziv, cilj, težina, tip i oznake laboratorije.
- **Proces na klijentu:** forma proverava obavezna polja i ograničenja dužine, pa formira API zahtev.
- **Granica poverenja:** između pregledača i API-ja. Sve vrednosti i identitet koji dolaze iz pregledača smatraju se nepouzdanim.
- **Procesi na serveru:** provera identiteta i prava, validacija metapodataka i čuvanje podataka.
- **Skladište:** laboratorije i oznake; server ih čita i upisuje.
- **Izlaz:** odgovor o uspehu ili greške validacije koje forma prikazuje uz polja.
- **Bezbednosno pravilo:** prolazak klijentske provere ne dokazuje da su podaci validni niti da je korisniku dozvoljen upis.

## Rute i API pozivi

Stranica je dostupna na rutama:

- `teacher/labs/new` — unos nove laboratorije.
- `teacher/labs/:labId/edit` — izmena laboratorije sa datim ID-jem.

Frontend trenutno koristi sledeće pozive:

| Radnja | Trenutni poziv |
| --- | --- |
| Učitavanje kurseva | `GET /api/courses`; forma prikazuje aktivne kurseve |
| Kreiranje laboratorije | `POST /api/laboratories/` sa `{ title, courseId }` |
| Kreiranje metapodataka | `POST /api/laboratories/{id}/metadata` sa ciljem, težinom, tipom i oznakama |
| Izmena naziva | `PATCH /api/laboratories/{id}/title` sa `{ title }` |
| Učitavanje podataka za izmenu | Zaštićeni `GET /api/laboratories/{id}/metadata`; vraća naziv i metapodatke nacrta bez zahteva za javni sadržaj |
| Izmena metapodataka | `PATCH /api/laboratories/{id}/metadata` sa ciljem, težinom, tipom i oznakama |

### Status integracije

- Frontend testovi pokrivaju izbor kursa, slanje `courseId`, serverske greške, učitavanje nacrta i izmenu naziva i metapodataka.
- Integracioni test laboratorijskog API-ja koristi stvarnu registraciju ruta i prolazi tok create → save → load → edit → reload. Ne ubacuje javni sadržaj; testni adapter izoluje proveru pristupa u ovom toku, dok posebni HTTP testovi potvrđuju da student i nastavnik sa drugog kursa ne mogu da čitaju nacrt.
- Pri izmeni, forma koristi zaštićeni metadata endpoint, zato nacrt može da se učita i kada u katalogu nema javnog sadržaja. Enum vrednosti prihvataju numerički oblik koji vraća backend.
- Lokalna provera: 27 frontend test fajlova, 253 testa i produkcioni build su prošli. Backend tok integracije je prošao ciljano.

## Scenario i redosled demonstracije

### Kada serverski API-ji budu dostupni

1. Pokrenuti API i frontend prema README-u.
2. Otvoriti `/teacher/labs/new`; u razvojnom režimu ekran koristi demo nastavnika `marko` sa serverskim test ID-jem `11111111-1111-1111-1111-111111111111` i demo kurs `OIB-DEMO` (`aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa`), koji odgovaraju backend seed članstvu.
3. Izabrati aktivni kurs, pa uneti naziv, cilj, težinu, tip i oznake.
4. Izabrati **Sačuvaj laboratoriju** i proveriti da zahtev za kreiranje uključuje `courseId`, a zatim da se čuvaju metapodaci.
5. Otvoriti `/teacher/labs/{labId}/edit`, proveriti učitane vrednosti, promeniti naziv ili metapodatke i sačuvati ih.
6. Proveriti da nacrt može da se učita i bez zapisa javnog sadržaja u katalogu.

### Ograničenje lokalnog pokretanja

Stranica koristi unapred podešeni demo kurs samo u `Development` režimu, zato što backend lokalno seed-uje članstvo nastavnika na tom kursu. Produkcioni prikaz učitava kurseve preko `/api/courses`; identitet mora doći iz stvarne prijave, a server i dalje odlučuje o dozvoli.

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
- **Forma:** prikuplja naziv, cilj, težinu, tip i oznake; za izmenu prikazuje postojeće vrednosti kada ih API dostavi.
- **API klijent:** `api/labMetadata.ts` šalje zahteve za metapodatke, učitavanje forme i izmenu naziva, a odgovore o greškama priprema za prikaz u formi.
- **Zavisnosti:** API za laboratorije i metapodatke, format grešaka iz serverskog dela i API detalja laboratorije za učitavanje vrednosti pri izmeni.
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
| Kreiranje laboratorije | `POST /api/laboratories/` sa `{ title }` |
| Kreiranje metapodataka | `POST /api/laboratories/{id}/metadata` sa ciljem, težinom, tipom i oznakama |
| Izmena naziva | `PATCH /api/laboratories/{id}/title` sa `{ title }` |
| Učitavanje podataka za izmenu | `GET /api/catalog/{id}` — trenutni poziv forme; ugovor za ovu putanju i odgovor sa svim metapoljima nije potvrđen |
| Izmena metapodataka | `PATCH /api/laboratories/{id}/metadata` sa ciljem, težinom, tipom i oznakama |

### Status integracije

- Testovi forme proveravaju slanje podataka, prikaz serverske greške uz polje, učitavanje pri izmeni i izmenu naziva. Odgovori API-ja u ovim testovima su simulirani, pa oni ne potvrđuju rad sa stvarnim serverom.
- Build frontenda je uspeo, a na poslednjoj proveri prošlo je 65 testova u 9 test fajlova.
- U lokalno pregledanoj grani Osobe 5 postoji `GET /api/laboratories/{id}`, ali njegov odgovor sadrži samo osnovne podatke o laboratoriji. To nije dovoljno za popunjavanje cilja, težine, tipa i oznaka. Potrebno je dogovoriti i potvrditi API ugovor koji vraća sva polja forme pre nego što se izmena laboratorije može potvrditi end-to-end.
- Čuvanje i validacija na stvarnom serveru zavise od dostupnosti i spajanja odgovarajućih serverskih API-ja. Frontend ne prikazuje neuspešan zahtev kao uspešno čuvanje.

## Scenario i redosled demonstracije

### Kada serverski API-ji budu dostupni

1. Pokrenuti API i frontend prema README-u.
2. Otvoriti `/teacher/labs/new`; u razvojnom režimu ekran koristi test-identitet nastavnika `marko`.
3. Uneti naziv, cilj, težinu, tip i oznake.
4. Izabrati **Sačuvaj laboratoriju** i proveriti stvarni odgovor servera.
5. Poslati neispravnu vrednost i pokazati da se serverska greška prikazuje uz odgovarajuće polje.
6. Kada se potvrdi API za učitavanje svih metapodataka, otvoriti `/teacher/labs/{labId}/edit`, proveriti učitane vrednosti, promeniti podatke i sačuvati ih.

### Ako serverski API-ji još nisu dostupni

Prikazati ekran i objasniti koja polja se šalju, kako forma prikazuje greške i gde je granica poverenja. Navesti da testovi koriste simulirane odgovore i ne predstavljati serversko čuvanje ili učitavanje pri izmeni kao provereno.

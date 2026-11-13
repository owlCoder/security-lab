# ACC-01 — Tok podataka: kreiranje i arhiviranje kursa

Dijagram prikazuje kako pozivajući servis ili test koristi model i kontekst.
Autentifikacija i autorizacija su odgovornost pozivajućeg sloja.
Proces učitavanja i arhiviranja koristi `CoursesDbContext` da učita kurs,
a zatim `Course.Archive` da promeni njegovo stanje u memoriji.

## Dijagram toka podataka

```mermaid
flowchart TB
    caller["Pozivajući servis ili test"]

    subgraph modelBoundary ["Model kursa i pristup bazi — ACC-01"]
        createProcess(("1. Validacija i kreiranje"))
        archiveProcess(("2. Učitavanje i arhiviranje"))
        saveProcess(("3. Čuvanje promena"))
        courseStore[("PostgreSQL — courses.Courses")]
    end

    caller -->|"Code, Name, Description, CreatedBy"| createProcess
    createProcess -->|"Course sa statusom Active"| saveProcess
    createProcess -->|"Greška validacije"| caller

    caller -->|"CourseId i ArchivedBy"| archiveProcess
    archiveProcess -->|"CourseId za učitavanje"| courseStore
    courseStore -->|"Postojeći podaci kursa"| archiveProcess

    archiveProcess -->|"Course sa statusom Archived i arhivskim podacima"| saveProcess
    archiveProcess -->|"Greška aktera ili obrisan kurs"| caller

    saveProcess -->|"INSERT ili UPDATE uz UTC datume"| courseStore
    courseStore -->|"Potvrda upisa ili kršenje ograničenja"| saveProcess
    saveProcess -->|"Ishod čuvanja ili greška konkurentne izmene"| caller
```

## Objašnjenje toka

Pri kreiranju konstruktor proverava šifru, naziv, dužine teksta i autora, a kurs
dobija status `Active`. Pozivalac dodaje objekat u `Courses` i poziva `SaveChangesAsync`.

Pri arhiviranju pozivalac učitava postojeći kurs, poziva `Archive(actorId)` i zatim
`SaveChangesAsync`. Arhiviranje postavlja arhivska polja i `UpdatedBy`, dok kontekst
popunjava `UpdatedAt`. Kurs i njegovi podaci o kreiranju ostaju sačuvani.
Ponovljeni poziv `Archive` čuva prvog autora i trenutak arhiviranja.

`CanEnroll` nakon arhiviranja vraća `false`. Servis za upis mora proveravati
trenutno stanje kursa i uskladiti novi upis sa arhiviranjem.
Stvarne istorijske entitete treba povezati pri integraciji sa odgovarajućim modulima;
test očuvanja istorije koristi minimalan entitet samo u test projektu.

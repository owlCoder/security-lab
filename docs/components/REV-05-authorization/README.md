# REV-05: autorizacija i trajni audit

Implementacija zatvara tri primjedbe pregleda commita `6e310e9`: povezuje stvarne
dozvole i sve pomocne politike, uskladjuje samoodobravanje sa politikom kursa i
cuva audit stvarnog HTTP toka. Odluke su opisane u [ADR-001](ADR-001.md), a
rezultati i scenariji u [TEST-EVIDENCE](TEST-EVIDENCE.md).

Dijagram tokova podataka, granice povjerenja i negativni tokovi prikazani su u
[DATA-FLOW](DATA-FLOW.md).

Aktuelni obim i zavisnosti su u [KT2-STATUS](KT2-STATUS.md), a ponovljiva
demonstracija u [KT2-DEMO](KT2-DEMO.md). TEST-EVIDENCE i ACCESS-ALIGNMENT su
istorijski izvjestaji za datume i commit-e koje navode.

## Akcije i provjere

| Akcija | Resurs za IAccessDecision | Dodatni uslov |
|---|---|---|
| revision.entry | null (ulazna provjera) | Potvrdjen aktivni korisnik; ne zamjenjuje prava nad konkretnom revizijom |
| revision.create | course sa GUID-om kursa ucitanog iz laboratorije | Eksplicitno create pravo; autor laboratorije, poznat i podudaran kurs, postojeca politika kursa |
| revision.read | revision sa GUID-om revizije | Eksplicitno read pravo za kurs ucitan iz revizije |
| revision.mine | revision | Korisnik je autor i ima mine pravo |
| revision.pending.reviews | revision | Eksplicitno pravo za kurs te revizije |
| revision.submit | revision | Korisnik je autor i ima submit i read prava |
| revision.approve | revision | Approve i read prava; autor se odbija samo ako kurs zahtijeva nezavisno odobrenje |
| revision.return | revision | Return i read prava |
| account.unlock | null (System akcija) | Potvrdjen aktivni subjekt i IAdminDirectory pravo; Identity provjerava ciljni nalog |

Nepoznata akcija, nedostajuca politika, neaktivan subjekt, neispravan resurs i
nedostajuce pravo daju Deny. Nema automatskog odobravanja na osnovu globalne
administratorske ili saradnicke uloge. Nedostajuca politika objave ne omogucava
odobravanje. Promjena sadrzaja/verzije i uslovi masine stanja i dalje se provjeravaju
u ReviewService i domenu pod transakcijskim zakljucavanjem.

## Pouzdani izvori

- RevisionResourceReader cita AuthorId, CourseId i RequiresIndependentApproval iz
  serverske baze, ukljucujuci odsustvo politike. Klijent ne odredjuje ove vrijednosti.
- Program registruje PostgresRevisionCoursePermissionReader. Autoritativni izvor
  su eksplicitni redovi u `revision_security."CoursePermissions"`: UserId, CourseId,
  Action, IsActive i opcioni ExpiresAt. Provjera cita aktuelno stanje pri svakom pozivu.
  Prazan, opozvan, istekao ili nedostupan izvor ne daje pravo i ne prelazi na slabiji izvor.
- Prava se dodjeljuju serverskom administracijom. Web aplikacija nema javnu rutu za
  njihovu dodjelu, a njen runtime DB nalog treba da ima samo SELECT na toj tabeli.
  Nema seed-a testnih korisnika niti automatskog davanja prava svim nastavnicima.
- Za hostove koji nisu ukljucili persistent izvor ostaje adapter javnog Access
  IMembershipReader ugovora: aktivno clanstvo istog korisnika i kursa, nastavnik ili
  saradnik sa imenovanom delegacijom. Produkcioni Program koristi persistent prava.
- Rute `/api/revisions` autentifikuju se preko postojeceg Identity ISessionManager.
  Access provider i API resolver koriste potvrdjenu sesiju. Nema razvojnog identiteta
  kao rezerve za nedostajucu, opozvanu ili MfaPending sesiju. Identity middleware
  ponovo validira sesiju prije endpointa. Javni Identity IAccountStatusReader dodatno
  provjerava trenutno stanje naloga: Blocked, Archived i nepoznat nalog ne mogu
  koristiti ranije izdatu sesiju. Opoziv sesija pri blokadi naloga pripada
  postojecem Identity ugovoru. Ostale rute koriste svoje postojece providere.

## Samoodobravanje

RevisionApprovalPolicy v2 i permission adapter prate isto pravilo kao domen:

| RequiresIndependentApproval | Autor sa potrebnim pravima | Drugi korisnik sa potrebnim pravima |
|---|---|---|
| true | Deny | Allow |
| false | Allow | Allow |
| Politika nedostaje | Deny | Deny |

Allow ovdje znaci autorizaciju. Objava i dalje zahtijeva odgovarajuce stanje,
verziju, hash i validan snapshot. Dozvoljeno samoodobravanje ne daje nedostajuca prava.

## Audit i atomarnost

API host registruje stvarni PostgreSQL Audit writer pozivom
`AddAudit(builder.Configuration)` u `Program.cs`. Obicni `ITrustedAuditWriter`
koristi `ConnectionStrings:Audit`, dok `ITransactionalAuditWriter` za REV promjene
koristi PostgreSQL transakciju koju mu preda pozivalac. Ne treba vracati staru
API registraciju `AddAuditPersistence` sa Laboratories konekcijom.
Od ACC-05 integracije u PR-u #197, Program koristi `AddCentralAccessAudit()` i
`CentralAccessAuditWriter` za odbijene Access odluke, ukljucujuci REV akcije.
`RevisionAuditForwarder` je stari adapter za izolovane hostove/testove i nije
registrovan u Program.cs; dodatna registracija uz centralni writer moze duplirati
zapise. Zaseban scope centralnog writera izbjegava hvatanje scoped writera u singleton
Access servis. Akter dolazi iz validirane sesije; korelacija iz postojeceg serverskog
middleware-a. Arbitraran HTTP actor ili correlation header se ne koristi.

Ovo ukljucuje i `revision.entry`: odbijanje na ulaznoj provjeri prije poslovnog
handlera takodje stize u trajni Audit store. Endpoint metadata koristi
RevisionActions.Entry konstantu, a centralni mapper cuva validnu akciju. Za neautentifikovan zahtjev
upisuje se jedan `access.denied`, Anonymous akter bez ID-ja, action=revision.entry,
denialReason=unauthenticated i serverska korelacija. Time se ne dodjeljuju nova
prava, niti entry postaje kursna akcija ili zamjena za naknadne provjere resursa.

Poslovni tok upisuje dogadjaje `revision.submitted`, `revision.returned` i
`revision.approved`. Sadrze aktera, serversko UTC vrijeme, kurs, ID revizije, autora,
ID operacije i content hash. Objava dodatno sadrzi ID laboratorije i broj objavljene
verzije. Komentar, snapshot, referentni odgovor, cookie i token nisu audit detalji.
Audit validator zahtijeva obavezna polja i provjerava njihove formate.

IDN-05 IP odbijanje koristi `auth.login.throttled`/Denied: Anonymous akter,
serverska korelacija, bez resursa naloga i bez detalja zahtjeva. Zakljucani nalog
koristi `auth.login`/Failure sa `reasonCode=account_locked`; poznati nalog je cilj,
a nepotvrdjeni pozivalac Anonymous. Ovaj razlog ne razlikuje novo zakljucavanje
od narednog pokusaja nad vec zakljucanim nalogom. Granice su u [KT2 statusu](KT2-STATUS.md).

ReviewService poziva javni ITransactionalAuditWriter iz Audit modula sa svojom
PostgreSQL transakcijom. Audit modul ostaje vlasnik tabele i validacije; API ne
pristupa njegovim internim tipovima. Stanje, odluka, potvrda idempotentnosti i audit
insert se potvrde zajedno. Ako audit upis ne uspije, odgovor je 503 audit_unavailable
i transakcija se ponistava. Odbijanje pristupa ostaje odbijanje i kada ne uspije
njegov audit, prema postojecem Access ugovoru, uz tehnicki alarm.

EventId je ID poslovne operacije. Ponovljen uspjesan zahtjev sa istim kljucem vraca
vec sacuvanu potvrdu bez druge objave i drugog audit dogadjaja. I pri ponavljanju se
ponovo provjeravaju dozvole. Poslovna baza i audit schema moraju biti u istoj bazi
za ovu transakcijsku integraciju; nema alternativnog audit skladista niti outbox-a.

## Priprema baze i prava

REV poslovno skladiste i dozvole koriste `ConnectionStrings__Laboratories`, a
obicni Audit writer koristi zasebno podesavanje `ConnectionStrings__Audit`.
Za jedinstvenu REV audit evidenciju i ovu transakcijsku integraciju obje konekcije
treba da ciljaju istu fizicku PostgreSQL bazu; migracioni i runtime nalozi i dalje
moraju imati odvojena prava. `ConnectionStrings__Audit` se ne zamjenjuje Laboratories
registracijom u kodu. Postojece Identity i Authoring konekcije ostaju potrebne za
njihove funkcije. Migracije se izvrsavaju eksplicitno
administratorskim/migracionim nalogom; web host ne migrira produkciju automatski.

Za lokalnu pripremu Audit baze i ogranicene runtime uloge pratiti
[Local Audit database required by Identity login](../../DEVELOPMENT.md#local-audit-database-required-by-identity-login).
Postojeci lokalni `.env` treba dopuniti vrijednostima `AUDIT_RUNTIME_PASSWORD` i
`ConnectionStrings__Audit` iz `.env.example`, izvrsiti migraciju i bootstrap runtime
uloge, pa restartovati API. Bez uspjesnog trajnog audita ispravna prijava moze vratiti
`503 login_unavailable`; sama Laboratories konekcija nije dovoljna.

```powershell
dotnet tool restore
dotnet ef database update --project backend/Platform.Laboratories.Infrastructure --context LaboratoriesDbContext
# Postaviti REVISION_SECURITY_MIGRATION_CONNECTION_STRING na istu fizicku bazu.
dotnet ef database update --project backend/Platform.Api --context RevisionSecurityDbContext
# Postaviti AUDIT_MIGRATION_CONNECTION_STRING na istu fizicku bazu.
dotnet ef database update --project backend/Platform.Audit --context AuditDbContext
```

Novi context ima sopstvenu istoriju migracija u revision_security schemi. Nema
izmjene postojecih Laboratories migracija ili podataka. Migracioni i runtime nalozi
moraju biti odvojeni. Runtime treba poslovna prava na laboratories tabelama,
SELECT na CoursePermissions i SELECT/INSERT na audit.audit_events; ne treba mu
UPDATE/DELETE/TRUNCATE/DDL nad audit tabelom niti izmjena dozvola. Audit runtime
ogranicenja provjerava postojeci `scripts/audit/test-postgres.ps1`.

Dodjela jednog prava: [provision-permission.sql](provision-permission.sql), sa psql
parametrima user_id, course_id i action. Skripta se izvrsava samo administrativnim
nalogom nad izabranom bazom. Dodjeljuju se tacno potrebne akcije, bez wildcard-a.
Za tipican tok autor dobija create/read/mine/submit, a pregledavac
read/pending.reviews/approve/return za konkretan kurs. IsActive=false ili ExpiresAt
ukida pravo pri sljedecoj provjeri. account.unlock se ne dodjeljuje ovom tabelom:
ono ostaje sistemska Identity dozvola, a stvarna lockout operacija dio vlasnika IDN-05.

Od PR-a #214 `StoredLaboratory` cuva opcioni CourseId. Stari redovi ostaju NULL,
pa se kreiranje revizije odbija dok LAB vlasnici ne utvrde pripadnost kursu iz
pouzdanog izvora. Redovna izmjena laboratorije ne moze promijeniti kurs.
Prije rada sa pravim LAB skladistem primijeniti i `AddLaboratoryCourse` migraciju
nad njegovom Authoring konekcijom, kroz migracioni postupak LAB/persistence vlasnika.
Runtime nalog ne dobija DDL prava radi ove pripreme.

## Provjere i granice

Pozitivni HTTP test ne zamjenjuje sesije, prava, publication source ili audit writer
fake servisima: koristi produkcioni host, stvarne PostgreSQL adaptere, validirane
sesije i testne podatke u izolovanom Docker kontejneru. Provjerava i stvarni upis
odbijenog pristupa, oba podesavanja politike, opoziv/istek i odbijanje drugog kursa.
Namjerno nedostupna audit tabela dokazuje rollback, bez fake uspjeha writera.

Ocekivano odbijanje politike se cuva kao `policy_denied`; greska pri provjeri
politike ostaje `policy_error`. Oba koda su dozvoljena audit ugovorom. Postojeci
test opozvane dozvole provjerava tacnu sacuvanu vrijednost i jedan denial dogadjaj.

Promjene rjesavaju REV-05 autorizaciju i njenu audit integraciju. Nisu tvrdnja da su
zavrseni lockout, oporavak lozinke, administrativni interfejs za dodjelu prava ili
ostali zadaci drugih clanova. Za te operacije postoje navedeni javni ugovori.

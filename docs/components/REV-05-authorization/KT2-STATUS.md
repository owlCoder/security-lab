# KT2 — Luka: preostali posao i granice odgovornosti

Pregledano 19.11.2026, na osnovi `main` commita `b43cdd0`.
Ovaj pregled odnosi se na politike pristupa i audit, prema Lukinom pojašnjenju zaduženja.
Ne pripisuje mu cijeli IDN-05 ili REV-01/02/05 domen, API, frontend i skladište.

## Šta je čije

| Dio | Odgovornost | Trenutno stanje / sljedeći korak |
| --- | --- | --- |
| Predaja, vraćanje i odobrenje revizije | Markov REV API i tok; Luka povezuje provjere prava i audit | Postoje povezani tokovi i testovi trajnog audita, rollbacka i idempotentnosti. |
| Administrator/saradnik bez automatskog prava odobravanja | Lukine politike i mapiranje pouzdanih prava | Postoji: produkcioni host traži eksplicitne grantove, a globalna administracija ne dodjeljuje course approval pravo. |
| Audit odgovora 429 | Luka — producent i povezivanje s postojećim Audit ugovorom; limiter ostaje IDN-05 komponenta | Završeno i spojeno u main kroz [PR #204](https://github.com/owlCoder/security-lab/pull/204), squash commit `1c0f579`. Stari commit ne mora biti predak main-a nakon squash merge-a. |
| Zaključavanje naloga | Identity/IDN-05 vlasnik (dokumentacija i istorija navode Milana); Luka provjerava audit integraciju | Mehanizam zaključavanja nije Lukina implementacija. Postoji audit `auth.login` Failure sa `reasonCode=account_locked`; sada su dodati pravi HTTP/PostgreSQL dokazi. |
| Poseban događaj upravo nastalog zaključavanja | Zajednički dogovor Identity/IDN-05 i Audit vlasnika; Luka povezuje događaj | Još otvoreno: `CredentialResult.IsLockedOut` ne razlikuje novo od postojećeg zaključavanja. Ne dodavati novu semantiku ili katalog događaja jednostrano. |
| Stvarna operacija otključavanja | Identity vlasnik; Luka daje provjeru prava i povezuje audit | Postoje akcija/politika `account.unlock`, ali nema povezanog endpointa/use case-a koji uklanja blokadu i zapisuje uspjeh. Ovo nije Markov REV poslovni tok. |
| Usklađenost laboratorije i kursa pri kreiranju revizije | Zajednička granica: Markov `DraftRevisionService` + javni LAB ugovor; Luka provjerava da odluka koristi pouzdan resurs | Još otvoreno. API koristi courseId iz zahtjeva, a LAB `StoredLaboratory` nema CourseId. Potrebna je pouzdana javna veza laboratorija–kurs, pa provjera prije kreiranja. |
| Individualna dokumentacija i demonstracija | Luka za svoj dio; Marko i ostali za njihove komponente | Može se pripremati odmah, sa jasnim razdvajanjem završenog i otvorenih integracija. Predložene DFD slike nisu ovim izmjenama mijenjane. |

Dokaz Markovog obima je [REV-05-use-cases-api.md](../REV-05-use-cases-api.md), koji navodi autora i API tok.
Identity granice opisuje [IDN-05-lockout.md](../identity/IDN-05-lockout.md).
Audit 429 detaljno je opisan u [IDN-05-throttled-audit.md](../identity/IDN-05-throttled-audit.md).

## Šta je sada urađeno bez promjene tuđeg poslovnog koda

U `IdentityAuditPostgresIntegrationTests` dodata su dva slučaja. Koriste stvarni
Program/login, verifier, Identity bazu i Audit writer, sa zasebnim ograničenim
SELECT/INSERT audit nalogom u privremenom PostgreSQL kontejneru. Ne zamjenjuju audit
uspješnim fake writer-om.

1. `AccountLockoutHttpFlow_PersistsAuditAtThreshold_AndForLaterRejectedLogin`
   - Dva pogrešna pokušaja još ne zaključavaju nalog.
   - Treći pokušaj aktivira stvarni lockout; nov DB kontekst potvrđuje promjenu.
   - I zahtjev koji dostiže prag i kasnija ispravna lozinka nad zaključanim nalogom
     imaju po jedan stvarno sačuvan `auth.login`/Failure zapis sa razlogom `account_locked`.
   - Akter je Anonymous, nalog je cilj događaja, a korelacija je serverska.
   - Lažni korisnički/correlation headeri ne određuju aktera; detalji ne sadrže email ili lozinku.
   - Odgovor ostaje generički 401, bez Retry-After i bez nove sesije.
2. `AccountLockout_WhenAuditInsertFails_StaysLockedAndIssuesNoSession`
   - PostgreSQL stvarno odbije INSERT ograničenom runtime nalogu pri zaključavanju.
   - Stanje ostaje zaključano; nema nove sesije niti lažne potvrde da je događaj sačuvan.
   - Poslije vraćanja prava sljedeća prijava se i dalje odbija i njen audit zapis se sačuva.

Testni host sada može podešavati IP prag i prag zaključavanja. Postojeći 429 testovi
zadržavaju svoje podrazumijevane vrijednosti. Nisu mijenjani produkcioni kod,
Audit katalog, javni ugovori, migracije, Markov API ni Identity algoritam.

## Šta ovaj dokaz ne zatvara

Ovo dokazuje postojeći normalan tok i ponašanje pri odbijenom audit upisu.
Ne uvodi novi događaj `account.locked` i ne tvrdi da postoji razlika između novog
zaključavanja i ponovljene odbijene prijave. Promjena lockout stanja i audit i dalje
nisu jedna atomska transakcija; kod pada audita ili prekida zahtjeva nije garantovan
zapis svakog prelaza. Ta garancija traži dogovor s vlasnikom Identity transakcije
i Audit ugovora. Nisu implementirani otključavanje, outbox ni retroaktivna rekonstrukcija izgubljenih događaja.

## Provjere i ponovljivo pokretanje

Lokalna Release provjera 19.11.2026:

| Grupa | Prošlo | Palo | Preskočeno |
| --- | ---: | ---: | ---: |
| Identity audit producent i pravi HTTP/PostgreSQL tokovi | 18 | 0 | 0 |
| REV autorizacija i audit pravila | 87 | 0 | 0 |
| REV HTTP/PostgreSQL integracija | 48 | 0 | 0 |
| Ukupno | 153 | 0 | 0 |

`git diff --check` prolazi. Ovo je ciljana provjera, ne nova puna CI kampanja.
Nema produkcionih izmjena koje bi zahtijevale migraciju ili frontend build.

```powershell
dotnet test backend/Platform.Identity.Tests/Platform.Identity.Tests.csproj --configuration Release --filter "FullyQualifiedName~IdentityAuditProducerTests|FullyQualifiedName~IdentityAuditPostgresIntegrationTests"
dotnet test backend/Platform.Tests/Platform.Tests.csproj --configuration Release --filter "FullyQualifiedName~RevisionAuthorization"
dotnet test backend/Platform.Laboratories.IntegrationTests/Platform.Laboratories.IntegrationTests.csproj --configuration Release
```

Za PostgreSQL testove potreban je pokrenut Docker. Svaka grupa koristi svoje
privremene podatke; REVOKE/GRANT testovi mijenjaju samo runtime nalog u vlastitom
testnom kontejneru i vraćaju prava u finally bloku. Sirovi TRX izvještaji ove
provjere su lokalno u `TestResults/lockout-audit-evidence`.

## Konkretni dogovori koji još trebaju

- **Marko + LAB vlasnik:** koji javni ugovor pouzdano vraća kurs postojeće laboratorije
  i kako obraditi stare laboratorije bez te veze. Zatim odbiti podmetnuti courseId
  bez kreiranja revizije; Luka provjerava sigurnosni dio.
- **Identity/IDN-05 vlasnik:** operacija otključavanja i razlikovanje upravo nastale
  blokade od već zaključanog naloga. Potrebni su pouzdani identitet, cilj, rezultat i
  dogovorena transakcijska/ponavljajuća obrada audita.
- **Audit vlasnik:** potvrda eventualnih novih tipova i dozvoljenih polja; postojeći
  `auth.login` razlog ne predstavljati kao novi zaseban događaj koji još ne postoji.

Luka ne treba sam mijenjati tuđe tabele ili tvrditi da politika Allow znači da je
otključavanje izvršeno. Može sam završavati audit adaptere za postojeće ugovore,
negativne testove, dokumentaciju i dokaz svog individualnog doprinosa.

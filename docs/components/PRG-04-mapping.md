# PRG-04: čitanje, dodavanje i uklanjanje veza sa kompetencijama

**Zahtev:** jedna laboratorija može da proverava više kompetencija, a ista kompetencija može da bude povezana sa više laboratorija.

**Osnova:** postojeća implementacija sa grane `feature/prg-04-competencies`, commit `17b20d5`. Arhitektura, vlasništvo nad podacima i DFD su u [PRG-04-competencies.md](PRG-04-competencies.md). Dijagrami u ovom dokumentu prikazuju postojeći API i skladište; frontend i autorizacija laboratorije još nisu implementirani.

## HTTP ugovor

| Metod i putanja | Uspešan odgovor | Značenje |
| --- | --- | --- |
| `GET /api/competencies` | `200 { competencies: [...] }` | Ceo početni katalog |
| `GET /api/laboratories/{laboratoryId}/competencies` | `200 { laboratoryId, competencies: [...] }` | Kompetencije vezane za prosleđeni ID laboratorije |
| `PUT /api/laboratories/{laboratoryId}/competencies/{competencyId}` | `201` za novu vezu, `204` ako već postoji | Dodavanje jedne veze; bez tela zahteva |
| `DELETE /api/laboratories/{laboratoryId}/competencies/{competencyId}` | `204`, i kada veza već ne postoji | Uklanjanje samo jedne veze |

Stavka `competencies` sadrži `id`, `name` i opcioni `description`. Odgovor na uspešan novi `PUT` ima `Location` sa putanjom veze. Operacije ne primaju studentske odgovore, ocene, revizije ili podatke o usvojenosti kompetencija.

| Slučaj | Ishod handlera |
| --- | --- |
| Nema autentifikovanog identiteta ili je `UserId` prazan | `401 { error: "unauthenticated" }`, pre skladišta |
| `laboratoryId` je `Guid.Empty` | `400 { error: "invalid_laboratory_id" }` |
| `competencyId` je `Guid.Empty` | `400 { error: "invalid_competency_id" }` |
| Katalog ili skladište nije registrovano | `503 { error: "competency_storage_unavailable" }` |
| `PUT` referencira nepostojeću kompetenciju | `404 { error: "competency_not_found" }` |

Middleware može da završi zahtev sa `401` pre handlera, bez ovog JSON tela. GUID pogrešnog formata ne odgovara ruti sa `:guid` i ne stiže do handlera. `DELETE` za nepostojeću vezu daje `204`, bez posebne provere da kompetencija postoji.

## Tok dodavanja jedne veze

Tok počinje u handleru nakon usmeravanja. Puna linija predstavlja postojeće ponašanje; u njemu nema provere postojanja laboratorije ili prava nad njom.

```mermaid
flowchart TB
  START["PUT: LaboratoryId i CompetencyId"] --> AUTH{"Autentifikovan identitet<br/>i neprazan UserId?"}
  AUTH -->|"ne"| E401["401 unauthenticated"]
  AUTH -->|"da"| VALID{"Oba ID-a su neprazna?"}
  VALID -->|"ne"| E400["400 invalid_laboratory_id<br/>ili invalid_competency_id"]
  VALID -->|"da"| DI{"ILaboratoryCompetencyStore<br/>je registrovan?"}
  DI -->|"ne"| E503["503 competency_storage_unavailable"]
  DI -->|"da"| ADD["AddAsync<br/>validacija modela veze"]
  ADD --> EXISTS{"Kompetencija postoji<br/>u PRG-04 katalogu?"}
  EXISTS -->|"ne"| E404["404 competency_not_found"]
  EXISTS -->|"da"| INSERT["Parametrizovani INSERT<br/>ON CONFLICT DO NOTHING"]
  INSERT --> CREATED{"Dodat je novi red?"}
  CREATED -->|"da"| R201["201 Created<br/>Location: putanja veze"]
  CREATED -->|"ne; veza već postoji"| R204["204 No Content"]
```

Provera kataloga nalazi se u `PostgresLaboratoryCompetencyStore.AddAsync`. Nepostojeća kompetencija izaziva `KeyNotFoundException`, koju handler prevodi u `404`. Složeni ključ u bazi obezbeđuje da ponovljeni ili istovremeni `PUT` za isti par ne napravi dodatni red. Ostale greške baze nisu obuhvaćene granama `404` ili `503` ovog dijagrama.

## Sekvenca: dve kompetencije na jednoj laboratoriji

Preduslovi: server je razrešio autentifikovan identitet, skladište je registrovano, migracija je primenjena i K1/K2 postoje u katalogu. L označava isti trajni `LaboratoryId`. Dijagram prikazuje uspešan scenario; provera identiteta se ponavlja pri svakom zahtevu.

```mermaid
sequenceDiagram
  actor C as Autentifikovan HTTP klijent
  participant E as CompetencyEndpoints
  participant I as IIdentityContext
  participant S as PostgresLaboratoryCompetencyStore
  participant DB as PostgreSQL / prg04

  C->>E: GET /api/competencies
  E->>I: Provera Current i UserId
  I-->>E: Autentifikovan identitet
  E->>S: ICompetencyCatalog.ListAsync()
  S->>DB: SELECT kompetencija
  DB-->>S: Katalog
  S-->>E: Kompetencije
  E-->>C: 200 competencies

  loop Za K1, pa za K2
    C->>E: PUT /api/laboratories/L/competencies/K
    E->>I: Provera identiteta
    I-->>E: Autentifikovan identitet
    Note over E,S: Validacija ID-eva i dostupnost porta<br/>Provera laboratorije i prava još nije dodata
    E->>S: AddAsync(L, K)
    S->>DB: Provera K u katalogu
    DB-->>S: K postoji
    S->>DB: INSERT veze ON CONFLICT DO NOTHING
    DB-->>S: Broj dodatih redova
    S-->>E: true ako je nova veza, inače false
    E-->>C: 201 ako je nova veza, inače 204
  end

  C->>E: GET /api/laboratories/L/competencies
  E->>I: Provera identiteta
  I-->>E: Autentifikovan identitet
  E->>S: GetForLaboratoryAsync(L)
  S->>DB: JOIN veza i kataloga, WHERE LaboratoryId = L
  DB-->>S: K1 i K2, sortirano po nazivu i ID-u
  S-->>E: Lista kompetencija
  E-->>C: 200 laboratoryId i competencies

  C->>E: DELETE /api/laboratories/L/competencies/K1
  E->>I: Provera identiteta
  I-->>E: Autentifikovan identitet
  E->>S: RemoveAsync(L, K1)
  S->>DB: DELETE samo para L i K1
  DB-->>S: 0 ili 1 obrisan red
  S-->>E: false ili true
  E-->>C: 204 u oba slučaja
  Note over S,DB: Veza L-K2 i oba unosa u katalogu ostaju sačuvani
```

Veze se čuvaju u PostgreSQL-u i ostaju dostupne nakon ponovnog kreiranja aplikacionih servisa. To svojstvo proverava postojeći integracioni test; nije posledica čuvanja stanja u klijentu.

## Naredni tok autorizacije i korisničkog interfejsa

Ovo su **planirani** koraci koji dopunjuju postojeće dijagrame:

1. Pre poziva skladišta proveriti postojanje laboratorije kroz dogovoren LAB ugovor i pravo nad konkretnom laboratorijom kroz Access ugovor. Primeniti kontekst kursa i članstva; odbijeni zahtev ne sme da čita ili menja veze.
2. Tek nakon tih provera omogućiti nastavniku sa odgovarajućim pravom ekran koji učitava katalog i trenutne veze, dodaje jednu kompetenciju ili uklanja jednu vezu. Ekran ne uređuje početni katalog.
3. Pri grešci sačuvati prikaz potvrđenog stanja ili ponovo učitati veze. Pri promeni korisnika ili laboratorije ukloniti prethodne podatke i sprečiti da zakašnjeli odgovor prepiše novi prikaz.
4. Uskladiti Development izuzetak za prefiks `/api/laboratories` sa sesijskim identitetom PRG-04 i proveriti ceo tok kroz stvarni host. Trenutni razvojni izuzetak i njegova posledica opisani su u [arhitekturi](PRG-04-competencies.md).

## Postojeći testovi koji potkrepljuju tok

| Ponašanje | Test na PRG-04 grani |
| --- | --- |
| Anoniman poziv se odbija pre skladišta | `EveryOperation_RequiresAnIdentity_BeforeItUsesStorage` |
| Trenutna granica proverava samo identitet | `AuthenticatedCaller_CanAddAndRemoveALink_WithoutResourceAuthorization` |
| Prazni ID-evi i nepostojeća kompetencija | `InvalidOrUnknownIdentifiers_AreSafeHttpErrors` |
| Neregistrovano skladište | `AuthenticatedCaller_Gets503WhenCompetencyPersistenceIsNotConfigured` |
| Dve kompetencije i ponovno kreiranje servisa | `TwoCompetencies_SurviveDisposingAndRecreatingApplicationServices` |
| Ponovljeno i istovremeno dodavanje | `DuplicateLink_IsIdempotentAndDatabaseKeyAlsoRejectsDirectDuplicate`, `ConcurrentDuplicateAdds_CreateExactlyOneRelationship` |
| Uklanjanje jedne veze čuva ostale | `RemoveOneLink_PreservesOtherLinksAndSharedCatalogEntry` |

Testovi handlera koriste test identitet i skladište; ne predstavljaju proveru sesijskog middleware-a kroz stvarni HTTP host. Testovi PostgreSQL trajnosti zahtevaju posebnu test konfiguraciju. Ovaj dokument navodi postojeće testove, bez tvrdnje da su ponovo izvršeni u izmeni dokumentacije. Komande, migracije i raniji rezultati nalaze se u `docs/components/PRG-04-storage.md` na PRG-04 grani.

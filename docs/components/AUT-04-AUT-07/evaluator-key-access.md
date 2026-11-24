# AUT-04: Kostur modula i pristup evaluator ključu

**Tim:** 6

**Student:** Dušan Kovačević, PR 6/2023 (koordinator tima)

**Zahtevi:** AUT-04; povezano AUD-03

**Grane:** feature/AUT-04-skeleton (PR #44), feature/AUT-04-evaluator-key-access (PR #75)

**Revieweri:** Gajić (kostur), Antonić (pristup ključu)

## Odgovornosti i granice

Ovaj deo pravi okvir modula `Platform.LabVisibility` i jedini put do evaluator ključa.

- Kostur (PR #44): projekat modula, ugovori `ILabPublicContentReader`, `IEvaluatorKeyStore`, `ICourseMembership`, `ILabVisibilityAudit`, nazivi radnji `LabVisibilityActions`, registracija modula u `Program.cs` i InMemory implementacije za razvoj i testove.
- Pristup ključu (PR #75): politika `EvaluatorKeyPolicy` za radnju `lab.key.read`, servis `EvaluatorKeyReader` i audit `LoggerLabVisibilityAudit`.
- KT2 ugovori (kartica 1): radnja `lab.key.write`, `ISaveEvaluatorKey`, `ILabPublicationSource` sa stub-om; vidi [LabVisibility-KT2.md](../../contracts/LabVisibility-KT2.md).
- KT2 trajni audit (kartica 2): `AuditWriterLabVisibilityAudit` šalje svaki događaj modula u AUD modul tima 12 kao `lab.visibility.accessed`, kroz `ITrustedAuditWriter`.
- Ključ sme da čita samo servisni subjekat evaluatora (`system:evaluator`) i nastavnik kursa kome laboratorija pripada. Student nikad, ni kada je upisan na kurs.
- Ključ nema HTTP endpoint. Do njega se dolazi samo pozivom `EvaluatorKeyReader` unutar servera.

Domenski modeli, baza, studentski prikaz, pregled pre objave i ekrani pripadaju drugim članovima tima.

## Arhitektura komponente

```mermaid
flowchart TD
    Evaluator["Evaluator — EVAL tim, posle KT1"]
    Reader["EvaluatorKeyReader"]
    Decision["IAccessDecision — tim 3"]
    Policy["EvaluatorKeyPolicy (lab.key.read)"]
    Labs["ILabPublicContentReader"]
    Membership["ICourseMembership"]
    Store["IEvaluatorKeyStore"]
    Audit["ILabVisibilityAudit → AuditWriterLabVisibilityAudit"]
    AUD["ITrustedAuditWriter — AUD, tim 12"]

    Evaluator -->|"subjekat, labId, correlationId"| Reader
    Reader -->|"AccessRequest"| Decision
    Decision --> Policy
    Policy -->|"kurs laboratorije"| Labs
    Policy -->|"uloga na kursu"| Membership
    Reader -->|"samo posle Allow"| Store
    Reader -->|"akcija, labId, akter, ishod"| Audit
    Audit -->|"lab.visibility.accessed: action, laboratoryId, reasonCode"| AUD
```

`EvaluatorKeyReader` prvo traži odluku, a tek posle dozvole čita ključ. Svako čitanje, dozvoljeno ili odbijeno, ide u audit.

Politika je registrovana u registru tima 3 u `RegisterLabVisibilityAccess()`. Radnja `lab.key.read` je u katalogu označena kao radnja nad resursom `lab` u okviru kursa.

## DFD i granica poverenja

```mermaid
flowchart LR
    subgraph Client["Klijent — nepouzdana zona"]
        User["Student ili nastavnik"]
    end

    subgraph Server["Server — granica poverenja"]
        Api["Kontroleri Platform.Api"]
        Eval["Evaluator (u procesu)"]
        Reader["EvaluatorKeyReader"]
        Decision["Odluka o pristupu"]
        Key[("lab_evaluator_key")]
        Log[("Trajni audit — AUD, tim 12")]
    end

    User -->|"HTTP zahtev"| Api
    Api -. "nema zavisnosti ka ključu" .- Reader
    Eval -->|"zahtev za ključ"| Reader
    Reader -->|"subjekat, radnja, resurs"| Decision
    Decision -->|"Allow / Deny"| Reader
    Key -->|"ključ, samo posle Allow"| Reader
    Reader -->|"ključ"| Eval
    Reader -->|"bez sadržaja ključa"| Log
```

Ključ nikada ne prelazi granicu server–klijent. Nijedan kontroler ne prima `IEvaluatorKeyStore` ni `EvaluatorKeyReader`, što proverava arhitekturni test.

## Bezbednosni zahtevi

### Provere na serveru

- Odluku donosi `IAccessDecision` tima 3; bez identiteta, sa blokiranim nalogom ili bez politike zahtev se odbija.
- `EvaluatorKeyPolicy` dozvoljava evaluatoru i nastavniku kursa te laboratorije, a sve ostalo odbija: studenta, korisnika van kursa, nastavnika drugog kursa i nepoznatu laboratoriju.
- ID evaluatora nije GUID, a korisnički ID iz `IIdentityContext` uvek jeste, pa se korisnik ne može predstaviti kao evaluator.

### Osetljivi podaci

- `LabVisibilityAuditEvent` nema polje za sadržaj; zapisuju se samo radnja, laboratorija, akter, ishod i correlation ID (AUD-03).
- Odbijen zahtev vraća `null`, bez razloga odbijanja i bez informacije da li ključ postoji.

### Ograničenja

- Kad AUD ne sačuva zapis (`Unavailable` ili `Rejected`), odluka se ne menja: odbijanje ostaje odbijanje, a dozvoljeno čitanje ključa se ne prekida. Diže se alarm `LabVisibilityAuditLost` u logu, bez sadržaja. Fail-closed za upis ključa (`lab.key.write`, privilegovana promena) je deo kartice 5.
- Bez AUD modula u kontejneru (testovi, alati) audit ide u log kroz `LoggerLabVisibilityAudit`.
- Zapisi nemaju `CourseId`, pa ih AUD-06 (pregled za nastavnika po kursu) još ne prikazuje.
- Članstvo na kursu je razvojni stub dok ACC timovi ne objave ugovor.

## Negativni scenariji i testovi

Fajl: `backend/Platform.Tests/LabVisibility/EvaluatorKeyAccessTests.cs` (10 testova)

- evaluator i nastavnik kursa dobijaju ključ;
- upisan student, korisnik van kursa, nastavnik drugog kursa, blokiran nalog i anoniman korisnik dobijaju odbijanje;
- odbijeno čitanje upisuje audit događaj sa ishodom `Deny`;
- dozvoljeno čitanje upisuje audit događaj, a uhvaćen log ne sadrži tekst odgovora;
- arhitekturni test: nijedan kontroler ne zavisi od `IEvaluatorKeyStore` ni `EvaluatorKeyReader`.

Fajl: `backend/Platform.Tests/LabVisibility/LabVisibilitySkeletonTests.cs` (6 testova)

- nepoznata laboratorija se odbija za sve tri radnje;
- nepoznata radnja `lab.answers.dump` se odbija kao `UnknownAction`;
- dupla registracija radnji pada pri pokretanju;
- korisnik bez članstva nema ulogu na kursu.

Fajl: `backend/Platform.Tests/LabVisibility/LabVisibilityDurableAuditTests.cs` (19 testova)

- sa AUD modulom koristi se trajni audit, bez njega log;
- nastavnik, student, anoniman korisnik i evaluator dobijaju zapis sa ispravnim akterom (`User`, `Anonymous`, `Service`);
- odbijeno čitanje se upisuje kao `Denied` sa `reasonCode` `forbidden`;
- ni zapis ni kontekst ne sadrže tekst odgovora;
- svaki ishod modula (`Allow`, `Deny`, `NotFound`, `Saved`, `Published`, `Invalid`…) daje zapis koji AUD katalog prihvata;
- kad AUD nije dostupan, odluka ostaje ista, a alarm u logu ne sadrži odgovor.

Primer u `backend/Platform.Audit.Tests/AuditDraftExamples.cs` prolazi validator AUD modula i upis u PostgreSQL (`PostgresAuditEventStoreTests`).

## Dokazi rada

- PR #44: `AUT-04: add Platform.LabVisibility skeleton and contracts`
- PR #75: `AUT-04: evaluator key access policy and audit without secrets`

Pokretanje testova ove komponente:

```powershell
dotnet test backend/Platform.Tests/Platform.Tests.csproj --configuration Release --filter "FullyQualifiedName~EvaluatorKeyAccessTests|FullyQualifiedName~LabVisibilitySkeletonTests"
```

Lokalna provera 7.11.2026 na `main`: 16 uspešnih, 0 neuspešnih, 0 preskočenih.

## Plan za KT2

Povezaću `EvaluatorKeyReader` sa evaluatorom EVAL tima, tako da ključ i dalje ostane samo na serveru.

Urađeno (kartica 2): audit u logu je zamenjen trajnim auditom AUD tima, uz test da ni tamo ne završava sadržaj ključa.

Zameniću razvojni stub članstva ugovorom ACC tima i proširiti negativne testove za promenu uloge na kursu.

# Projektna celina: ASN-04 / ASN-06, celina 5 — Audit i API

**Tim:** 15

**Student:** Bubulj Milan / PR 136/2021

**Zahtevi iz specifikacije:** ASN-04 (limit pokušaja), ASN-06 (individualno produženje roka). Oslanja se na
AUD-01 (zapis ima akter, događaj, vreme i ishod), AUD-03 (bez tajni), AUD-04 (korelacioni ID), ACC-05
(podrazumevano odbijanje) i IDN-02 (identitet iz sesije).

## Odgovornosti i granice

| Član | Celina | Šta je njegovo |
|-|-|-|
| 1 | Baza i domen | `Assignment`, `StudentDeadlineOverride`, PostgreSQL za `IAssignmentRepository`, `IAttemptStore`, `IDeadlineExtensionStore`, migracije, seed |
| 2 | ASN-04 | `StartAttemptService` |
| 3 | ASN-06 | `ExtendDeadlineService` |
| 4 | Autorizacija | adapteri `IStartAttemptAuthorization`, `IDeadlineExtensionAuthorization`, `ICourseEnrollmentCheck` nad `IAccessDecision` |
| **5** | **Audit i API (ovaj dokument)** | audit adapter (akter, radnja, vreme, ishod, korelacioni ID, bez tajni) i kontroleri koji izlažu use-case-ove |

**Moj deo radi:**

- `AssignmentsController` sa dve rute, akter isključivo iz proverene sesije (`AssignmentCaller`).
- `StartAttemptApiHandler` i `ExtendDeadlineApiHandler`: validacija HTTP ulaza i mapiranje ishoda use-case-a u
  bezbedne HTTP odgovore (stabilan `code`, bez poruka izuzetaka).
- `IAssignmentAuditWriter` / `AssignmentAuditWriter`: dodaje aktera, vreme i korelacioni ID iz pouzdanog
  konteksta, čisti nacrt po allowlisti i upisuje kroz `ITrustedAuditWriter` Audit modula (tim 12).
- `DeadlineExtensionAuditAdapter`: implementacija porta `IDeadlineExtensionAuditWriter` koji je definisao član 3.
- `AddAssignmentsApi()`: DI; portovi članova 1 i 4 dobijaju **fail-closed** podrazumevane implementacije
  (`TryAdd`), pa host radi i pre njihovih adaptera, a sve se odbija.

**Moj deo NE radi** (da ne bi ulazio u tuđi posao): logiku limita i dostupnosti (član 2), pravila roka
(član 3), bazu i transakciju za uspešno produženje (član 1), odluku o pravima (član 4), Audit katalog,
skladište i čitanje audita (AUD-01/AUD-06, tim 12). Audit modul se samo poziva preko njegovog javnog ugovora.

## Endpointi

| Metoda i ruta | Zahtev | Odgovori |
|-|-|-|
| `POST /api/assignments/{assignmentId}/attempts` (ASN-04) | kolačić sesije, zaglavlje `Idempotency-Key: <guid>` | `201 {attemptId}`; `400 invalid_request_id`; `401`; `404 assignment_not_available` (i za nepostojeće i za zabranjeno, isto telo); `409 attempt_limit_reached`; `409 assignment_closed`; `503 temporarily_unavailable` |
| `POST /api/assignments/{assignmentId}/students/{studentId}/deadline-extensions` (ASN-06) | kolačić sesije, telo `{"newDueAtUtc":"2026-11-20T23:59:00Z"}` | `200 {assignmentId, studentId, dueAtUtc, previousDueAtUtc, extendedAtUtc}`; `400 invalid_deadline / invalid_student`; `401`; `403 forbidden` (i za nepostojeću dodelu); `409 deadline_extension_rejected`; `503` |

Svi odgovori imaju `Cache-Control: no-store` i `X-Correlation-ID` (iz `CorrelationMiddleware`).

## Arhitektura

![Arhitektura](diagrams/asn-audit-api-architecture.png)

Izvor: [diagrams/asn-audit-api-architecture.mmd](diagrams/asn-audit-api-architecture.mmd) (plavo = moj deo,
sivo = ostali članovi tima 15, narandžasto = drugi timovi).

## Dijagram toka podataka (DFD)

![DFD](diagrams/asn-audit-api-dfd.png)

Izvor: [diagrams/asn-audit-api-dfd.mmd](diagrams/asn-audit-api-dfd.mmd). Granice poverenja: (1) klijent je
nepouzdan; (2) server određuje aktera, kurs i vreme; (3) skladišta su iza portova. Klijent nikad ne šalje
aktera, kurs, broj pokušaja ni trenutni rok. Eventualna polja `actorId`/`courseId` u telu se ignorišu
(pokriveno testom).

## Audit zapis

| Polje (AUD-01) | Izvor |
|-|-|
| akter | `IIdentityContext` (sesija koju je proverio `SessionCheckMiddleware`); bez sesije `Anonymous` |
| radnja | `assignment.attempt.start` ili `assignment.deadline.extend` (format `ActionId`) |
| vreme | `TimeProvider` (UTC), postavlja ga i Audit modul |
| ishod | `Success`, `Denied`, `Failure` |
| korelacioni ID | `ICorrelationContext` (AUD-04, tim 12) |
| ostalo | `assignmentId`, kurs (samo ako je GUID), student (samo ako prolazi `PrincipalId`), `reasonCode`, stari/novi rok, `attemptId` |

Nikad se ne upisuju: token, kolačić ili ID sesije, lozinke, telo zahteva, poruke izuzetaka, slobodan tekst.
Nevažeće vrednosti se izbacuju, ne prepisuju se u log ni u izuzetak.

**Audit nikad ne menja odluku.** Use-case prvo odluči (i eventualno sačuva pokušaj ili rok), iz ishoda se
napravi HTTP odgovor, a audit se piše tek posle, van `try` bloka use-case-a, kao best-effort korak koji ne
baca izuzetke. Ako audit padne: uspešan start ostaje `201` (ponovljen zahtev sa istim `Idempotency-Key`
vraća isti pokušaj), odbijanje ostaje `404`/`403`, nema drugog „Failure” zapisa za isti zahtev, a podiže se
tehnički alarm bez detalja izuzetka. Isto važi za ASN-06 (`DeadlineExtensionAuditAdapter` vraća `false`
umesto izuzetka). Izuzetak je samo `Success` za ASN-06, koji član 1 upisuje u istoj transakciji sa rokom.

**Ko upisuje šta:** ASN-04 svaki ishod upisuje `StartAttemptApiHandler` (servis člana 2 nema audit port).
ASN-06 `Denied` i `Failure` upisuje servis člana 3 kroz `DeadlineExtensionAuditAdapter`; `Success` upisuje
`IDeadlineExtensionStore.SaveAsync` (član 1) u istoj transakciji, za šta može da koristi
`AssignmentAuditDraftMapper.ToPlatformDraft(DeadlineExtensionAuditAdapter.ToDraft(entry))` i
`ITransactionalAuditWriter`.

### Zahtev timu 12 (otvoreno)

Audit katalog (verzija 1) nema tipove za dodele. Predlog:

| Tip | Ishodi | Kurs | Resurs | Detalji |
|-|-|-|-|-|
| `assignment.attempt.started` | Success, Denied, Failure | opciono | nema (akter je student) | `attemptId` (Success), `reasonCode` (Denied/Failure) |
| `assignment.deadline.extended` | Success, Denied, Failure | opciono dok se ne dogovori tip `CourseId` | `Account` (student) | `previousDueAt`, `newDueAt`, `reasonCode` |

Dok tipovi nisu dodati, Audit modul ih odbija sa `UnknownEventType`. Tada se **odbijanja** (`Denied`) upisuju
kao postojeći `access.denied` sa `action` i `denialReason = policy_denied`, a za `Success`/`Failure` se
podiže tehnički alarm. Test `ToPlatformDraft_MatchesCatalogSchema_WhenTeam12AddsTheType` automatski proverava
naše nacrte čim tim 12 doda tipove. Otvorena tačka iz `ASN-06-contracts.md`: `Assignment.CourseId` je string, a
`AuditEventDraft.CourseId` je `Guid`; kurs se šalje samo kada je string ispravan GUID.

## Bezbednosni zahtevi

- **Provera na serveru:** akter samo iz sesije + aktivan nalog; kurs iz same dodele; odluka u use-case-u kroz
  portove člana 4 pre čitanja ili izmene (`[DelegatedAuthorization]`, dodato u inventar
  `ApiEndpointCoverageTests` uz komentar za review tima 3).
- **Bez nabrajanja:** ASN-04 vraća isti `404` za nepostojeću i zabranjenu dodelu; ASN-06 isti `403`.
- **Validacija ulaza:** `Idempotency-Key` mora biti GUID; rok mora imati eksplicitnu zonu (`Z` ili `±hh:mm`), do
  40 znakova; student ID proverava servis (`PrincipalId`).
- **Fail-closed:** bez adaptera članova 1/4 start vraća 404, produženje 403; neuspeh audita ne menja odluku
  (testirano i kada writer baci izuzetak); tehnička greška use-case-a daje `503` bez detalja.
- **Negativni scenariji (testirani):** bez sesije, blokiran nalog, student van kursa, nastavnik drugog kursa,
  student koji šalje `actorId` nastavnika u telu, nepostojeća dodela, limit dostignut, istekao rok, ponovljen
  `Idempotency-Key`, rok bez zone/u prošlosti/raniji, nevažeći student ID, student nije upisan, pad skladišta,
  pad audita, prekinut zahtev, ubačene vrednosti nalik tajnama.

## Eksplicitno ograničenje: produkcioni portovi

Produkcione implementacije portova pripadaju članu 1 (`IAssignmentRepository`, `IAttemptStore`,
`IDeadlineExtensionStore`) i članu 4 (`IStartAttemptAuthorization`, `IDeadlineExtensionAuthorization`,
`ICourseEnrollmentCheck`) i nisu deo ove celine. Dok ih ne registruju:

- `AddAssignmentsApi()` registruje fail-closed podrazumevane implementacije (oznaka `IFailClosedAssignmentPort`):
  ASN-04 vraća `404`, ASN-06 `403`; ništa se ne čita ni ne menja.
- `AssignmentsCompositionCheck` pri pokretanju hosta loguje spisak nepovezanih portova (`Warning` u
  Development, `Error` van njega), pa ograničenje nije tiho.
- Kada član 1 ili 4 registruje pravi adapter (pre ili posle `AddAssignmentsApi()`), on se koristi umesto
  podrazumevanog, a provera ga više ne prijavljuje (testovi `CompositionCheck_*`).

## Dokaz rada

- **Fajlovi:** `backend/Platform.Api/Modules/Assignments/**` (novo, uključujući `AssignmentsCompositionCheck`), `backend/Platform.Api/Program.cs`
  (+2 linije), `backend/Platform.Api/Platform.Api.csproj` (+referenca na `Platform.Assignments`),
  `backend/Platform.Access.Tests/ApiEndpointCoverageTests.cs` (+2 rute u inventar),
  `backend/Platform.Tests/AssignmentsAuditApi/**` (testovi), `docs/components/assignments-audit-api/**`.
- **Pokretanje testova:**
  ```
  dotnet build backend/SecurityLab.slnx --configuration Release
  dotnet test backend/SecurityLab.slnx --configuration Release --no-build
  ```
  Samo moji testovi: `dotnet test backend/Platform.Tests/Platform.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~AssignmentsAuditApi"`
- **Testovi (94 slučaja, NUnit):** `AssignmentAuditDraftMapperTests` 20, `AssignmentAuditWriterTests` 10,
  `DeadlineExtensionAuditAdapterTests` 8, `StartAttemptApiHandlerTests` 17, `ExtendDeadlineApiHandlerTests` 21,
  `AssignmentsHttpTests` 18 (ceo HTTP pipeline preko `WebApplicationFactory<Program>`). Portovi članova 1 i 4 i
  Audit skladište su test dvojnici; pravi `StartAttemptService` i `ExtendDeadlineService` se koriste.
- **Demonstracija (KT1):** pokrenuti `AssignmentsHttpTests` i pokazati: 401 bez sesije, 201 i audit sa akterom i
  istim `X-Correlation-ID`, 409 iznad limita, isti 404 za tuđu i nepostojeću dodelu, 403 za studenta na ASN-06
  iako u telu šalje `actorId` nastavnika.
- **Sledeći koraci i zavisnosti:** tim 12 dodaje `assignment.*` u katalog; član 1 upisuje Success u transakciji
  i registruje PostgreSQL portove; član 4 registruje prave adaptere autorizacije; član 2 po želji dodaje
  namenski izuzetak za limit (sada se prepoznaje po poruci, test `LimitMessage_MatchesStartAttemptService` čuva
  ugovor) i individualni rok u `StartAttemptService`; frontend poziva dve rute.

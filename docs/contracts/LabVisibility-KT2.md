# Ugovori: upis evaluator ključa i stanje objave (AUT-04, AUT-07, KT2)

| | |
|-|-|
| **Vlasnik** | Tim 6, `Platform.LabVisibility` (AUT-04, AUT-07) |
| **Verzija** | 0.1 (DRAFT dok ga AUT-01/AUT-06 i REV timovi ne potvrde u PR-u) |
| **Zahtevi** | AUT-04, AUT-07, LAB-04, ACC-05, AUD-03 |

Do potvrde susednog tima u pregledanom PR-u, svako ime i pravilo ispod je predlog.

## 1. Upis evaluator ključa

Nastavnik kursa upisuje tačne odgovore za nacrt laboratorije. Ključ putuje samo od browsera ka serveru: nijedan endpoint ga ne vraća, nijedna greška ga ne ponavlja, audit nema polje za njega.

### Radnja

| Radnja | Opseg | Resurs | Privilegovana | Ko sme |
|-|-|-|-|-|
| `lab.key.write` | `Course` | `lab` | da | samo nastavnik kursa te laboratorije, samo dok je laboratorija `Draft` |

Politika `EvaluatorKeyWritePolicy` je registrovana u katalogu tima 3. Do kartice 5 odbija sve zahteve.

### HTTP

`PUT /api/labs/{labId}/evaluator-key`

Zahtev (`application/json`):

```json
{
  "answers": [
    { "taskId": "t1", "expectedAnswer": "SHA-256" }
  ]
}
```

| Status | Kada | Telo |
|-|-|-|
| `204` | ključ je sačuvan | bez tela |
| `400` | ključ ne prolazi validaciju | `{ "code": "key.unknown_task" }` (samo kod, nikad poslat odgovor) |
| `401` | zahtev bez identiteta | `{ "code": "unauthenticated" }` |
| `404` | odbijeno ili nepostojeće (isto telo) | `{ "code": "lab_not_available" }` |
| `409` | laboratorija je objavljena ili arhivirana | `{ "code": "lab.published" }` |

Svaki odgovor nosi `Cache-Control: no-store`. Identitet dolazi iz sesije (`LabCaller`), nikad iz tela zahteva.

Kodovi za `400` (`SaveEvaluatorKeyErrors`):

| Kod | Značenje |
|-|-|
| `key.unknown_task` | odgovor za zadatak koji ne postoji u laboratoriji |
| `key.missing_task` | neki zadatak nema odgovor |
| `key.duplicate_task` | zadatak ima više od jednog odgovora |
| `key.empty_answer` | prazan odgovor |
| `key.answer_too_long` | odgovor je duži od dozvoljenog |

### Aplikacioni ugovor

| Tip | Opis |
|-|-|
| `ISaveEvaluatorKey.ExecuteAsync(SaveEvaluatorKeyCommand, SubjectRef?, correlationId, ct)` | use case; implementira `SaveEvaluatorKey` (kartica 5), koristi ga kontroler (kartica 6) |
| `SaveEvaluatorKeyCommand(LabId, Answers)` | `ToString()` ne ispisuje odgovore |
| `EvaluatorKeyAnswerInput(TaskId, ExpectedAnswer)` | `ToString()` ne ispisuje odgovor |
| `SaveEvaluatorKeyResult(Outcome, ErrorCode?)` | `Outcome`: `Saved` → 204, `Unauthenticated` → 401, `NotFound` → 404, `Published` → 409, `Invalid` → 400 |

Audit događaj (kartica 5): `lab.key.write`, `labId`, akter, ishod, `correlationId`. Bez sadržaja ključa (AUD-03).

## 2. Stanje objave od AUT-01/AUT-06 i REV

AUT-01/AUT-06 su vlasnici nacrta, REV je vlasnik objave revizije. Mi ne pravimo i ne objavljujemo laboratoriju; pitamo za njeno stanje i od toga odlučujemo šta student vidi.

```csharp
public enum LabPublicationStatus { Draft, Published, Archived }

public sealed record LabPublication(Guid LabId, Guid RevisionId, LabPublicationStatus Status, DateTimeOffset ChangedAt);

public interface ILabPublicationSource
{
    // null = laboratorija ne postoji kod vlasnika objave
    ValueTask<LabPublication?> GetAsync(Guid labId, CancellationToken cancellationToken = default);
}
```

| Pravilo | |
|-|-|
| Odgovor nosi samo stanje | nikad sadržaj laboratorije ni odgovore |
| Ključ se ne čuva u nacrtu | čuva se samo kroz naš `IEvaluatorKeyStore` (AUT-04) |
| Objava bez potpunog ključa | odbija se kod nas (`ApplyLabPublication`, kartica 7) i ide u audit |
| Arhivirana laboratorija | student dobija 404; nastavnik i dalje ima pregled |

### Stub dok adapter ne postoji

`StubLabPublicationSource` (KT2, stavka 5):

| Zahtev | Odgovor |
|-|-|
| laboratorija `4444…` (demo) | pozitivan: `Published` |
| laboratorija `5555…` (demo) | pozitivan: `Draft` |
| bilo koji drugi ID | negativan: `null` |

`Set(LabPublication)` menja stanje u testovima i na demou. Testovi: `LabVisibilityKt2ContractTests`.

## Potrošači i potvrda

| Tim | Šta treba da potvrdi | Stanje |
|-|-|-|
| AUT-01/AUT-06 (`Platform.Authoring`) | ključ se ne čuva u nacrtu; ID nacrta = `labId` | čeka |
| REV | `ILabPublicationSource` ili događaj o objavi sa istim poljima | čeka |
| Tim 6, frontend (Ferjuc) | JSON oblik `PUT /evaluator-key` | dogovoreno |

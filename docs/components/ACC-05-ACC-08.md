# ACC-05 i ACC-08 — komponenta autorizacije

| Stavka | Vrednost |
|---|---|
| Tim | Tim 3 |
| Zahtevi | ACC-05, ACC-08 |
| Status | ACC-05 jezgro implementirano|
| Projekat | `backend/Platform.Access` |
| Studenti | PR 1/2023 Živanović Jovana<br>PR 16/2023 Musli Danijel<br>PR 18/2023 Spasić Veljko<br>PR 30/2023 Pavlović Sara<br>PR 32/2023 Stanković Pavle<br>PR 53/2023 Matić Željana<br>PR 60/2023 Todorović Nikolina |

## Ugovor za druge module

Drugi moduli pozivaju samo javni ugovor:

```csharp
ValueTask<AccessDecision> DecideAsync(
    AccessRequest request,
    CancellationToken cancellationToken = default);
```

`Allow` je dozvoljen samo kada registrovana politika eksplicitno vrati uspeh. Svaki drugi ishod je `Deny`. Pozivalac ne sme grešku, nedostajući podatak ili nepoznatu akciju tretirati kao dozvolu.

Ugovor trenutno obuhvata:

- `SubjectRef` — identitet i stanje naloga;
- `ResourceRef` — tip i identifikator resursa;
- `AccessRequest` — subjekt, akcija, resurs i korelacioni ID;
- `AccessDecision` — ishod, razlog, politika, verzija i korelacioni ID;
- `IActionCatalog`, `IAccessPolicyRegistry` i `IAccessAuditWriter` — granice za buduće adaptere.
- `IResourceOwnershipResolver` — server-side granica za potvrdu vlasništva resursa.

`IActionCatalog` izlaže tipizovane `ActionDefinition` zapise. Početni katalog sadrži
akcije iz matrice ACC-05, uključujući scope (`Public`, `System` ili `Course`),
tip resursa i oznaku privilegovane akcije.

## Pravilo registracije akcija

`Platform.Access` poseduje centralni, verzionisani katalog dozvoljenih akcija.
Akcija se mora dodati u `AccessActions` i registrovati kroz
`InMemoryActionCatalog` pre nego što je bilo koji modul upotrebi u politici,
endpoint metapodacima ili `AccessRequest`-u. Nepoznata akcija se odbija
(`UnknownAction`), tako da novi modul ne može proširiti authorization površinu
samostalnom konfiguracijom, bazom ili runtime registracijom.

Dodavanje akcije obuhvata:

1. definiciju ID-a u `AccessActions`;
2. deklaraciju scope-a, tipa resursa i privilegovanosti;
3. proveru kompletnosti i jedinstvenosti kataloga;
4. pripadajuću politiku i testove u modulu koji akciju koristi.

Centralni katalog je nameran izbor za početnu verziju: omogućava pregled cele
authorization površine kroz code review i sprečava da pojedinačni moduli
samostalno uvedu preširoku ili nekonzistentnu dozvolu.

Podaci o identitetu, ulozi, članstvu i vlasništvu moraju doći sa servera, ne iz headera, tela ili query stringa klijentskog zahteva.

## Implementacija 

Registracija zavisnosti je deo `Platform.Access/Composition`, dok `Program.cs` samo poziva `AddAccessControl()`. Ostali moduli mogu primeniti isti obrazac.

Fail-closed tok:

1. nema subjekta → `Unauthenticated`;
2. nalog nije aktivan → `AccountNotActive`;
3. akcija nije validna ili registrovana → `UnknownAction`;
4. politika ne postoji → `NoPolicy`;
5. politika odbije zahtev → `PolicyDenied`;
6. greška, izuzetak ili timeout u politici → `PolicyError`;
7. neusklađen scope ili tip resursa → `PolicyError`;
8. samo uspešna politika može vratiti `Allow`.

Audit se izvršava kroz lanac `ResilientAccessAuditWriter` →
`FilteringAccessAuditWriter` → `AggregatingAccessAuditSink` →
`LoggerAccessAuditSink`. Odbijanja se agregiraju po akteru, akciji, ciljnom resursu
i razlogu odbijanja; prvi događaj se emituje odmah, a ponovljena odbijanja se
objavljuju kao ograničeni summary zapisi nakon isteka aggregation prozora.
`AccessAuditFlushService` periodično i pri gašenju aplikacije flush-uje otvorene
summary zapise. Pipeline ima ograničene aggregation ključeve i pending retry
zapise, tako da rast broja događaja ne postane neograničen.

`ResilientAccessAuditWriter` izoluje audit od odluke: audit ima timeout,
prosleđuje cancellation token i greške audit adaptera ne menjaju već izračunati
`Allow` ili `Deny` ishod. Neuspeh i timeout se prijavljuju kao zasebni alarmi.
Audit record factory i sanitizer dozvoljavaju samo definisana polja, ograničavaju
dužinu vrednosti i odbacuju kontrolne/hostile vrednosti; tajne, telo zahteva i
studentski odgovori se ne upisuju.

`LoggerAccessAuditSink` je lokalni adapter preko host `ILogger` ugovora. Centralni
`Platform.Audit` ugovor još nije direktno integrisan; integracija se radi nakon
stabilizacije Audit modula. Testovi koriste eksplicitne recording/throwing
audit fake-ove. `EmptyMembershipReader`
i `EmptyResourceDescriptorResolver` ostaju namerni fail-closed fallback-i za slučaj
da domenski adapteri nisu registrovani; ne predstavljaju placeholder poslovnu
logiku i ne mogu odobriti zahtev.

`AddAccessControl()` registruje katalog, policy registry, decision engine, audit
pipeline i flush servis kao singleton komponente. Audit loggers koriste host
logger kada je dostupan, a `NullLogger` fallback u minimalnim kontejnerima; modul
ne konfiguriše globalni logging hosta.

Za `attempt.read` i `attempt.submit` politika zahteva server-side potvrdu vlasništva.
Ako resolver vlasništva nije dostupan ili ne potvrdi vlasnika, pristup se odbija.
Ownership adapter je pripremljen kroz `IResourceOwnershipResolver`; konkretni domenski
moduli treba da ga obezbede kroz dogovorenu integraciju.

## Dijagram arhitekture

```mermaid
flowchart LR
  API[Platform.Api] -->|AccessRequest| DEC[IAccessDecision]
  DEC --> CAT[IActionCatalog]
  DEC --> REG[IAccessPolicyRegistry]
  DEC --> AUD[IAccessAuditWriter]
  REG --> POL[Policy adapter]
  DEC -->|AccessDecision| API
  ID[Identity / membership] -. future adapter .-> API
  OWN[IResourceOwnershipResolver] --> POL
```

**Veza sa platformom:** `Platform.Api` je kompozicioni sloj; ostali moduli kasnije registruju svoje akcije, politike i server-side resolvere preko javnih ugovora.

## Dijagram toka podataka

```mermaid
flowchart LR
  U[Student / nastavnik / administrator]
  subgraph CLIENT[Granica poverenja: klijent]
    U -->|HTTP zahtev| API[Platform.Api]
  end
  subgraph SERVER[Granica poverenja: serverska platforma]
    API -->|server-side podaci| P[Proces odluke]
    P --> C[(Katalog akcija)]
    P --> R[(Registar politika)]
    P --> A[(Audit adapter)]
    P -->|Allow / Deny| API
  end
  API -->|generički rezultat| U
  API -.->|uloga/identitet iz zahteva se ignorišu| IGN[Klijentski header/body/query]
```


## Demonstracija

Iz korena repozitorijuma:

```bash
dotnet build backend/Platform.Api/Platform.Api.csproj --configuration Release
ASPNETCORE_ENVIRONMENT=Development dotnet run \
  --project backend/Platform.Api/Platform.Api.csproj \
  --urls http://localhost:5080
```

U drugom terminalu:

```bash
curl http://localhost:5080/api/health
curl http://localhost:5080/api/access/demo-deny
```

Očekivano:

- `/api/health` vraća `status: "ok"`;
- `/api/access/demo-deny` vraća `Deny` sa razlogom `UnknownAction`.

Važno: razvojni demo endpoint je registrovan samo kada je okruženje `Development`:

```csharp
if (app.Environment.IsDevelopment())
{
    app.MapGet("/api/access/demo-deny", ...);
}
```

Ako se API pokrene bez `ASPNETCORE_ENVIRONMENT=Development`, `/api/health` će raditi, ali će `/api/access/demo-deny` vratiti `404 Not Found`. To znači da ruta nije registrovana, a ne da je autorizacija odbila zahtev.

Ovim se proveravaju pokretanje API-ja i početni ACC-05 tok. Demo ruta je samo razvojna i namerno nije dostupna u produkciji.


## Planirani tok razvoja

1. Dogovor sa Identity, Laboratories, Attempts i Audit timovima.
2. Registrovane akcije i konkretne politike.
3. Endpoint metadata i fallback-deny.
4. Integracija sa centralnim `Platform.Audit` modulom nakon stabilizacije njegovog composition ugovora.
5. Domenski ownership adapteri i preostala ACC-08 pravila.
6. Jedinični, integracioni i negativni testovi u CI-ju.
## Integracija razvojne KT1 demonstracije (ACC-04/ADM-08)

KT1 endpoint-i su dostupni isključivo u Development okruženju uz eksplicitno
`Kt1Demo:Enabled`. KT1 koristi **izolovan** `DefaultDenyAccessDecision` sa sopstvenim
katalogom i registracijom svojih `AttemptPolicy` i `AdministrationPolicy` pravila.
KT1 servisi dobijaju taj imenovani decision engine kroz DI fabrike, dok ostatak
API-ja nastavlja da koristi globalni `IAccessDecision` iz ACC-05.

Razlog je što KT1 simulacija i produkciona matrica imaju isto ime akcije
`attempt.read`, ali različite izvore članstva i provere vlasništva. Ne sme se
preskočiti konflikt registracije tako da jedan modul neprimetno nasledi politiku
drugog. Oba engine-a zadržavaju fail-closed proveru opsega, resursa,
identiteta i konkretne politike; KT1 ne postaje alternativna dozvola na
produkcijskim endpoint-ima.

Za konačnu integraciju svih timova ostaje obavezno da rute eksplicitno
deklariraju `RequireAccess` ili odobreni `AllowPublic`; test endpoint
coverage-a se ne isključuje radi zelenog CI-a.

Integraciona provera 2026-11-05: resursne akcije Authoring i LabVisibility sada se registruju sa eksplicitnim tipom resursa i opsegom u kompoziciji pripadajućih modula; provera scope-a i dalje ostaje fail-closed.

## Migracija postojećih rutera bez promene njihovih autorizacionih pravila

`DelegatedAuthorizationAttribute` je eksplicitni integracioni most isključivo
za proverene starije endpoint-e koji već imaju samostalne guard-ove (LAB-01
`RequireLabAccess` i provera vlasništva, IDN i AUT handleri sa sopstvenim
`IAccessDecision` pozivom). Nije ekvivalent `AllowPublic`: samo ovim
označenim rutama dopušta se izvršenje njihovih već postojećih autorizacionih
provera. Novi neoznačeni endpoint ostaje zabranjen. ACC-05 test proverava
tačno odobrenu listu ruta i odbija nepoznate delegacije.

Audit probni `/__test/audit-events` postoji isključivo u `Testing` hostu
i oslanja se na testovima definisan sintetički kontekst. Ne postoji u
produkcijskom registru endpoint-a.

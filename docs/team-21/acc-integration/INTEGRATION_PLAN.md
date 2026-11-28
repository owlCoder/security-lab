# Team 21 — deo 9: integracije, ugovori i audit

- **Vlasnik:** član 9, Team 21.
- **Zahtevi:** ACC-01 i ACC-06; saradnja sa IDN, ACC-02/03, ACC-05, delegacijama, LAB/ASN/AUT i AUD.
- **Datum pregleda:** 2026-11-07.
- **Status lokalnog review-a (2026-11-08):** `feature/acc-integration-review-v2`, pending spoj `00b3c47` + `fe6507f`, bez commit-a. Produkcioni/testni izvori odgovaraju testiranoj detached probi; njeni rezultati se koriste kao postojeći dokazi. KT1 dizajn je odobren, a merge commit čeka zasebno završno odobrenje. Stari review i njegove četiri dokumentacione korekcije ostaju nepromenjeni. Produkcione i pune CI zavisnosti ostaju otvorene.
- **Radna grana:** `feature/acc-integration`, izvedena iz `feature/acc06-groups`; početni pregled na `736b8f2`, aktuelna osnova posle predaje testova na `3fca69c`.

> **Lokalni review-v2:** roditelj `fe6507f` + svih devet commitova do `00b3c47`. Originalna grana, stari review i raniji PoC ostaju sačuvani. [Izveštaj kandidata](MERGE_CANDIDATE.md) odvaja proveru prenosa na review-v2 od testiranja detached probe i istorijskih provera protiv `3b1e6ff`.

## 0. Stanje izolovanog kandidata

Kandidat ima ACC-01 i ACC-06 iz roditelja, stvarni `CourseStateReader` i mali host adapter za Access subject. Za označene Team 21 rute važi routing → AUD korelacija → IDN session validacija → scoped `SessionCurrentActor` / `Team21AccessSubjectProvider` → postojeći `RequireAccess` → servisne provere. Originalni REV subject dekorator ostaje unutrašnji provider za ostale rute; njihov redosled nije promenjen. Nema duplog poziva session middleware-a za Team 21.

Na osveženom kandidatu prolazi 259/259 bezbednih Team 21 slučajeva. Ranijih 182/182 odnosi se na stari samostalni roditeljski baseline `3b1e6ff`, ne na novo pokretanje baseline-a `fe6507f`. To nije broj 158 sa stvarne grane niti starih 295 iz prethodnog eksperimenta. Predatih 39 nije vraćeno. Produkcioni account reader ostaje deny-all; status iz IDN EF čitača nije automatski prihvaćen kao autoritativan. KT1 dizajn selektivnog host/Access spoja odobren je prema potvrdi korisnika; IDN dogovor o autoritativnom izvoru i sinhronizaciji statusa i stvarna ACC članstva/dozvole i dalje su KT2 zavisnosti. Detalji, tačni testovi i preostala ograničenja su u izveštaju kandidata.

## 1. Obuhvat i izvori — istorija stvarne grane

Ovaj dokument priprema povezivanje modula. Ne uvodi nove javne ugovore, ne menja poslovna pravila i ne preuzima autentifikaciju, upis studenata, delegacije, isporuku materijala ili audit skladište od njihovih vlasnika.

Dopuna nakon početnog pregleda: `SessionCurrentActor` je dodat u host integracioni sloj, a `Program.cs` sada eksplicitno zamenjuje `ICurrentActor` scoped registraciju tim adapterom. Postojeći `IIdentityContext` i IDN validacija ostaju isti. Uklonjeni su samo Development session bypass-i za `/api/courses` i `/api/materials`. Tabela Git izvora ispod beleži osnovu početne analize; sesijska aktivacija i njeni testovi opisani su u ovoj dopuni. Ne tvrdi se da su ostale međutimske integracije završene.

Izvori su stvarni kod na radnoj grani i lokalne udaljene reference osvežene prethodnim `git fetch origin`. Tokom izrade dokumenta nije rađen novi fetch. Kod drugih grana pregledan je bez checkout-a i spajanja:

| Referenca | Pregledani deo | Ograničenje |
| --- | --- | --- |
| Radna grana `736b8f2` | ACC-06 i platform IDN/Access/AUD | ACC-01 ima samo kostur |
| `origin/feature/acc01-courses`, `e8cfabe` | Model, servis, autorizacija, kontekst i endpointi ACC-01 | Nije uključen u radnu granu |
| `origin/feature/ACC-02-membership`, `4f0087e` | Membership ugovori, store, servis i DI | Zasebna grana; nije produkciona integracija sa Team 21 |
| `origin/feat/asn-05-group-access`, `887d30b` | Assignment scope i grupa | Zasebna grana; nema potvrđenog povezivanja sa Team 21 |

Prethodna Git provera pokazala je da `origin/main` (`e3c8f03`) još nema Team 21 projekte. README predviđa integracionu granu iz `main` nakon objedinjavanja funkcionalnosti; za ovaj rad korisnik je izričito izabrao podgranu iz ACC-06. Dalji PR ide u dogovorenu roditeljsku granu, uz pregled tima. Dokument ne odobrava niti izvršava merge.

Osnovna dokumentacija: [README](../README.md), [ACC-06 model](../ACC-06-model.md), [testovi i ograničenja](../acc01-acc06-tests/PROJECT_COMPONENT.md), [AUD ugovor](../../components/audit/CONTRACTS.md).

### KT1 dopuna nakon sesijskih i HTTP milestone-a

Prethodna tabela i navod o `main` predstavljaju istorijski pregled osnove `736b8f2`, ne trenutno udaljeno stanje. Osvežavanje referenci za raniji KT1 pregled 2026-11-07 pokazalo je `origin/main` na `e744fab` (ACC-01 je tamo spojen), a `origin/feature/acc06-groups` na `3b1e6ff`. Tadašnji radni HEAD bio je `c94882e`: ACC-01 model nije bio uključen u ovu granu, roditelj je imao 106 commitova koji nisu bili ovde, a ova grana šest svojih commitova. To nisu novi podaci o udaljenim referencama. Preporučeni cilj ostaje `feature/acc06-groups`, uz poseban dogovor o objedinjavanju; odobren rezultat budućeg PR-a nije potvrđen. Ova dopuna ne menja grane niti odobrava merge.

Istorijski rezultat pre predaje testova: 197/197 Team 21 slučajeva prolazilo je na `c94882e`, a pun skup ponovo je potvrđen pre smanjenja na osnovi `0c05a16`. Tadašnjih dvanaest account-status HTTP slučajeva proveravalo je Active → Blocked/Inactive → Active, isti važeći session token, 200 → 404 → 200 i odsustvo zaštićenih podataka u odbijenim odgovorima, kao i zabranu kreiranja grupe neaktivnom nastavniku.

Nisu dodati produkcioni adapteri za status, članstvo, stanje kursa, sadržaj ili centralni AUD. Početni rezultat od 118 testova u odeljku 9.1 ostaje istorijski; devet navedenih integracionih padova ponovo je potvrđeno tokom ranijeg KT1 pregleda. Tim rezultatima nije dokazano da su to pre-existing baseline padovi. Nisu ponovo proveravani u ovoj dokumentacionoj dopuni i ostaju otvorene blokade. [Arhitektura](ARCHITECTURE.md), [DFD](DFD.md) i [KT1 pregled](KT1_READINESS.md) sadrže granice, matricu odluka i odvojene aktuelne i istorijske rezultate lokalnih provera.

### Dopuna nakon predaje testova članu 8

Aktuelni dokaz na stvarnoj grani, commit `3fca69c`: **158/158 Team 21 test slučajeva prolazi, 0 preskočenih**; Team 21 Release build prolazi sa 0 upozorenja i 0 grešaka. Od 79 slučajeva iz sedam prethodnih commitova dela 9 zadržano je 40 ključnih, a 39 dodatnih izvezeno je članu zaduženom za deo 8. Svih 118 ranijih slučajeva drugih članova ostalo je neizmenjeno. Deo 5 zadržava odgovornost za ACC-06 backend upravljanje grupama, deo 9 za integracije, ugovore i audit, a deo 8 za sveobuhvatno automatizovano bezbednosno i integraciono testiranje.

Kompletni originali pet smanjenih test fajlova i zajednički `RecordingLogger` sačuvani su pre izmene u spoljašnjem `Team21-Part8-Handoff.zip`, zajedno sa projektnim kontekstom, klasifikacijom, uputstvima i kontrolnim sumama. Arhiva je proverena i dobija se od vlasnika predaje; njena lokalna putanja nije deo dokumentacije. Originalni testovi ostaju i u Git istoriji, na primer na `0c05a16`. Zadržane asercije, zajednički fixture-i i projektne reference nisu menjani. [Pregled predaje](KT1_READINESS.md#predaja-testova-delu-8) daje brojeve po fajlu i granice vraćanja dodatnih slučajeva.

Od ranijih dvanaest account-status HTTP slučajeva ostala su tri reprezentativna: Blocked za listu grupa, Inactive za materijalnu referencu i zabrana kreiranja grupe blokiranom nastavniku. Oba testa promene statusa zadržavaju isti token, 200 → 404 → 200 i provere odsustva zaštićenih podataka; test odbijenog upisa proverava nepromenjeno stanje grupa. Koriste kontrolisani testni `IAccountStatusReader`, stvarni `SessionCurrentActor`, IDN validaciju i Team 21 servise; **ne dokazuju autoritativni IDN status naloga**. Preostali HTTP fixture ima 19 slučajeva i reprezentativne provere ruta, umesto prethodnog šireg skupa od 41 slučaja.

Prethodni izbor od **295 Team 21 slučajeva** odnosi se na zaseban privremeni merged worktree sa roditeljskim izmenama i Access/IDN PoC-om, pre ovog smanjenja. Nije broj testova stvarne grane niti dokaz njenog prolaska; PoC nije prenet niti spojen u `feature/acc-integration`. Njegova Access odluka 403 za neaktivne naloge ne menja ovde postojeći servisni 404 ugovor. Predaja testova nije rešila CI ili merge blokade; privremeni worktree-i ostaju odvojeni.

Putanje u nastavku su relativne u odnosu na koren repozitorijuma. Putanja označena drugom Git referencom nije nužno dostupna u radnom stablu.

## 2. Postojeći Core ugovori

| Ugovor i putanja unutar `backend/Team21.Acc.Core/` | Postojeći članovi / odgovornost |
| --- | --- |
| `Interfaces/ICurrentActor.cs` | `Guid? UserId`, `bool IsAuthenticated`; identitet pozivaoca |
| `Interfaces/ICorrelationContext.cs` | `string CorrelationId`; veza sa serverskim zahtevom |
| `Interfaces/IAuditTrail.cs` | `RecordAsync(string action, Guid resourceId, AuditOutcome outcome, CancellationToken)`; privilegovane promene, ishodi `Success` i `Denied` |
| `Groups/Visibility/Interfaces/IAccountStatusReader.cs` | `GetStatusAsync(Guid accountId, CancellationToken)` → `Task<AccountStatus>` |
| `Groups/Visibility/Interfaces/ICourseMembershipReader.cs` | `FindAsync(Guid accountId, Guid courseId, CancellationToken)` → `Task<CourseMembership?>` |
| `Groups/Visibility/Interfaces/ICourseStateReader.cs` | `GetStateAsync(Guid courseId, CancellationToken)` → `Task<CourseState?>` |
| `Groups/Visibility/Interfaces/IVisibilityAuditWriter.cs` | `WriteDenialAsync(VisibilityDenial, CancellationToken)` |
| `Groups/Visibility/Interfaces/IMaterialVisibilityService.cs` | `CanAccessAsync(MaterialRef?, MaterialAction, CancellationToken)` i `FilterVisibleAsync(Guid courseId, CancellationToken)`; vraćaju `Result` / `Result<T>` |
| `Groups/IGroupMembershipReader.cs` | `GroupsOfAsync(accountId, courseId, ct)`; aktivne grupe, bez samostalne autorizacije |
| `Groups/IMaterialAccessRuleReader.cs` | `FindAsync(material, ct)`, `ListByCourseAsync(courseId, ct)`; čitanje pravila, bez samostalne autorizacije |
| `Groups/Interfaces/IGroupService.cs` | Kreiranje/čitanje grupa i čitanje/dodavanje/uklanjanje članova |
| `Common/Interfaces/IEntity.cs`, `IUpdatable.cs` | Identifikator i vremenski metapodaci; nisu međutimski adapteri |

Na **ACC-01 referenci**, `Courses/ICourseAuthorization.cs` definiše `CanCreateAsync`, `CanArchiveAsync` i `CanViewAsync`, svaki sa `Guid` identitetima i `Task<bool>` rezultatom. `Courses/ICourseService.cs` predstavlja poslovni servis. Ovi ugovori nisu trenutno prisutni na radnoj grani.

## 3. Matrica zavisnosti i odgovornosti kandidata

Broj tima naveden je samo gde je potvrđen pregledanim dokumentima. Ostali vlasnici označeni su zahtevom/modulom; broj i kontakt treba potvrditi u dogovoru.

| Zavisnost / potrošač | Odgovorni tim i izvor podataka | Postojeći ugovor | Potrebno povezivanje — predlog | Trenutno stanje |
| --- | --- | --- | --- | --- |
| Pozivalac ACC-01/06 | IDN login/sesije; validirana serverska sesija | Team 21 `ICurrentActor`; `Platform.Api.Interfaces.Identity.IIdentityContext.Current` | `SessionCurrentActor` u host integracionom sloju | Host u svim okruženjima koristi scoped sesijski adapter; početni fake/anonymous izbor ostaje samo pri samostalnom pozivu Team 21 modula |
| Status naloga ACC-06 | IDN-03; autoritativni status naloga | Team 21 `IAccountStatusReader`; IDN `Platform.Identity.Interfaces.IAccountStatusReader.FindAsync`; host `IAccountRepository.Find(Guid)` | Adapter potvrđenog IDN izvora statusa | Fake / deny-all u Team 21; IDN ima scoped `EfAccountStatusReader` nad `PlatformIdentityDbContext.Accounts`, ali status write ide kroz host `InMemoryAccountRepository`. Autoritativnost i sinhronizacija nisu odobrene |
| Članstvo i uloga ACC-06 | ACC-02/03; članstva kursa | Team 21 `ICourseMembershipReader`; Access `IMembershipReader.GetAsync`; na ACC-02 grani `IMembershipStore.FindActiveAsync` | Čitač koji eksplicitno mapira aktivnost, kurs i ulogu | Nema stvarnog izvora na radnoj grani; ACC-02 kod postoji zasebno |
| Delegirani pregled | Vlasnici delegacija ACC-07 i prava pregleda; serversko važeće pravo | Team 21 `CourseMembership.CanReview`; Access `DelegatedActions` | Mapiranje tačno dogovorene radnje i važenja prava | Nije potvrđen izvor ni dogovor koja radnja znači `CanReview`; ne pretpostavljati ga iz same uloge Assistant |
| Stanje kursa ACC-06 | Team 21, ACC-01; `courses.Courses` | `ICourseStateReader`; ACC-01 `ICoursesDbContext.Courses` | Stvarni EF čitač stanja kursa | Roditelj već daje model, DbSet i stvarni scoped `CourseStateReader`; ne implementirati duplikat |
| Dozvole ACC-01 | ACC-01 vlasnik i Access/ACC pravila; sistemsko pravo kreiranja i kursna prava | ACC-01 `ICourseAuthorization`; `Platform.Access.Contracts.IAccessDecision` | Adapter odobrenih pravila za create/archive/view | Roditeljski Development course authorization i deny-all drugde su sačuvani; postojeće Team 21 Access politike proveravaju aktivan nalog, ne kursne dozvole |
| Zabrana novog upisa posle arhiviranja | ACC-02 servis upisa, stanje daje Team 21 | ACC-02 `Platform.Membership.Contracts.ICourseReader.FindAsync` → `CourseInfo(Id, IsArchived)` | Adapter stvarnog ACC-01 stanja u postojeći ACC-02 ugovor | ACC-02 servis već proverava arhiviranje, ali njegov čitač kursa je privremen; modul nije na radnoj grani |
| Članstva grupa i pravila publike | Team 21 ACC-06; `groups` tabele | `IGroupMembershipReader`, `IMaterialAccessRuleReader` | Nije potreban novi čitač istih podataka | Stvarni EF čitači postoje; ne izlagati ih direktno kao nezaštićene endpointe |
| Stvarni materijali i prilozi | ASN/LAB, za priloge AUT-05; vlasnički repozitorijumi/sadržaj | Team 21 `IMaterialVisibilityService`; ASN ima `IAssignmentRepository`; LabVisibility ima `ICourseMembership` | Pozivanje provere pre sadržaja i filtriranje liste; dogovor upisa publike | Team 21 endpointi vraćaju reference. Nije pronađena primena Team 21 provere u pregledanim vlasničkim tokovima; ugovor za upis publike nije pronađen |
| HTTP autorizacija | Access, Team 3 / ACC-05; routing metadata i odluke | `RequireAccess`, `IAccessDecision`, `Team21SessionRequiredMetadata` | Scoped `Team21AccessSubjectProvider` i selektivni raniji IDN middleware | Implementirano samo u kandidatu; Access proverava Active, servisi zadržavaju vlasništvo/grupe; puni Development inventar nije izvršen |
| Audit promena | AUD, Team 12; centralno append-only skladište | Team 21 `IAuditTrail`; `Platform.Audit.Contracts.IAuditWriter` | Adapter događaja nakon usaglašavanja kataloga i pouzdanog upisa | Samo `LoggingAuditTrail`; centralni događaji kurseva/grupa nedostaju |
| Audit odbijanja vidljivosti | AUD, Team 12, uz Access saradnju | `IVisibilityAuditWriter`, `VisibilityDenial`; centralni `IAuditWriter` | Adapter razloga i resursa prema prihvaćenoj šemi | Samo logger; centralni katalog nije dovoljan za sve razloge/resurse |
| Korelacija i vreme | Platform/AUD i Team 21; serverski zahtev i UTC časovnik | Oba modula imaju svoj `ICorrelationContext`; `TimeProvider` | Zadržati zajedničku serversku vrednost, bez kastovanja interfejsa | Već povezano kroz `TraceIdentifier`; AUD middleware postavlja novi ID, Team 21 ga čita |
| Perzistencija | Team 21 Infrastructure i platform konfiguracija; PostgreSQL | `ICoursesDbContext`, `IGroupsDbContext`, `ITeamDbContext` | Postojeća registracija konteksta; nema novog skladišta | `courses` / `groups` odvojene šeme; dostupnost baze nije proverena ovim dokumentom |

Dokazi za matricu:

- `backend/Team21.Acc.Api/Team21AccModule.cs`, `Groups/GroupsModule.cs`, `Services/*.cs`.
- `backend/Team21.Acc.Infrastructure/Groups/Fakes/FakeDirectory.cs`, `Fake*Reader.cs`, `Defaults/*.cs`, `Services/*.cs`.
- `backend/Platform.Api/Program.cs`, `Services/Identity/SessionIdentityContext.cs`, `InMemoryAccountRepository.cs`, `Interfaces/Identity/IAccountRepository.cs`.
- `backend/Platform.Access/Contracts/IMembershipReader.cs`, `Domain/Models/CourseMembership.cs`, `Composition/AccessServiceCollectionExtensions.cs`.
- ACC-02 referenca: `backend/Platform.Membership/Contracts/ICourseReader.cs`, `Application/IMembershipStore.cs`, `MembershipService.cs`, `Composition/MembershipServiceCollectionExtensions.cs`.
- `backend/Platform.Audit/Contracts/*.cs`, `Composition/AuditServiceCollectionExtensions.cs` i [AUD ugovor](../../components/audit/CONTRACTS.md).

## 4. Mapiranje identiteta, statusa i prava

### 4.1. Identitet i nalog

`SessionCurrentActor` daje neprazan `UserId` samo za potvrđenog, autentifikovanog pozivaoca. `CallerIdentity.IsAuthenticated` i nivo `AuthenticationLevel.Active` moraju biti konzistentni; `None`, `MfaPending`, odsutna sesija ili prazan ID ne daju Team 21 identitet. Adapter ne validira lozinku, cookie ili token samostalno i ne kopira session handle u audit.

`SessionIdentityContext` čita rezultat `SessionCheckMiddleware`, a `SessionCurrentActor` prihvata samo autentifikovan Active identitet sa nepraznim UserId. Development bypass-i za `/api/courses` i `/api/materials` uklonjeni su u odobrenoj aktivaciji; ostali izuzeci nisu menjani. `X-Test-User` i IDN `X-Stub-User` nisu izvori identiteta u integrisanom hostu. `StubIdentityContext` postoji, ali host i dalje registruje `SessionIdentityContext`. Istek i opoziv proverava postojeći IDN validator, ne Team 21 adapter.

| IDN status | Predloženi Team 21 status | Pristup |
| --- | --- | --- |
| `Active` | `AccountStatus.Active` | Tek nakon ostalih provera |
| `Blocked` | `AccountStatus.Blocked` | Odbijen |
| `Archived` | `AccountStatus.Inactive` | Odbijen |
| Nema naloga / nepoznata vrednost | `Inactive` | Odbijen |

Ne kastovati enum brojeve: IDN i Team 21 vrednosti nisu isti ugovor. Tehnički kvar izvora nije isto što i odsutan nalog; videti odeljak 6.

Izvori: `backend/Platform.Api/Models/Identity/CallerIdentity.cs`, `Enums/Identity/AuthenticationLevel.cs`, `Middleware/SessionCheckMiddleware.cs`, `backend/Platform.Identity/Enums/AccountStatus.cs`, Team 21 `Groups/Visibility/AccountStatus.cs`.

### 4.2. Članstvo i delegacije

Access `MembershipStatus.Active` odgovara aktivnom članstvu; `Left` i `Suspended` ne daju aktivno članstvo. Uloge Student/Assistant/Teacher mapirati po značenju, uz potvrdu izabranog izvora. Nepoznata uloga, pogrešan korisnik ili drugi kurs ne smeju dati ovlašćenje. Model ACC-02 na drugoj grani zaseban je od Access modela; njegova aktivnost i uloge zahtevaju sopstveno eksplicitno mapiranje, ne pretpostavljenu binarnu kompatibilnost.

Na pregledanoj ACC-02 referenci `Domain/MemberRole.cs` ima Student/Teacher/Assistant: mapiraju se u istoimene Team 21 uloge. `Domain/MemberStatus.cs` ima Active/Left/Suspended: samo Active daje `Active=true`. Odsutan rezultat `IMembershipStore.FindActiveAsync` daje odsutno članstvo; ovaj store sam ne dokazuje delegirano pravo pregleda.

`CanReview` znači potvrđeno delegirano pravo pregleda, ne samo prisustvo bilo koje stavke u `DelegatedActions`. Tačna radnja, rok, opoziv i vlasnik provere ostaju otvoreni. Do dogovora ne izvoditi `true` iz nepoznatih prava. LabVisibility `ICourseMembership` poznaje samo None/Student/Teacher i nije dovoljan izvor asistenata/delegacija. Globalna uloga naloga nije dokaz uloge na konkretnom kursu.

Postojeće ponašanje `MaterialVisibilityService`:

| Pozivalac, uz aktivan nalog i članstvo | Aktivan kurs | Arhiviran kurs |
| --- | --- | --- |
| Teacher | Sve pronađene materijalne reference kursa | Zadržava pristup |
| Assistant sa `CanReview=true` | Kao Teacher za vidljivost | Zadržava pristup |
| Student ili Assistant bez pregleda | CourseWide ili presek aktivnih dozvoljenih grupa | Odbijen |
| Nečlan / neaktivan član | Odbijen | Odbijen |

Asistent bez pregleda u aktivnom kursu nije bezuslovno odbijen: prolazi obična pravila publike. Upravljanje grupama je strože: `GroupService` traži Teacher, ne samo `CanReview`. Čitanja grupa proveravaju nastavnika i nalog; stanje kursa se u tom servisu proverava za promene, ne za sva čitanja.

Za `RestrictedToGroups`, odsustvo aktivne dozvoljene grupe odbija običnog člana. Za `CourseWide` i dalje su obavezni status naloga, članstvo i stanje kursa. `MaterialAction` trenutno ima samo `Read`; nepoznata radnja se odbija.

Izvori: `backend/Team21.Acc.Infrastructure/Groups/Services/MaterialVisibilityService.cs`, `GroupService.cs`, Core `Groups/Visibility/CourseMembership.cs`, `MaterialAction.cs`, `Groups/MaterialVisibility.cs`.

### 4.3. Materijali i granice vlasništva

Team 21 `MaterialType` ima `Assignment` i `Attachment`. Laboratorija ili njena revizija nisu automatski ista referenca kao assignment. Na ASN-05 referenci assignment ima `AssignmentScope.Course` / `Group` i jednu string vrednost `GroupId`, dok Team 21 podržava skup Guid grupa. Mapiranje Course → CourseWide i Group → RestrictedToGroups može biti predlog tek nakon potvrde identifikatora, pripadnosti kursu i vlasništva nad publikom.

Nije pronađen prihvaćen ugovor za kreiranje/izmenu `MaterialAudience` i `GroupMaterialAccess`. Ne izmišljati ga niti direktno pisati u tuđe tabele. Vlasnički endpoint za sadržaj/prilog mora proveriti pristup pre čitanja/isporuke; uspešan Team 21 endpoint sa referencom ne dokazuje zaštitu sadržaja. Filtriranje se radi na serveru i ne sme otkrivati skrivene reference ili broj skrivenih rezultata.

Izvori: Team 21 `Core/Groups/MaterialType.cs`, `Api/Groups/MaterialEndpoints.cs`, `MaterialResponse.cs`; ASN-05 referenca `backend/Platform.Assignments/Domain/Models/Assignment.cs`.

## 5. Prva integracija: ACC-01 → ACC-06

**Status u kandidatu: implementirano u roditelju, bez novog Part 9 čitača.** Stvarna integraciona grana još nije sinhronizovana.

`Team21.Acc.Infrastructure/Courses/Services/CourseStateReader.cs` implementira postojeći `GetStateAsync(Guid, CancellationToken)` nad `ICoursesDbContext.Courses`. Koristi `AsNoTracking`, soft-delete filter i prosleđuje cancellation token. Active mapira u `CourseState.Active`, Archived u `CourseState.Archived`, a odsutan/nepoznat status u `null`. Ne kopira model, ne poziva korisnički autorizovan servis i ne menja podatke.

`GroupsModule.AddGroups` registruje taj čitač kao scoped u svim okruženjima. Grupni i material EF čitači ostaju isti. HTTP fixture kontrolisano zamenjuje course reader, pa njegov prolaz ne dokazuje stvarni PostgreSQL upit niti arhiviranje kroz bazu; pet migracionih/seeder slučajeva izostavljeno je iz bezbednog skupa. Autoritativni status, članstvo i upis studenata ostaju zasebne zavisnosti.

Za sam čitač nije potrebna nova tabela niti nova migracija. Test izvršavanja zavisi od postojeće ACC-01 šeme. Naknadna ACC-02 integracija koristi njegov postojeći `ICourseReader`, zasebnim adapterom. `Course.CanEnroll` nije automatska zabrana upisa u drugom modulu. Konkurentni upis tokom arhiviranja traži dogovor vlasnika o transakciji/zaključavanju; običan read adapter to ne garantuje.

## 6. Nedostupnost zavisnosti i HTTP ponašanje

**Zatečeno:** čitači vraćaju podatak, status ili `null`, bez eksplicitnog rezultata tehničke nedostupnosti. Servisi uglavnom ne hvataju njihove izuzetke. Trenutno nije implementiran zajednički ugovor za timeout niti garantovano mapiranje kvara na 503. Otkazivanje zahteva i grešku baze ne predstavljati kao dokaz da korisnik/kurs ne postoji.

**Predlog za usaglašavanje pre produkcionog povezivanja:**

| Događaj | Bezbednosno pravilo | Potrebna odluka / provera |
| --- | --- | --- |
| Nema potvrđenog identiteta | Ne dozvoliti operaciju; postojeći servisi daju Unauthorized | Održati `IsAuthenticated=false` i `UserId=null`; middleware može ranije dati 401 |
| Nepostojeći kurs/članstvo/pravilo | Provera vidljivosti daje generički NotFound; odsustvo članstva nastavnika takođe skriva kurs | Dodavanje ciljnog studenta bez aktivnog članstva daje Validation, ne NotFound; ne otkrivati postojanje tuđeg resursa |
| Neaktivan nalog ili članstvo | Bez pristupa | Dodavanje neaktivnog studenta trenutno daje Validation; nije svako odbijanje 404 |
| Timeout / kvar IDN, članstva, kursne baze ili pravila | Ne vratiti sadržaj i ne izvršiti novu privilegovanu promenu | Dogovoriti bezbednu tehničku grešku (predlog 503), bez detalja baze i bez lažnog pozitivnog fallback-a |
| Otkazan zahtev | Propagirati otkazivanje pre promene | Posebno obezbediti audit već potvrđene promene |
| Audit odbijene akcije ne uspe | Odbijanje ostaje; nema sadržaja | Adapter/host mora zabeležiti tehnički alarm bez tajni; trenutno bačeni izuzetak može prekinuti planirani 404 |
| Audit privilegovane promene ne uspe | Ne potvrditi promenu bez garantovanog zapisa | Potrebna zajednička transakcija ili outbox, ne samo exception posle SaveChanges |

Ne uvoditi novo keširanje koje bi ostavljalo opozvana prava važećim. Provere se ponavljaju pri narednom zahtevu. Postojeći `Result` i HTTP mapiranje ne proširivati ovim dokumentom.

## 7. Audit ugovor i otvorene odluke

Team 21 `IAuditTrail` i `IVisibilityAuditWriter` trenutno koriste `LoggingAuditTrail` i `LoggingVisibilityAuditWriter`. Pozivi obuhvataju `group.create`, `group.member.add`, `group.member.remove`, a ACC-01 grana `course.create` / `course.archive`. Ne evidentira se svaka validacija, čitanje ili skrivena stavka uspešne liste.

Centralni AUD ugovor 0.1 označen je kao draft. Postoje `IAuditWriter.WriteAsync(AuditEventDraft, ct)` i `ITrustedAuditWriter`, ali `AddAudit()` na radnoj grani registruje samo korelacioni kontekst. Writer, context provider i store nisu povezani kroz tu metodu. Identity `IAuditWriter` u drugom namespace-u nije centralni AUD writer.

| Stavka | Postojeće stanje / obavezna odluka |
| --- | --- |
| Ishod | Team 21 Success → AUD Success, Denied → AUD Denied, eksplicitno po imenu; brojevi enum-a se razlikuju. AUD Failure nema Team 21 ekvivalent |
| Događaji kurseva i grupa | Nisu u centralnom katalogu; AUD vlasnik mora prihvatiti šeme pre adaptera |
| Resursi | AUD prihvata Account/Course/Delegation; Group/Assignment/Attachment nisu prihvaćeni tipovi |
| Razlozi odbijanja | Direktno odgovaraju AccountNotActive → `account_not_active`, UnknownAction → `unknown_action`; ostali Team 21 razlozi nemaju precizno prihvaćeno mapiranje |
| `access.denied` | Dozvoljeni razlozi su `unauthenticated`, `account_not_active`, `unknown_action`, `no_policy`, `policy_error`; ne pretvarati sve razloge u pogrešan kod radi prolaska validacije |
| Kontekst | Actor i correlation iz pouzdanog servera; centralni writer određuje vreme. Ne kopirati proizvoljan HTTP body/header ili vreme klijenta |
| Rezultat upisa | `Recorded` je uspeh; `Rejected` i `Unavailable` nisu uspešan audit |
| Pouzdanost | Trenutni servisi rade SaveChanges pa audit. Adapter sam ne obezbeđuje atomarnost; dogovoriti zajedničku transakciju ili transactional outbox |
| Kontekst događaja | `IAuditTrail` ima samo action/resourceId/outcome; nema eksplicitan courseId, tip resursa ni ciljnog člana. Potrebnu dopunu ugovora usaglasiti, ne nagađati iz jednog ID-a |

Nikada ne slati lozinke, tokene, cookie, session identifikatore, sadržaj materijala, studentske odgovore ili slobodan tekst. Za retry koristiti dogovoreni stabilni EventId; korelacioni ID nije idempotency ključ. AUD ugovor traži da audit već sačuvane akcije ne zavisi samo od otkazanog HTTP tokena.

Validator proverava oblik i dozvoljene vrednosti konteksta; ne dokazuje poreklo identiteta. Sintaksno ispravan izmišljen actor prosleđen kroz `ITrustedAuditWriter` nije automatski prepoznat kao lažan. Zato su vezivanje za validiranu sesiju, kontrolisane serverske registracije i pregled svakog trusted adaptera obavezna granica poverenja.

Izvori: `backend/Team21.Acc.Infrastructure/Common/LoggingAuditTrail.cs`, `Groups/Defaults/LoggingVisibilityAuditWriter.cs`, `Groups/Services/GroupService.cs`; ACC-01 `Courses/Services/CourseService.cs`; `backend/Platform.Audit/Contracts/AuditEventCatalog.cs`, `AuditEventTypes.cs`, `AuditWriteResult.cs`, `AuditResourceRef.cs`, `Application/AuditDetailFormats.cs`, `Application/AuditWriter.cs`.

## 8. Arhitektura i Dependency Injection

- Core zadržava male interfejse bez EF i ASP.NET zavisnosti. Poslovne odluke ostaju u servisima; adapter prevodi podatke.
- EF adapter ide u Infrastructure i zavisi od interfejsa konteksta. Ne duplirati `GroupMembershipReader` i `MaterialAccessRuleReader`.
- `SessionCurrentActor` je u `Platform.Api/Modules/Team21/Integration/`, uz eksplicitnu scoped zamenu registracije u hostu. Team21.Acc.Api ne referencira Platform.Api; test projekat referencira host radi izolovanih i HTTP integracionih testova, bez kružne zavisnosti.
- Stvarni scoped kontekst ne ubrizgavati u singleton Access politike. Access trenutno koristi singleton politike i `EmptyMembershipReader`; eventualni adapter zahteva lifetime rešenje sa vlasnikom.
- Registraciju zamene uraditi eksplicitno, bez oslanjanja na slučajan redosled više `AddScoped` registracija. Testirati Development i Production.
- Kandidat zadržava roditeljski `RequireAccess` na svih 12 Team 21 ruta, uz zajednički serverski marker. Nema public/delegated zaobilaska; servisna pravila grupa i vlasništva ostaju aktivna.
- Team 21 rute sa `RequireAccess` ne zahtevaju unos u `ApprovedDelegatedRoutes`. Novi Testing metadata test potvrđuje 12 ruta i odsustvo public/delegated oznaka. Originalni ACC-05 Development inventar nije pokrenut zbog seedera; to ostaje CI ograničenje.
- `TimeProvider` i server-generated korelaciju zadržati. `HttpCorrelationContext` čita `TraceIdentifier` koji AUD middleware postavlja i vraća kao `X-Correlation-ID`.
- Proveriti konfiguracioni redosled: `AddTeam21Acc()` trenutno čita konekciju pre kasnijeg host `AddLocalEnvFile()`. Dokument ne proverava niti objavljuje tajne.

Izvori: Team 21 `.csproj` fajlovi, `Api/Team21AccModule.cs`, `Api/Services/HttpCorrelationContext.cs`, `backend/Platform.Api/Program.cs`, `backend/Platform.Access/Composition/AccessServiceCollectionExtensions.cs`, `AspNet/LegacyAuthorizationIntegrationExtensions.cs`, `backend/Platform.Audit/Correlation/CorrelationMiddleware.cs`.

## 9. Potrebni pozitivni i negativni testovi

Matrica ispod ostaje plan šire integracije; sesijski deo je sada pokriven izolovanim `SessionCurrentActorTests` i HTTP `SessionCurrentActorHostIntegrationTests` u Team 21 test projektu. HTTP testovi koriste stvarni host u Testing okruženju, postojeći `SessionManager`, stvarne Team 21 servise i izolovane EF InMemory baze sa sintetičkim sesijama. Development uklanjanje bypass-a proverava se zasebnim malim pipeline-om sa stvarnim middleware-om, bez izvršavanja startup migracija ili seedera. To nije dokaz PostgreSQL ponašanja ili kompletne integracije stvarnih naloga i članstava. Ostale testove proširiti sa članom 8 bez dupliranja odgovornosti.

| Oblast | Pozitivni slučaj | Negativni slučajevi / očekivana bezbednosna posledica |
| --- | --- | --- |
| CourseStateReader | Stvarni aktivan i arhiviran red daju odgovarajući enum | Nepostojeći/obrisan kurs i prazan ID daju null; nepoznat status nikad Active; otkazivanje se propagira |
| Svežina kursa | Posle stvarnog arhiviranja naredni zahtev vidi Archived | Bez zastarelog fake-a/keša; grupa/add/remove ne upisuju promenu u arhiviran kurs |
| Kurs i vidljivost | Teacher i reviewing assistant zadržavaju pristup arhiviranom kursu | Student nema pristup; unknown kurs nema pristup ni privilegovanom korisniku |
| DI | Jedan stvarni scoped čitač se resolve-uje u oba okruženja | Fake/deny-all ne ostaju izabrani; nema captive scoped zavisnosti u singleton-u |
| Identitet | Validirana Active sesija daje odgovarajući UserId | Nema/istekla/opozvana/MfaPending sesija i Guid.Empty nemaju identitet; spoof header ne daje produkcioni pristup |
| Nalog | IDN Active omogućava ostale provere | Blocked/Archived/nepoznat/nepostojeći odbijeni; kvar ne daje pristup |
| Članstvo | Aktivni član očekivanog korisnika i kursa mapira se tačno | Drugi kurs/korisnik, Left/Suspended/uklonjeno članstvo i nepoznata uloga ne daju pravo |
| Delegacije | Tačno važeće pravo daje CanReview | Istek/opoziv/nepovezano pravo ne daje CanReview; proveriti naredni zahtev |
| Grupe | Član dozvoljene aktivne grupe vidi materijal | Druga grupa, soft-deleted član, arhivirana grupa, grupa drugog kursa i prazan skup dozvola odbijeni |
| Sadržaj i liste | Vlasnički ASN/LAB endpoint isporučuje dozvoljen sadržaj | Direktan URL, izmenjen ID i alternativna putanja ne zaobilaze proveru; lista ne vraća skrivene reference |
| ACC-01 autorizacija | Odobreni create/archive/view prolaze | Neovlašćeno kreiranje 403; arhiviranje/pregled tuđeg kursa 404, prema postojećem CourseService ponašanju |
| ACC-02 upis | Aktivan kurs prihvata dozvoljen upis | Arhiviran/nepostojeći kurs ne upisuje člana; konkurentno arhiviranje posebno proveriti nakon dogovora |
| Audit schema | Prihvaćen događaj prolazi stvarni AUD validator | Nepoznat događaj/resurs/razlog, zabranjeni detalji i strukturno neispravan kontekst se odbijaju; zaseban test adaptera dokazuje da HTTP ulaz ne određuje actor |
| Audit pouzdanost | Promena i garantovan događaj potvrđeni zajedno | Rejected/Unavailable ne ostavljaju potvrđenu promenu bez zapisa; otkazivanje i retry ne gube/dupliraju događaj |
| Audit odbijanja | Odbijanje sa odgovarajućom serverskom korelacijom | Kvar audita ne daje sadržaj; alarm bez tajni; client correlation header ne određuje zapis |
| Nedostupnost | Obnovljena zavisnost omogućava novu regularnu proveru | Timeout/kvar baze ili modula ne vraća zaštićen sadržaj niti koristi fake kao produkcioni fallback |

Poznato ograničenje: `GroupService.AddMemberAsync` ne proverava posebno CourseId vraćenog članstva ciljnog studenta. Adapter mora proveriti odgovor izvora; dodatnu zaštitu servisa usaglasiti sa njegovim autorom. `MaterialVisibilityService` proverava UserId bez posebnog IsAuthenticated uslova, pa identitetski adapter mora održavati konzistentnost. Ove nalaze ne popravljati usput u dokumentacionom zadatku.

Postojeći dokazi i ograničenja: `backend/Team21.Acc.Tests/Groups/*.cs`, `Api/HeaderCurrentActorTests.cs`, `Infrastructure/TeamDbContextBaseTests.cs` i [opis test komponente](../acc01-acc06-tests/PROJECT_COMPONENT.md). Njihovo postojanje ne dokazuje izvršavanje novih adaptera, PostgreSQL ni kompletan HTTP tok.

### 9.1. Zatečene integracione blokade van dokumentacionog zadatka

**Istorijski nalazi sa stvarne grane.** Prethodna osnova `3b1e6ff` i stari kandidat imali su AUD 17/17 i REV 5/5. Nova detached proba protiv `fe6507f` ponavlja 17/17 i 5/5; novi samostalni roditeljski baseline nije pokretan. Te roditeljske izmene nisu delo ovog PoC-a. ACC-05 Development inventar nije izvršen; ne tvrdi se prolaz punog CI-ja. Detalje poređenja daje [izveštaj kandidata](MERGE_CANDIDATE.md). Preostali tekst odeljka beleži tadašnje stanje.

Pregled 2026-11-07 potvrdio je uspešan backend Release build (0 upozorenja, 0 grešaka) i 118 uspešnih Team 21 testova. Širi izbor backend testova, uz izostavljanje scenarija koji mogu primeniti migracije, dao je 1.205 prolaznih, 9 neuspešnih i 10 preskočenih testova. Team 21 testovi uključeni su i u taj širi zbir. Dokazi izvršavanja lokalno su u ignorisanom direktorijumu `TestResults/acc-integration-review/` i nisu deo ovog dokumentacionog commita.

Sledećih devet padova predstavljaju zatečene integracione blokade; ne popravljaju se u ovom zadatku. **Nije izvršeno poređenje sa neizmenjenom roditeljskom granom ili drugim baseline-om, pa nije dokazano da su padovi postojali pre ove dokumentacione izmene. Puna CI provera ne prolazi i push nije odobren.**

| Modul / test | Zabeleženi pad i granica odgovornosti |
| --- | --- |
| ACC-05: `ACC05_Every_real_route_is_protected_or_approved_public` | Osam Team 21 endpointa nedostaje u `ApprovedDelegatedRoutes`; pregled servisne autorizacije i inventara usaglasiti sa Access vlasnikom |
| REV: `Approve_WithoutRegisteredPolicy_DeniesBeforeDatabase(True,Forbidden)` | Očekivan 403, dobijen 401; test podešava Access identitet bez validirane IDN sesije, koju zaseban middleware zahteva |
| AUD: `Request_CorrelationIdIsAcceptedByAuditValidator` | HTTP probna ruta vraća 401, pa očekivani uspešan odgovor nije dostupan |
| AUD: `Request_WithClientCorrelationHeader_IgnoresClientValue("attacker-chosen-id")` | Umesto očekivanog JSON odgovora stiže prazan 401 odgovor |
| AUD: `Request_LogsWrittenDuringRequestCarryCorrelationScope` | Probni zahtev dobija 401 pre ulaska u kontroler |
| AUD: `Request_GetsServerIdInContextTraceIdentifierAndHeader` | Parsiranje očekivanog JSON odgovora pada zbog praznog 401 odgovora |
| AUD: `Request_WithClientCorrelationHeader_IgnoresClientValue("corr-0001")` | Umesto očekivanog JSON odgovora stiže prazan 401 odgovor |
| AUD: `TwoRequests_GetDifferentCorrelationIds` | Probni zahtev dobija 401 umesto očekivanog uspešnog odgovora |
| AUD: `Request_WithClientCorrelationHeader_IgnoresClientValue("0123456789abcdef0123456789abcdef")` | Umesto očekivanog JSON odgovora stiže prazan 401 odgovor |

AUD probni kontroler ima `[DelegatedAuthorization]`, ali nema `[AllowAnonymous]` niti test postavlja validiranu IDN sesiju, pa `SessionCheckMiddleware` vraća 401. To je prepreka za dokaz HTTP korelacije, ne dokaz da middleware ne generiše korelacioni ID. Sa vlasnicima dogovoriti kako probna ruta dobija odgovarajući testni identitet; ne menjati produkcionu autorizaciju radi prolaska testa.

Izvori: `backend/Platform.Access.Tests/ApiEndpointCoverageTests.cs`, `backend/Laboratories.Tests/ReviewAuthorizationTests.cs`, `backend/Platform.Audit.Tests/CorrelationContextTests.cs` i `backend/Platform.Api/Middleware/SessionCheckMiddleware.cs`. PostgreSQL migracioni scenariji nisu izvršeni zbog ograničenja ovog zadatka; navedeni rezultat nije dokaz prolaska pune CI procedure.

## 10. Redosled rada i blokade

| Redosled | Rezultat | Spremnost / vlasnik sledećeg koraka |
| --- | --- | --- |
| 1 | Pregled ovog nacrta i potvrda vlasnika/semantike | Spremno za pregled; član 9 i vlasnici zavisnosti |
| 2 | Zajednička ACC-01/06 osnova bez kopiranja modela | Osvežena `fe6507f` osnova postoji samo u novoj detached probi; prethodno odobren KT1 dizajn je očuvan, ali novi commit ovim zadatkom nije odobren |
| 3 | Stvarni CourseStateReader, DI i fokusirani testovi | Roditelj ga već implementira i registruje; ne duplirati. PostgreSQL dokaz nije izvršen |
| 4 | Sesijski identitet i status naloga | Sesijski adapter aktiviran uz odobrene host/IDN izmene i testiran izolovanim HTTP tokom; autoritativni/trajni status naloga ostaje zavisnost IDN-03 |
| 5 | Članstva, delegacije i ACC-01 autorizacija | Blokirano dostupnošću/usaglašavanjem ACC-02/03/07 izvora, prava i Access politika |
| 6 | Zabrana upisa u arhiviran kurs kroz ACC-02 | Blokirano dostupnošću ACC-02 modula i dogovorom konkurentnosti; član 9 pravi adapter, ne enrollment |
| 7 | Primena provere na stvarnom sadržaju i upis publike | Blokirano dogovorom i povezivanjem ASN/LAB/AUT vlasničkih tokova |
| 8 | Centralni audit adapteri i garantovan zapis | Blokirano AUD katalogom, registracijama i transakcionim dogovorom; razgovor otvoriti već u koraku 1 |
| 9 | HTTP/PostgreSQL dokaz integracije i dokumentacija | Članovi 8 i 9 sa vlasnicima; posle dostupnosti konkretnih integracija |

Integracija se označava završenom tek kada su prihvaćeni ugovor i mapiranje, registrovan stvarni adapter, povezani stvarni potrošači i prošli odgovarajući pozitivni i negativni testovi. Fake, logger ili zelen izolovani test sami nisu završena međutimska integracija.

Početni milestone obuhvatio je samo dokumentaciju; kasniji sesijski milestone dodaje adapter, minimalnu host registraciju i uklanjanje dva Development izuzetka, bez promene šeme baze ili IDN validacione logike. Rezultate provera prijaviti odvojeno, bez predstavljanja njih kao dokaza završene celokupne integracije ili prolaska pune CI procedure.

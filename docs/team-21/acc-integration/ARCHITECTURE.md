# Team 21, deo 9 — arhitektura izolovanog KT1 kandidata

**Lokalni review-v2, bez promene originalne ili stare review grane.** Arhitektura prikazuje roditelja `fe6507f`, svih devet commitova do `00b3c47` i isti odobreni Access/IDN dizajn kao testirana detached proba. Novi IDN lockout pripada login toku. [Izveštaj kandidata](MERGE_CANDIDATE.md) razdvaja prenete dokaze od novih provera dokumentacije; [DFD](DFD.md) prikazuje redosled obrade. Merge commit nije napravljen.

~~~mermaid
flowchart TB
    client["Nepouzdan klijent: cookie, zaglavlja i ID resursa"]
    subgraph host["Zajednički Platform.Api / IDN / AUD / Access"]
        corr["AUD CorrelationMiddleware: serverski TraceIdentifier"]
        session["Team 21 marker: jedna SessionCheck validacija pre Access"]
        identity["IIdentityContext / SessionIdentityContext scoped"]
        actor["SessionCurrentActor scoped: samo validirana Active sesija"]
        subject["Team21AccessSubjectProvider scoped"]
        access["Roditeljski RequireAccess i ActiveAccountPolicy"]
        other["Ostale rute: sačuvan REV / originalni subject provider"]
    end
    subgraph team["Team 21: Core ugovori, API i Infrastructure"]
        endpoint["12 označenih ACC-01 / ACC-06 endpointa"]
        services["CourseService / GroupService / MaterialVisibilityService"]
        course["Stvarni scoped CourseStateReader / ICoursesDbContext"]
        readers["GroupMembershipReader / MaterialAccessRuleReader"]
        status["IAccountStatusReader: Development fake; ostalo deny-all"]
        membership["ICourseMembershipReader: Development fake; ostalo null"]
        permissions["ICourseAuthorization: Development demo; ostalo deny-all"]
        audit["LoggingAuditTrail / LoggingVisibilityAuditWriter"]
        db[("Odvojene courses i groups šeme")]
    end
    sessions[("Postojeći IDN session store; IDN validira")]
    logs[("Strukturisani application log")]
    pending["KT2: odobren IDN status, ACC članstvo/dozvole, sadržaj i centralni AUD"]
    client --> corr --> session
    session --> sessions
    session --> identity --> actor --> subject --> access
    status --> subject
    access -->|"Active i dozvoljena radnja"| endpoint --> services
    subject -->|"samo neoznačene rute"| other
    actor --> services
    services --> course --> db
    services --> readers --> db
    services --> status
    services --> membership
    services --> permissions
    services --> audit --> logs
    corr -->|"TraceIdentifier kroz Team 21 correlation context"| audit
    pending -.-> status
    pending -.-> membership
    pending -.-> permissions
    audit -.->|"budući centralni adapter"| pending
~~~

Pune veze predstavljaju postojeći kandidat, uključujući fake/deny-all zamene. Isprekidane veze su nedovršene integracije. Core ne zavisi od HTTP/EF; novi subject adapter i selektivni middleware su u host integracionom sloju, a marker u Team21.Acc.Api. Nema kružne projektne reference.

- Session middleware ostaje IDN implementacija. Klijentska zaglavlja ne proizvode identitet; provider ne parsira cookie niti sam validira sesiju.
- Posle routing-a i AUD korelacije samo serverski označene Team 21 rute dobijaju raniju session validaciju. Kasniji session middleware obrađuje samo ostale rute. REV/AUD registracije i pravila drugih modula su sačuvani.
- Keyed kopija postojećeg subject descriptor-a čuva njegov lifetime i disposal. Scoped dekorator lenjo razrešava Team 21 zavisnosti samo za naše rute; nijedan scoped kontekst nije ubrizgan u singleton Access politiku.
- `CourseStateReader` je roditeljska implementacija, ne novi Part 9 duplikat. HTTP fixture koristi kontrolisanu zamenu, pa nije dokaz stvarnog PostgreSQL čitanja.
- Production `DenyAllAccountStatusReader` daje Inactive i Access odbija validiranog korisnika sa 403. `NoCourseMembershipReader` i `DenyAllCourseAuthorization` dodatno blokiraju pristup. EF-backed IDN reader postoji, ali sinhronizacija sa status write putem nije odobrena; nije aktiviran kao Team 21 izvor.
- Materijalni endpointi daju reference, ne LAB/ASN sadržaj. Application log nije centralni garantovani AUD zapis. Odbijanje na Access sloju ne mora pozvati Team 21 audit writer.

Izvori: [host kompozicija](../../../backend/Platform.Api/Program.cs), [session middleware](../../../backend/Platform.Api/Middleware/SessionCheckMiddleware.cs), [sesijski adapter](../../../backend/Platform.Api/Modules/Team21/Integration/SessionCurrentActor.cs), [Team 21 DI](../../../backend/Team21.Acc.Api/Team21AccModule.cs), [ACC-06 DI](../../../backend/Team21.Acc.Api/Groups/GroupsModule.cs), [Core ugovori](../../../backend/Team21.Acc.Core/Groups/Visibility/Interfaces/), [Infrastructure servisi](../../../backend/Team21.Acc.Infrastructure/Groups/Services/). [KT1 dokazi i ograničenja](KT1_READINESS.md).

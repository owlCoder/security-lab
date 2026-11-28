# Team 21, deo 9 — tok izolovanog KT1 kandidata

Ovo je tok lokalnog `feature/acc-integration-review-v2`: `fe6507f` + `00b3c47` + odobreni Access/IDN patch, sa istim produkcionim/testnim izvorima kao testirana detached proba. Originalna i stara review grana ostaju neizmenjene; merge commit čeka posebno odobrenje. [Izveštaj](MERGE_CANDIDATE.md) razdvaja 158 testova stvarne grane, prethodnih 295 i dokaz 259 bezbednih slučajeva kandidata.

~~~mermaid
flowchart TB
    client["Nepouzdan klijent: zahtev, cookie, zaglavlja i ID"]
    routing["Routing: Team21SessionRequiredMetadata bira granu"]
    corr["AUD: serverski TraceIdentifier i scope"]
    session{"Postojeći IDN SessionCheck: prihvaćena sesija?"}
    actor{"IIdentityContext / SessionCurrentActor: authenticated, Active, neprazan ID?"}
    account["Scoped Team21AccessSubjectProvider čita status naloga"]
    access{"RequireAccess: Active subject i dozvoljena radnja?"}
    services["CourseService / GroupService / MaterialVisibilityService"]
    decision{"Servis: dozvole, vlasništvo, kurs i pravila grupa?"}
    courses[("ACC-01 courses: stvarni CourseStateReader")]
    groups[("ACC-06 groups: stvarni EF čitači")]
    external["Status / članstvo / course permissions: fake ili deny-all"]
    allowed["200 / 201 / 204: dozvoljen DTO, referenca ili promena"]
    denied["Servisni 404: tuđi ili skriveni resurs"]
    unauthorized["401: nema prihvaćene sesije / identiteta"]
    forbidden["403: non-Active / unknown nalog ili Access zabrana"]
    audit["Odabrani serverski Team 21 audit događaji"]
    logs[("ILogger: bez cookie-ja, tokena i sadržaja zahteva")]
    other["Ostale rute: originalni provider i Access pre kasnije session provere"]
    client --> routing --> corr
    corr -->|"označena Team 21 ruta"| session
    corr -->|"ostale rute"| other
    session -->|"ne"| unauthorized --> client
    session -->|"validirani rezultat u HttpContext.Items"| actor
    actor -->|"ne"| unauthorized
    actor -->|"da: samo serverski UserId"| account --> access
    access -->|"ne"| forbidden --> client
    access -->|"da"| services --> decision
    external --> account
    external --> decision
    courses --> decision
    groups --> decision
    decision -->|"da"| allowed --> client
    decision -->|"ne"| denied --> client
    allowed -->|"odabrane promene"| audit
    denied -->|"odabrana odbijanja"| audit
    corr -->|"Team 21 ICorrelationContext"| audit --> logs
~~~

Servisne operacije ne koriste sve iste izvore: grupni read proverava nastavnika i aktivno članstvo, upisi dodatno stanje kursa/grupe. Materijalni servis određuje kurs i publiku iz serverskog pravila. Nastavnik i reviewing assistant zadržavaju dozvoljen pristup arhiviranom kursu, dok student nema taj pristup. Filtrirana lista može biti prazna, bez otkrivanja skrivenih referenci. Nevažeći ulaz može dati 422, a konflikt/arhivirana promena 409.

401, Access 403 i servisni 404 imaju različite uzroke. Access odbija neaktivan nalog pre pristupa konkretnoj grupi/materijalu; njegov generički ProblemDetails ne sadrži zaštićene identifikatore ili nazive. Nepoznato stanje mapira se na Unknown; izuzetak reader-a ne daje Active fallback, nego ga postojeći Access catch odbija. Kvar kasnijeg servisnog čitanja nije unapred obećan kao 403/404.

Kandidat zadržava roditeljski `CourseStateReader`. Production status reader ostaje deny-all; odsutna članstva i course permissions su dodatne blokade. Uspešni testovi sa kontrolisanim čitačima zato ne dokazuju legitimni produkcioni pristup, autoritativni IDN status ili PostgreSQL.

Nije svaka zabrana auditovana Team 21 writer-om. `LoggingAuditTrail` ima Outcome za odabrane događaje; visibility writer ima samo odbijanja sa Reason. Access može vratiti svoj correlationId, dok Team 21/AUD logovi koriste serverski TraceIdentifier; nije dokazan jedinstven ID kroz sve module. Centralni AUD upis i transakciona garancija ostaju KT2.

Izvori: [IDN ugovor](../../contracts/IIdentityContext.md), [session middleware](../../../backend/Platform.Api/Middleware/SessionCheckMiddleware.cs), [adapter](../../../backend/Platform.Api/Modules/Team21/Integration/SessionCurrentActor.cs), [GroupService](../../../backend/Team21.Acc.Infrastructure/Groups/Services/GroupService.cs), [MaterialVisibilityService](../../../backend/Team21.Acc.Infrastructure/Groups/Services/MaterialVisibilityService.cs), [HTTP mapiranje](../../../backend/Team21.Acc.Api/Extensions/ResultHttpExtensions.cs), [HTTP testovi](../../../backend/Team21.Acc.Tests/Api/SessionCurrentActorHostIntegrationTests.cs). [Matrica odluka i pretnje](KT1_READINESS.md).

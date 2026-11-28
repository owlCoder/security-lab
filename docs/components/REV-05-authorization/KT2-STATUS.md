# KT2 — Luka: autorizacija, audit i preostale integracije

Pregledano 23.11.2026. Osnova: main `e2c97fe`, nakon PR-ova #197, #204, #212 i #214.
Ovaj dokument prati Lukin individualni dio, a ne cijeli timski REV-05 / IDN-05.
Ponovljivo pokretanje i granice dokaza: [KT2-DEMO](KT2-DEMO.md).

## Implementirano i spojeno

| Stavka | Implementacija i dokaz | Odgovornost |
| --- | --- | --- |
| Konkretne REV akcije i kurs | Registrovane politike, serverski identitet i eksplicitni grantovi bez fallback-a na ulogu kada je persistent izvor ukljucen | Luka; Access ugovor Tim 3 |
| Nema automatskog approve prava administratora/saradnika | RevisionPermissions i RevisionApprovalPolicy; samoodobravanje zavisi od politike i potrebnih prava | Luka; domensko pravilo Matija |
| Predaja, vracanje i odobrenje sa auditom | ReviewService i ITransactionalAuditWriter potvrde promjenu, operaciju i audit u jednoj transakciji; retry ne ponavlja dogadjaj | Markov API + Lukina integracija; Audit ugovor Tim 12 |
| Odbijeni pristup | Program registruje CentralAccessAuditWriter; jedan access.denied sa serverskim identitetom/korelacijom i alarm pri izgubljenom upisu | Tim 3/12 centralni tok; Luka koristi javni ugovor |
| Audit 429 za login | auth.login.throttled/Denied, Anonymous akter, bez naloga i detalja zahtjeva; PR #204 | Luka; IP limiter Nemanja |
| Audit prijave zakljucanog naloga | auth.login/Failure, reasonCode=account_locked; nalog je cilj, akter Anonymous; HTTP/PostgreSQL dokaz PR #212 | Milanov lockout + Lukina audit provjera |
| Pouzdani kurs laboratorije | StoredLaboratory.CourseId, AddLaboratoryCourse i provjera prije kreiranja; PR #214 | Marko uz LAB/persistence vlasnike; sigurnosni pregled Luka |

PR #192 je bio namijenjen strogoj ACC-05 grani; #197 je tu integraciju spojio u main.
Za zavrsene stavke ne ceka se ponovno spajanje drugih grana.

## Dorada ovog paketa

- README i DATA-FLOW prate centralni Access audit umjesto stare produkcione
  registracije RevisionAuditForwarder-a. Stari adapter nije ponovo ukljucen.
- Dodati su RevisionAuthoritativePermissionTests: administrator, nastavnik ili
  saradnik sa imenovanom delegacijom ne zaobilaze nedostajuci, opozvani, istekli
  ili drugi kursni grant. Nova odluka ponovo cita pravo. Koriste stvarni permission
  reader i politike uz EF InMemory; ovo nije dokaz PostgreSQL ponasanja.
- Dopunjeno je uputstvo za demonstraciju individualnog doprinosa.
- Produkcioni algoritam, Identity operacije, Audit katalog i migracije ovim
  paketom nisu mijenjani.

Lokalna ciljana provjera 23.11: 109 REV autorizacionih + 11 Identity audit producent +
38 rate limiting testova = 158 proslo, 0 palo, 0 preskocenih. Nakon pokretanja Dockera
kompletan scripts/audit/test-postgres.ps1 prosao je sa 2370 uspjesnih, 0 neuspjesnih
i 70 konfiguraciono preskocenih testova; svih 58 REV PostgreSQL i 441 Identity
test proslo je bez preskakanja. Ciljana grupa je dio punog zbira. Detalji su u KT2-DEMO.

## Stvarno otvorene zavisnosti

| Stavka | Sta postoji | Naredni korak |
| --- | --- | --- |
| Nastanak blokade naspram ponovljenog pokusaja | IsLockedOut i auth.login/account_locked opisuju odbijenu prijavu | Milan/Identity potvrdjuje atomski nastanak prelaza; Luka i Tim 12 dogovaraju mapiranje |
| Audit svakog prelaza pri padu writera ili prekidu zahtjeva | Blokada ostaje na snazi, zapis moze izostati; bez outbox-a | Identity + Tim 12 usaglasavaju transakciju ili ponovljivu isporuku i idempotentnost |
| Administratorsko otkljucavanje | account.unlock provjerava aktivnog administratora; nema operacije koja uklanja blokadu | Milan implementira Identity operaciju i ciljni nalog; Luka veze prava i audit prema potvrdjenom ugovoru |
| Stare laboratorije bez kursa | Podaci i NULL pripadnost su sacuvani, kreiranje revizije je odbijeno | Marko + Tim 13/LAB-03 + Tim 20 potvrde pouzdan izvor i kontrolisano dopunjavanje |

Audit katalog na ovoj osnovi nema poseban tip zakljucavanja/otkljucavanja.
account.status.changed opisuje Active/Blocked/Archived, a ne privremeni lockout;
ne koristi se kao zamjena. Allow za account.unlock nije izvrseno otkljucavanje
niti audit uspjeha. Trenutni IAdminDirectory koristi serversku konfiguraciju
Stub:AdminIds; to nije zaseban produkcioni registar administratora.

Prije Identity/audit dorade potrebno je dogovoriti:

1. Koja Identity operacija potvrdjuje prelaz i razlikuje retry od nove promjene.
2. Potvrdjenog aktera, ciljni nalog i serversku korelaciju. Nepotvrdjeni login je
   Anonymous; uspjesno administratorsko otkljucavanje ima potvrdenog aktera.
3. Tip/ishod/polja koje Audit katalog prihvata; bez emaila, lozinke, cookie-ja,
   tokena i slobodnog teksta.
4. Granicu transakcije, odgovor pri padu audita i zastitu od duplih/izgubljenih
   dogadjaja. Test provjerava i stanje naloga i trajni zapis.

To su zajednicke zavisnosti. UI, glavni lockout algoritam i reset lozinke pripadaju
njihovim vlasnicima; ovaj paket ih ne predstavlja kao zavrsene.

## Istorijski dokaz PR-a #212

Lokalno 19.11.2026. na osnovi b43cdd0: 18 Identity audit, 87 REV autorizacionih i
48 REV HTTP/PostgreSQL testova: ukupno 153 prosla, 0 pala, 0 preskocenih. To je
istorijski rezultat, ne broj testova danasnjeg main-a.

Login_LockoutThresholdAndLaterAttempt_ShouldPersistAnonymousFailureEvents provjerava
stvarni prag i kasnije odbijanje: Anonymous akter, nalog kao cilj, serverska
korelacija, isti genericki 401 i bez nove sesije.
AccountLockout_WhenAuditInsertFails_StaysLockedAndIssuesNoSession stvarno ukida
INSERT pravo ogranicenom Audit runtime nalogu u privremenoj bazi: blokada ostaje,
a audit uspjeh nije lazno potvrdjen. REVOKE/GRANT ne dira razvojnu bazu.

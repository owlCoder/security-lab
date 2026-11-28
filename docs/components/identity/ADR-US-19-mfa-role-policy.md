# ADR US-19: MFA obavezan za izabrane uloge

## Problem

Privilegovan nalog (administrator, nastavnik) ne sme da dobije potpunu (`Active`) sesiju bez drugog faktora.
Koje uloge moraju imati MFA određuje se konfiguracijom, ne izmenom koda. Trebalo je odlučiti i šta se dešava
kada korisnik dobije privilegovanu ulogu dok već ima aktivnu sesiju, kao i nekoliko graničnih slučajeva.

## Odluke

1. **Uloga dobijena usred sesije.** Postojeća `Active` sesija ostaje važeća do isteka (najviše 8 sati,
   `StoreSessionCreator.Lifetime`). Politika se primenjuje od sledeće prijave. Razlog: ne traži dodatni kod,
   a rizik je ograničen trajanjem sesije.
2. **Konfiguracija.** Lista je `Identity:Mfa:RequiredForRoles` (trenutno `Administrator`, `Teacher`).
   Imena se porede tačno, uključujući velika i mala slova. Nepoznato ime zaustavlja start API-ja
   (`MfaPolicyOptionsValidator`), pa greška u kucanju ne može tiho da isključi MFA. Lista se čita i
   proverava jednom, pri startu (`IOptions<MfaPolicyOptions>`), pa izmena traži restart API-ja.
3. **Prazna ili nepostojeća lista** znači da nijedna uloga ne mora MFA. Lista je zasad samo u
   `appsettings.Development.json`; za druga okruženja mora se zadati posebno.
4. **Prijava.** Sesija je `MfaPending` ako korisnik ima uključen MFA ili ako njegova uloga traži MFA.
   Odgovor prijave dobija `mfaEnrollmentRequired = true` kada uloga traži MFA, a korisnik ga nema.
5. **Obavezno uključivanje.** `MfaPending` sesija sme da uključi MFA preko
   `POST /api/auth/mfa/enroll/required` i `/required/confirm`, ali samo ako uloga traži MFA i korisnik
   ga još nema (`MfaService.MayEnrollAsync`). Postojeći MFA se ne može zameniti samo lozinkom.
   Postojeći `mfa/enroll` i dalje pušta samo `Active` sesiju.
6. **Posle uključivanja** sesija ostaje `MfaPending`; korisnik zatim unosi sledeći kod preko `mfa/verify`.
7. **`MfaPending` ne prolazi nigde drugde**: zaštićeni endpoint-i vraćaju `401`.

## Posljedice i dokaz

- Slabost odluke 1: do isteka sesije korisnik je privilegovan bez MFA. Kasnije poboljšanje: opoziv sesija
  pri promeni uloge.
- Slabost odluke 3: lista koja nedostaje isključuje zahtev bez upozorenja.
- OIDC prijava primenjuje politiku (`MfaPending` za ulogu), ali ne šalje `mfaEnrollmentRequired`, jer ne čita
  MFA stanje korisnika.
- Uključivanje sa samo lozinkom je rizik prvog uključivanja (ukradena lozinka nalogu bez MFA); ograničen je
  na uloge koje moraju imati MFA i na naloge koji ga još nemaju.

Testovi:
- `RoleMfaPolicyTests`: lista, prazna lista, pogrešna velika i mala slova
- `LoginTests`, `FederatedLoginServiceTests`: `MfaPending` ili `Active` prema ulozi, `MfaEnrollmentRequired`
- `PasswordAndCredentialTests.Verifier_ReturnsTheRoleOfTheAccount`
- `MfaServiceEnrollTests`, `MfaServiceConfirmTests`: uključivanje sa `MfaPending` sesijom i njegova ograničenja
- `MfaEnrollmentIntegrationTests.EnrollRequired_WithMfaPendingSessionOfTeacher_ReturnsTotpUri`
- `MfaPendingAccessIntegrationTests`: `MfaPending` dobija `401` na pet zaštićenih adresa
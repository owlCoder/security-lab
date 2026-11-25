# MFA preko TOTP authenticator aplikacije

**Komponenta:** Identity  
**Oblast:** IDN-06  
**Status:** Predlog za implementaciju  

## 1. Cilj

Omogućiti višefaktorsku autentikaciju korišćenjem TOTP-a (Time-based One-Time Password). Korisnik povezuje svoj nalog sa authenticator aplikacijom skeniranjem QR koda, a zatim pri prijavi unosi kratkotrajni šestocifreni kod.

## 2. Uklapanje u postojeći backend

Identity komponenta se nalazi u `backend/Platform.Identity`, dok API endpoint-i pripadaju `backend/Platform.Api`.

Postojeći kod već podržava neophodne nivoe sesije:

- `MfaPending` — lozinka je proverena, ali MFA još nije završen;
- `Active` — lozinka i MFA su uspešno provereni.

Relevantna postojeća dokumentacija:

- [IDN-02/IDN-06 DFD](./IDN-02_IDN-06_DFD.md)
- [Login](./IDN-02_LOGIN.md)
- [Logout](./IDN-02_LOGOUT.md)
- [IIdentityContext](../../contracts/IIdentityContext.md)

Trenutno [LoginService](../../../backend/Platform.Api/Services/Identity/LoginService.cs) uvek kreira `Active` sesiju. 
Treba da se uvede provera MFA politike i kreiranje `MfaPending` sesije kada je MFA uključen ili obavezan.

## 3. TOTP parametri

| Parametar | Predložena vrednost |
|-|-|
| Algoritam | HMAC-SHA-1 zbog kompatibilnosti sa authenticator aplikacijama |
| Dužina koda | 6 cifara |
| Vremenski interval | 30 sekundi |
| Tolerancija vremena | trenutni interval, uz najviše jedan susedni interval |
| Secret format | Base32 |
| Provisioning | `otpauth://` URI prikazan kroz lokalno generisan QR |

RFC 6238 dozvoljava HMAC-SHA-256 i HMAC-SHA-512, ali SHA-1 je najkompatibilniji sa uobičajenim authenticator aplikacijama. Server mora imati sinhronizovano vreme.

## 4. Enrollment

### 4.1 Pokretanje

```http
POST /api/auth/mfa/enroll
Cookie: SecurityLab.Session=<active-session>
```

Backend:

1. proverava da je korisnik autentifikovan;
2. generiše kriptografski slučajnu tajnu od najmanje 160 bita;
3. ne aktivira tajnu pre potvrde prvog koda;
4. formira `otpauth://` URI;
5. vraća URI samo jednom.

Primer:

```text
otpauth://totp/Security%20Lab:student%40example.com?secret=BASE32SECRET&issuer=Security%20Lab&algorithm=SHA1&digits=6&period=30
```

QR kod se generiše lokalno u frontend-u. Tajna se ne sme slati eksternom QR servisu.

### 4.2 Potvrda

```http
POST /api/auth/mfa/enroll/confirm
Content-Type: application/json
Cookie: SecurityLab.Session=<active-session>

{ "code": "123456" }
```

Enrollment uspeva samo ako je kod validan, enrollment nije istekao i MFA još nije aktivan. Nakon uspeha tajna se šifrovano čuva, postavlja se `MfaEnabled = true`, a privremeni enrollment podatak se uklanja.

Postojeća aktivna tajna ne sme biti prepisana običnim enrollment zahtevom.

## 5. Login i verifikacija

```text
validna lozinka + MFA isključen  -> Active sesija
validna lozinka + MFA uključen   -> MfaPending sesija
nevalidna lozinka                -> 401
```

Endpoint za završetak prijave:

```http
POST /api/auth/mfa/verify
Content-Type: application/json
Cookie: SecurityLab.Session=<mfa-pending-session>

{ "code": "123456" }
```

Backend proverava da je sesija `MfaPending`, učitava zaštićenu tajnu i proverava TOTP kod. Uspešan kod promoviše sesiju u `Active`. Pogrešan, istekao ili ponovljen kod ne menja sesiju i vraća generički odgovor:

```json
{ "code": "invalid_mfa" }
```

`MfaPending` sesija ne sme pristupiti zaštićenim endpoint-ima. Ovo mora važiti i kada korisnik ručno pozove endpoint, bez oslanjanja na frontend.

## 6. Podaci i zaštita tajne

Preporučeni podaci u `identity.accounts` ili izdvojenoj tabeli `identity.mfa_credentials`:

```text
mfa_enabled              boolean not null default false
totp_secret_ciphertext   text null
totp_secret_key_version  integer null
last_totp_time_step      bigint null
mfa_failed_attempts      integer not null default 0
mfa_locked_until         timestamp null
```

TOTP tajna mora biti šifrovana pre upisa u bazu. Ključ se čuva u environment promenljivoj, user-secrets ili secrets manager-u, nikada u repozitorijumu. Tajna, QR URI i kod se ne smeju upisivati u logove, audit događaje ili response nakon enrollment-a.

Pošto backend mora da koristi tajnu za verifikaciju, ona se ne hash-uje kao lozinka.

TOTP tajnu šifruje samo `ITotpSecretProtector` (`DataProtectionTotpSecretProtector`, purpose
`SecurityLab / Identity / MfaTotpSecret / v1`). Običan `IDataProtector` nije registrovan, pa drugi moduli prave
sopstveni protector iz `IDataProtectionProvider`, sa svojim purpose-om.

### Key ring (implementirano)

Gubitak Data Protection ključeva znači da se nijedna sačuvana TOTP tajna ne može dešifrovati, a bez recovery
kodova i admin reseta svi MFA nalozi ostaju zaključani. Zato `AddMfaKeyRing` (`Program.cs`) traži:

| Podešavanje | Značenje |
|-|-|
| `Identity:DataProtection:KeysPath` | direktorijum za ključeve; mora da preživi restart i da bude u backup-u zajedno sa bazom (ADM-03, OPS-04) |
| `Identity:DataProtection:CertificatePath` | PFX sa privatnim ključem; njime se šifruje svaki ključ upisan u `KeysPath` |
| `Identity:DataProtection:CertificatePassword` | lozinka PFX-a; samo kroz environment varijablu ili user-secrets, nikad u repou (ADM-04) |

- Van Development okruženja oba prva podešavanja su obavezna. API se ne pokreće u ovim slučajevima (svaki ima
  test u `MfaKeyRingTests`). Poruka greške ove provere ne sadrži lozinku ni putanju:

  | Uslov | Test |
  |-|-|
  | podešavanja nedostaju | `AddMfaKeyRing_OutsideDevelopmentWithoutSettings_Throws`, `ApiHost_InProductionWithoutKeyRing_DoesNotStart` |
  | podešeno je samo jedno od dva | `AddMfaKeyRing_OnlyOneSettingInDevelopment_Throws` |
  | PFX fajl ne postoji | `AddMfaKeyRing_MissingCertificateFile_Throws` |
  | lozinka nije dobra | `AddMfaKeyRing_WrongCertificatePassword_ThrowsWithoutRevealingIt` |
  | sertifikat nema privatni ključ | `AddMfaKeyRing_CertificateWithoutPrivateKey_Throws` |
  | `KeysPath` ne može da se napravi | `AddMfaKeyRing_KeysPathThatCannotBeCreated_Throws` |
  | `KeysPath` postoji, ali nije čitljiv | `AddMfaKeyRing_ExistingKeysPathWithoutReadAccess_Throws` |
  | `KeysPath` postoji, ali nije upisiv | `AddMfaKeyRing_ExistingKeysPathWithoutWriteAccess_Throws` |
  | postojeći ključ (`*.xml`) nije čitljiv, npr. posle restore-a sa drugim vlasnikom fajlova | `AddMfaKeyRing_ExistingKeyFileWithoutReadAccess_Throws` |

- Prava na `KeysPath` se proveravaju pri startu, a ne pri prvom enrollment-u. Data Protection čita key ring pri
  prvoj upotrebi i tada upisuje prvi ključ, a posle toga nov ključ otprilike svakih 90 dana. Zato direktorijum
  koji postoji, ali nije čitljiv ili upisiv za korisnika pod kojim radi API, zaustavlja start. Upis se proverava
  pravim upisom privremenog fajla `.write-probe-<guid>.tmp`, koji se odmah briše. Tako provera radi isto na
  Windows-u (ACL), Linux-u i macOS-u (prava pristupa, read-only mount). Data Protection čita samo `*.xml`, pa
  probni fajl nikad ne smatra ključem. Svaki postojeći ključ se pri startu samo otvara za čitanje; dešifruje ga
  Data Protection kasnije, sertifikatom.
- Data Protection sam loguje putanju fajla kad upisuje nov ključ (`FileSystemXmlRepository`, nivo Information).
  Putanja nije tajna i pomaže operativi, pa se taj log ne filtrira. Log sadrži samo putanju, ne sadržaj ključa
  ni lozinku PFX-a.
- U Development okruženju bez podešavanja koristi se podrazumevani lokalni key ring. Ako je podešeno samo jedno
  od dva, start pada i tada.
- Za restore na drugom hostu potrebni su i `KeysPath` i isti sertifikat. Kopija ključeva bez sertifikata ne
  otvara tajne (`MfaKeyRingTests.TotpSecretProtector_KeysWithoutTheCertificate_CannotReadTheSecret`).
- Test hostovi koji rade kao Production ili Testing uključuju test key ring preko `UseMfaTestKeyRing()`
  (`backend/TestShared/MfaTestKeyRing.cs`): direktorijum i self-signed PFX u temp folderu, jedan po test procesu.
  Folder se briše kad se proces završi, a folder koji je ostao posle prekinutog procesa briše sledeće pokretanje
  kad je stariji od jednog dana.
- Lokalno pokretanje van Development okruženja i pravljenje PFX-a: `docs/DEVELOPMENT.md`, odeljak
  "MFA key ring (IDN-06)".

### Zamena sertifikata (rotacija nije podržana)

Svaki ključ u `KeysPath` je šifrovan sertifikatom koji je bio podešen kad je ključ napravljen, a u XML-u ključa
stoji njegov thumbprint. `AddMfaKeyRing` registruje samo trenutni sertifikat i ne prosleđuje prethodne
sertifikate u `UnprotectKeysWithAnyCertificate`. Posledice:

- Ako se `CertificatePath` zameni drugim sertifikatom, postojeći ključevi ne mogu da se dešifruju. Svaka sačuvana
  TOTP tajna postaje nečitljiva i svi MFA nalozi ostaju zaključani, dok se ne vrati stari PFX. To važi i za
  obnovljen sertifikat sa istim ključnim parom, jer se thumbprint menja.
- Stari PFX zato mora da ostane dostupan dok god u `KeysPath` postoji ključ šifrovan njime. PFX ide u backup,
  ali ne na isto mesto kao `KeysPath` i baza: ko ima sva tri, može da pročita TOTP tajne.
- Ako sertifikat procuri, napadaču za TOTP tajne trebaju još kopija `KeysPath`-a i baze. Bez podrške za rotaciju
  jedini izlaz je novi sertifikat sa praznim `KeysPath`-om i ponovni enrollment svih MFA naloga, a admin reset
  MFA (US-32) još ne postoji.

Rotacija bi se dodala opcionim podešavanjem, na primer `Identity:DataProtection:PreviousCertificatePaths`.
Učitani prethodni sertifikati išli bi u `UnprotectKeysWithAnyCertificate`, a novi ključevi bi se šifrovali
samo trenutnim. To nije deo IDN-06.

## 7. Replay i rate limiting zaštita

Za svaki nalog ili MFA credential čuvati poslednji prihvaćeni TOTP time-step. Kod iz istog ili starijeg time-step-a odbiti, čime se sprečava ponovna upotreba istog koda.

Predloženo ograničenje:

```text
Najviše 5 neuspešnih MFA pokušaja po MfaPending sesiji.
Nakon toga sesiju opozvati i zahtevati novi login lozinkom.
```

Ograničenje treba kombinovati sa postojećim mehanizmom za attempt limiting iz IDN-05 kada bude dostupan.

## 8. Recovery kodovi

Preporučuje se generisanje 8–10 jednokratnih recovery kodova pri aktivaciji MFA:

- prikazuju se samo jednom;
- u bazi se čuvaju samo hash vrednosti;
- svaki kod može da se iskoristi jednom;
- ne šalju se automatskim e-mailom.

Ako recovery kodovi nisu obuhvaćeni U-12 implementacijom, treba ih eksplicitno izdvojiti u naredni story. Bez recovery mehanizma gubitak telefona može trajno zaključati nalog.

## 9. Acceptance kriterijumi

### Enrollment

- [ ] Validna sesija može pokrenuti TOTP enrollment.
- [ ] Tajna se generiše kriptografski slučajno.
- [ ] `otpauth://` URI se vraća samo tokom enrollment-a.
- [ ] QR se generiše lokalno, bez eksternog servisa.
- [ ] MFA se aktivira tek nakon uspešne potvrde prvog koda.
- [ ] Aktivna tajna ne može biti slučajno prepisana.

### Login

- [ ] Nalog bez MFA dobija `Active` sesiju.
- [ ] Nalog sa MFA dobija `MfaPending` sesiju.
- [ ] `MfaPending` nema pristup zaštićenim endpoint-ima.
- [ ] Validan TOTP promoviše sesiju u `Active`.
- [ ] Nevalidan TOTP ostavlja sesiju u `MfaPending`.
- [ ] Već iskorišćen time-step se odbija.

### Bezbednost

- [ ] TOTP tajna je šifrovana u bazi.
- [ ] Kod, tajna i QR URI nisu prisutni u logovima.
- [ ] Pogrešni pokušaji su ograničeni.
- [ ] Istekla ili opozvana `MfaPending` sesija ne može biti promovisana.
- [ ] Login i MFA odgovori imaju `Cache-Control: no-store`.

## 10. Testovi

Dodati testove za:

1. generisanje enrollment tajne i validnog `otpauth://` URI-ja;
2. neuspešan i uspešan enrollment confirmation;
3. zaštitu od prepisivanja postojeće tajne;
4. kreiranje `MfaPending` sesije pri loginu;
5. promovisanje sesije validnim TOTP kodom;
6. odbijanje pogrešnog i ponovljenog koda;
7. opoziv nakon previše pokušaja;
8. odbijanje `MfaPending` sesije na zaštićenom endpoint-u;
9. odsustvo tajne i koda iz audit događaja;
10. čuvanje samo šifrovane vrednosti tajne.

## 11. Odluka

Za U-12 koristi se TOTP preko QR enrollment-a, bez automatskog slanja e-mailova. Rešenje je kompatibilno sa postojećim `MfaPending`/`Active` modelom sesija i ne uvodi zavisnost od SMTP servisa.

TOTP nije phishing-resistant MFA. WebAuthn/passkeys mogu biti buduće poboljšanje za viši nivo zaštite.

## 12. Izvori

- [RFC 6238 — TOTP](https://datatracker.ietf.org/doc/html/rfc6238)
- [RFC 4226 — HOTP](https://datatracker.ietf.org/doc/html/rfc4226)
- [OWASP Multifactor Authentication Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Multifactor_Authentication_Cheat_Sheet.html)
- [NIST SP 800-63B](https://pages.nist.gov/800-63-4/sp800-63b.html)

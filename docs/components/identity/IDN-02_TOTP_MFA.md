# TOTP MFA enrollment

**Komponenta:** Identity
**Story:** US-17 — MFA uključivanje na backendu
**Status:** US-17 enrollment je implementiran; MFA login verifikacija je izdvojena u US-18.

## 1. Obuhvat

US-17 pokriva:

- pokretanje enrollment-a samo za postojeću, važeću `Active` sesiju;
- generisanje TOTP tajne;
- formiranje `otpauth://` URI-ja;
- čuvanje pending enrollment-a bez aktiviranja MFA;
- potvrdu enrollment-a kodom;
- odbijanje ponovnog enrollment-a kada je MFA već aktivan;
- odbijanje novog enrollment-a dok prethodni pending enrollment nije istekao;
- zaštitu tajne u bazi i `Cache-Control: no-store` odgovore;
- audit enrollment događaja bez tajne, URI-ja, koda ili ciphertext-a.

US-18 (Tim 1) implementira proveru MFA koda tokom prijave, `MfaPending` tok,
promociju sesije u `Active`, ograničavanje pokušaja i replay zaštitu tokom
login verifikacije.

## 2. TOTP parametri

| Parametar | Implementirana vrednost |
|-|-|
| Algoritam | HMAC-SHA-1 |
| Dužina koda | 6 cifara |
| Period | 30 sekundi |
| Tolerancija vremena | jedan prethodni i jedan naredni interval |
| Secret | 20 slučajnih bajtova, Base32 |
| Provisioning | `otpauth://` URI |

TOTP helper koristi `Otp.NET`. QR kod se ne generiše niti šalje sa backend-a;
frontend ga generiše lokalno iz URI-ja.

## 3. Pokretanje enrollment-a

```http
POST /api/auth/mfa/enroll
Cookie: SecurityLab.Session=<active-session>
```

Servis:

1. učitava sesiju preko `ISessionStore`;
2. prihvata samo neisteklu, neopozvanu sesiju nivoa `Active`;
3. odbija aktivan MFA;
4. učitava email korisnika;
5. generiše novu tajnu;
6. šifruje Base32 tajnu ASP.NET Core Data Protection-om;
7. čuva ciphertext, verziju ključa `1` i rok pending enrollment-a od 10 minuta;
8. vraća `MfaEnrollResponse` sa `otpauth://` URI-jem.

URI se vraća samo iz uspešnog enrollment odgovora. Ne čuva se ponovo u bazi
i ne piše se u audit.

Mogući API ishodi:

| Ishod | HTTP | Kod |
|-|-|-|
| uspeh | 200 | telo sadrži URI |
| MFA već aktivan | 409 | `mfa_already_enabled` |
| pending enrollment postoji | 409 | `mfa_enrollment_pending` |
| neautorizovana sesija | 401 | `invalid_mfa` |
| problem persistence-a | 503 | `mfa_unavailable` |

## 4. Potvrda enrollment-a

```http
POST /api/auth/mfa/enroll/confirm
Content-Type: application/json
Cookie: SecurityLab.Session=<active-session>

{ "code": "123456" }
```

Potvrda:

- zahteva važeću `Active` sesiju;
- zahteva postojeći i neistekli pending enrollment;
- dešifruje pending ciphertext;
- proverava šestocifreni TOTP kod;
- pri pogrešnom kodu ne menja MFA stanje;
- pri ispravnom kodu prebacuje pending ciphertext u aktivni ciphertext;
- postavlja `MfaEnabled = true`;
- postavlja poslednji potvrđeni time-step;
- briše pending ciphertext, verziju i rok.

Neuspešni odgovori ne otkrivaju da li je problem bio u tajni, sesiji ili kodu.

## 5. Čuvanje tajne

Entitet `identity.mfa_credentials` koristi:

```text
mfa_enabled
totp_secret_ciphertext
totp_secret_key_version
pending_totp_secret_ciphertext
pending_totp_secret_key_version
pending_enrollment_expires_at
last_totp_time_step
mfa_failed_attempts
mfa_locked_until
```

TOTP tajna se ne hash-uje jer je server mora dešifrovati. Data Protection
provider koristi stabilan application name i purpose:

```text
SecurityLab / Identity / MfaTotpSecret / v1
```

Development koristi lokalni key ring. Production mora koristiti persistentan
i zaštićen key ring; ephemeral ključevi nisu dovoljni za restart aplikacije.

## 6. Audit i poverljivi podaci

Enrollment i potvrda pišu audit preko postojećeg API `IAuditWriter` ugovora.
Audit sadrži korisnika, akciju, ishod, korelacioni ID i bezbedan generički
detail. Ne sme sadržati:

- Base32 tajnu;
- `otpauth://` URI;
- MFA kod;
- ciphertext;
- session token.

## 7. Granica prema US-18

`VerifyAsync` je namerno placeholder i trenutno vraća nedostupnost. US-18,
koji implementira Tim 1, treba da doda:

- login sa `MfaEnabled` nalogom koji kreira `MfaPending` sesiju;
- proveru koda samo za `MfaPending` sesiju;
- promociju sesije u `Active`;
- odbijanje pogrešnog, istekao i ponovljenog koda;
- ograničavanje pokušaja i opoziv/blokadu nakon praga;
- replay zaštitu preko `LastTotpTimeStep`.

## 8. Testovi

US-17 negativni testovi pokrivaju:

- enrollment bez tokena;
- enrollment iz `MfaPending` sesije;
- ponovno uključivanje aktivnog MFA;
- novi enrollment dok pending enrollment još važi;
- pogrešan kod;
- istekao enrollment;
- neispravan Data Protection ciphertext;
- odsustvo tajne u audit događajima.

Login verifikacioni testovi pripadaju US-18.

## 9. Budući rad

Recovery kodovi i administratorski MFA reset nisu deo US-17. TOTP nije
phishing-resistant MFA; WebAuthn/passkeys mogu biti buduće poboljšanje.

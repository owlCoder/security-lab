# TOTP MFA enrollment i verifikacija

**Komponenta:** Identity
**Story:** US-17 — MFA uključivanje na backendu; US-18 — MFA provera pri prijavi
**Status:** US-17 i US-18 su implementirani u istom PR-u (#196). Obavezno uključivanje MFA (US-19) nije u obuhvatu.

## 1. Obuhvat

US-17 pokriva:

- pokretanje enrollment-a samo za postojeću, važeću `Active` sesiju;
- generisanje TOTP tajne;
- formiranje `otpauth://` URI-ja;
- čuvanje pending enrollment-a bez aktiviranja MFA;
- potvrdu enrollment-a kodom;
- odbijanje ponovnog enrollment-a kada je MFA već aktivan;
- odbijanje novog enrollment-a dok prethodni pending enrollment nije istekao (jedan `INSERT ... ON CONFLICT`, pa i
  pod paralelnim zahtevima);
- zaštitu tajne u bazi i `Cache-Control: no-store` odgovore;
- audit enrollment događaja bez tajne, URI-ja, koda ili ciphertext-a.

US-18 pokriva proveru MFA koda tokom prijave (`MfaPending` tok), promociju sesije u
`Active`, ograničavanje pokušaja (lockout) i replay zaštitu (odeljak 7).

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

Uključivanje je jedan uslovni `UPDATE` (`mfa_enabled = false`, isti pending ciphertext, neistekao rok) u
transakciji sa audit zapisom `auth.mfa.enabled`. Od dve paralelne potvrde prolazi samo jedna. Ako audit
zapis nije upisan, transakcija se ne commit-uje: MFA ostaje isključen, a odgovor je `503 mfa_unavailable`.

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

TOTP tajna se ne hash-uje jer je server mora dešifrovati. Šifruje je samo
`ITotpSecretProtector` (`DataProtectionTotpSecretProtector`), sa stabilnim application name-om i purpose-om:

```text
SecurityLab / Identity / MfaTotpSecret / v1
```

U kontejneru nije registrovan običan `IDataProtector`, pa nijedan drugi modul ne može slučajno da dobije MFA
purpose. Drugi moduli prave sopstveni protector iz `IDataProtectionProvider`, sa svojim purpose-om.

### Key ring

Gubitak Data Protection ključeva znači da se nijedna sačuvana TOTP tajna ne može dešifrovati, a bez recovery
kodova i admin reseta svi MFA nalozi ostaju zaključani. Zato `AddMfaKeyRing` (`Program.cs`) traži:

| Podešavanje | Značenje |
|-|-|
| `Identity:DataProtection:KeysPath` | direktorijum za ključeve; mora da preživi restart i da bude u backup-u zajedno sa bazom (ADM-03, OPS-04) |
| `Identity:DataProtection:CertificatePath` | PFX sa privatnim ključem; njime se šifruje svaki ključ upisan u `KeysPath` |
| `Identity:DataProtection:CertificatePassword` | lozinka PFX-a; samo kroz environment varijablu ili user-secrets, nikad u repou (ADM-04) |

- Van Development okruženja oba prva podešavanja su obavezna. Ako nedostaju, ako fajl ne postoji, ako lozinka
  nije dobra ili sertifikat nema privatni ključ, API se ne pokreće (poruka ne sadrži lozinku).
- U Development okruženju bez podešavanja koristi se podrazumevani lokalni key ring. Ako je podešeno samo jedno
  od dva, start pada i tada.
- Za restore na drugom hostu potrebni su i `KeysPath` i isti sertifikat. Kopija ključeva bez sertifikata ne
  otvara tajne (`MfaKeyRingTests.WithoutTheCertificate_TheStoredKeysCannotBeUsed`).
- Test hostovi koji rade kao Production ili Testing uključuju test key ring preko `UseMfaTestKeyRing()`
  (`backend/TestShared/MfaTestKeyRing.cs`): direktorijum i self-signed PFX u temp folderu.

## 6. Audit i poverljivi podaci

MFA audit ide kroz Audit modul (AUD-01), preko `IdentityAuditProducer`, jedine granice mapiranja
Identity ishoda u Audit katalog:

| Događaj | Ishodi | Kako se upisuje |
|-|-|-|
| `auth.mfa.enrollment.started` | Success | `ITrustedAuditWriter`, posle čuvanja pending enrollment-a (ništa još nije uključeno) |
| `auth.mfa.enabled` | Success | `ITransactionalAuditWriter`, u istoj transakciji kao uključivanje |
| `auth.mfa.enabled` | Failure (`already_enabled`, `no_valid_enrollment`, `invalid_enrollment`, `invalid_code`) | `ITrustedAuditWriter` |
| `auth.mfa.verify` | Success | `ITransactionalAuditWriter`, u istoj transakciji kao consume koraka i promocija sesije |
| `auth.mfa.verify` | Failure (`invalid_code`, `replayed`, `locked`, `invalid_secret`, `session_inactive`) | `ITrustedAuditWriter`, sa `CancellationToken.None` |

Uspeh bez audit zapisa ne postoji: ako zapis nije `Recorded`, nema commit-a i odgovor je `503`.
Odbijanje ostaje odbijanje i kada audit ne uspe (pravilo 2 iz `docs/components/audit/CONTRACTS.md`).
Zapis sadrži akter (`User`), nalog kao resurs, ishod, korelacioni ID i najviše `reasonCode`. Ne sme sadržati:

- Base32 tajnu;
- `otpauth://` URI;
- MFA kod;
- ciphertext;
- session token.

## 7. Verifikacija pri prijavi (US-18)

```http
POST /api/auth/mfa/verify
Content-Type: application/json
Cookie: SecurityLab.Session=<mfa-pending-session>

{ "code": "123456" }
```

- Login naloga sa uključenim MFA kreira `MfaPending` sesiju; kod se proverava samo za nju.
- **Lockout:** dok je `mfa_locked_until` u budućnosti, kod se ne proverava (ni ispravan), pending sesija se
  opoziva, a odgovor je isti `401 invalid_mfa` kao za pogrešan kod.
- **Pogrešan kod:** jedan `UPDATE` povećava `mfa_failed_attempts`, a na pragu
  (`IdentityLockout:MaxFailedAccessAttempts`) postavlja `mfa_locked_until` na
  `IdentityLockout:LockoutMinutes`. Zaključan nalog se ne broji; posle isteka lockout-a brojač kreće od 1.
  Na pragu se pending sesija opoziva.
- **Replay:** korak koji nije noviji od `last_totp_time_step` se odbija (isti i stariji).
- **Uspeh:** jedna transakcija: `UPDATE` koji upisuje korak samo ako je noviji i nalog nije zaključan
  (i briše brojač i lockout), `UPDATE` sesije `MfaPending -> Active` (neopozvana, neistekla) i audit zapis.
  Paralelni zahtev sa istim kodom čeka zaključan red, ponovo proverava `WHERE` i ne menja ništa.

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

US-18 i audit testovi (`MfaServiceTests`, `MfaIntegrationTests` na PostgreSQL-u) pokrivaju i:

- isti kod iz dve paralelne `MfaPending` sesije (tačno jedan uspeh) i stariji kod posle novijeg;
- dve paralelne potvrde enrollment-a (jedan uspeh);
- prag pogrešnih kodova, pa nova prijava lozinkom i ispravan kod (`401`), i uspeh posle `LockoutMinutes`;
- paralelne pogrešne kodove (tačan brojač, lockout tačno na pragu);
- nedostupan audit pri potvrdi i verifikaciji (`503`, ništa nije promenjeno);
- zapise `auth.mfa.enabled` i `auth.mfa.verify` u `audit` šemi bez koda, tajne i e-maila;
- potvrdu bez enrollment-a (`401`) i dva paralelna enroll-a (jedan `200`, jedan `409`, nijedan `500`);
- key ring (`MfaKeyRingTests`): nova instanca sa istim ključevima dešifruje sačuvanu tajnu, ključevi na disku su
  šifrovani sertifikatom, a start van Development okruženja pada bez podešavanja.

## 9. Budući rad

Recovery kodovi i administratorski MFA reset nisu deo US-17. TOTP nije
phishing-resistant MFA; WebAuthn/passkeys mogu biti buduće poboljšanje.

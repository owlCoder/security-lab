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
7. u jednoj transakciji ponovo proverava sesiju i nalog pod lock-om (videti 4.1), pa čuva ciphertext, verziju
   ključa `1` i rok pending enrollment-a od 10 minuta;
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

### 4.1 Sesija i nalog u trenutku upisa (IDN-03, IDN-07)

Servis čita sesiju i nalog bez lock-a na početku zahteva. Blokiranje naloga ili opoziv sesije posle tog čitanja
ne sme da dozvoli upis. Zato enrollment, potvrda i verifikacija (poglavlje 7) prvo u svojoj transakciji pozivaju
`IMfaUnitOfWork.TryLockActiveSessionAsync`:

1. `SELECT ... FROM identity.accounts ... FOR SHARE`: nalog mora biti `Active`;
2. `SELECT ... FROM identity.sessions ... FOR NO KEY UPDATE`: sesija pripada nalogu, ima očekivani nivo
   (`Active` za enrollment i potvrdu, `MfaPending` za verifikaciju) i nije opozvana ni istekla.

Blokiranje (`ChangeAccountStatus`, `FOR UPDATE` naloga pa opoziv sesija) ili opoziv (IDN-07) koji je već u toku
čeka se, pa se vidi novo stanje. Onaj koji počne kasnije čeka commit. Redosled je uvek nalog pa sesija, isti kao
kod blokiranja, pa ne nastaje deadlock. Sesija se zaključava sa `FOR NO KEY UPDATE`, a ne sa `FOR SHARE`, jer je
verifikacija kasnije menja u istoj transakciji. Dva zahteva koja drže `FOR SHARE` čekala bi jedan drugog.

Ako provera ne prođe, ništa se ne upisuje i odgovor je `401 invalid_mfa`:
- enrollment ne čuva pending tajnu i ne piše `auth.mfa.enrollment.started`;
- potvrda ne uključuje MFA i piše odbijanje `auth.mfa.enabled` sa `reasonCode = session_inactive`;
- verifikacija ne troši korak i ne promoviše sesiju (`auth.mfa.verified`, `session_inactive`).

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

### Ključevi

Data Protection ključevi se čuvaju u direktorijumu iz `Identity:DataProtection:KeysPath` (`IdentityModule`):

- ključevi preživljavaju restart, pa sačuvane tajne ostaju čitljive;
- direktorijum je van repozitorijuma i ide u backup zajedno sa bazom (ADM-03, OPS-04). Restore baze bez njega
  ostavlja MFA naloge bez pristupa, jer se nijedna tajna ne može dešifrovati;
- bez `KeysPath` koristi se podrazumevano mesto frameworka: na Linuxu `~/.aspnet/DataProtection-Keys`, a u
  kontejneru bez upisivog home direktorijuma samo memorija, pa se tajne posle restarta ne mogu dešifrovati.

Kompromis ovog stanja:

- ključevi u `KeysPath` **nisu šifrovani** na disku. Dump baze sam ne otkriva tajne, ali ih otkriva neko ko ima
  i bazu i taj direktorijum;
- start ne proverava da li je `KeysPath` podešen, ni u `Production` okruženju.

Šifrovanje ključeva, provera pri startu i test da logovi ne sadrže tajnu ni kod su obuhvat US-22.

Testovi: `IdentityModuleTests.TotpSecretProtector_WithKeysPath_NewInstanceReadsTheStoredSecret` (nova instanca
sa istim direktorijumom čita sačuvanu tajnu) i `TotpSecretProtector_WithAnotherKeysPath_CannotReadTheStoredSecret`.

## 6. Audit i poverljivi podaci

MFA audit ide kroz Audit modul (AUD-01), preko `IdentityAuditProducer`, jedine granice mapiranja
Identity ishoda u Audit katalog:

| Događaj | Ishodi | Kako se upisuje |
|-|-|-|
| `auth.mfa.enrollment.started` | Success | `ITrustedAuditWriter`, posle čuvanja pending enrollment-a (ništa još nije uključeno) |
| `auth.mfa.enabled` | Success | `ITransactionalAuditWriter`, u istoj transakciji kao uključivanje |
| `auth.mfa.enabled` | Failure (`already_enabled`, `no_valid_enrollment`, `invalid_enrollment`, `invalid_code`, `session_inactive`) | `ITrustedAuditWriter` |
| `auth.mfa.verified` | Success | `ITransactionalAuditWriter`, u istoj transakciji kao consume koraka i promocija sesije |
| `auth.mfa.verified` | Failure (`invalid_code`, `replayed_code`, `invalid_secret`, `session_inactive`) | `ITrustedAuditWriter`, sa `CancellationToken.None` |
| `auth.mfa.verified` | Denied (`attempt_limit`): kod koji dostigne prag ili pokušaj dok traje lockout | `ITrustedAuditWriter`, sa `CancellationToken.None` |

MFA tok piše `reasonCode` uz svako odbijanje, samo sa vrednostima iz liste u `CONTRACTS.md` (producer metode).
Pogrešan ili ponovljen kod je `Failure`, kao pogrešna lozinka u `auth.login`; ograničenje pokušaja je `Denied`,
kao `auth.login.throttled`. Za korisnika je odgovor u oba slučaja isti `401 invalid_mfa`.

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
- Ruta nema `[AllowAnonymous]`: `SessionCheckMiddleware` odbija zahtev bez cookie-ja ili sa opozvanom ili
  isteklom sesijom (`401 session_invalid`) pre handler-a. Access politika `MfaVerificationPolicy` samo propušta akciju.
- **Lockout:** dok je `mfa_locked_until` u budućnosti, kod se ne proverava (ni ispravan), pending sesija se
  opoziva, a odgovor je isti `401 invalid_mfa` kao za pogrešan kod.
- **Pogrešan kod:** jedan `UPDATE` povećava `mfa_failed_attempts`, a na pragu
  (`Identity:Mfa:Lockout:MaxFailedAttempts`) postavlja `mfa_locked_until` na
  `Identity:Mfa:Lockout:LockoutMinutes`. Zaključan nalog se ne broji; posle isteka lockout-a brojač kreće od 1.
  Na pragu se pending sesija opoziva.
- **Podešavanja lockout-a:** `Identity:Mfa:Lockout` (`MfaLockoutOptions`) je odvojeno od lockout-a lozinke
  (`Identity:Lockout`, IDN-05), pa promena IDN-05 podešavanja ne menja MFA. Podrazumevano je 5 kodova i 15 minuta.
  Granice su strože nego za lozinku: 1 do 10 kodova i 15 do 1440 minuta. Šestocifreni kod sa prozorom od jednog
  koraka ima 3 važeće vrednosti, pa granice dozvoljavaju najviše oko 960 pokušaja dnevno (oko 0,3% verovatnoće
  pogotka dnevno po nalogu), a podrazumevane vrednosti oko 480 (oko 0,14%). Vrednost van granica zaustavlja start
  (`MfaLockoutOptionsTests`).
- **Lockout lozinke (IDN-05) tokom MFA koraka:** `mfa/verify` ne proverava `lockout_end` naloga. `MfaPending`
  sesija znači da je lozinka već bila tačna pre lockout-a, a lockout lozinke ne gasi ni `Active` sesije, nego
  sprečava nove pokušaje lozinkom. Pogađanje koda ograničava MFA lockout.
- **Replay:** korak koji nije noviji od `last_totp_time_step` se odbija (isti i stariji).
- **Uspeh:** jedna transakcija: provera sesije i naloga pod lock-om (4.1), `UPDATE` koji upisuje korak samo ako je noviji i nalog nije zaključan
  (i briše brojač i lockout), opoziv `MfaPending` sesije (neopozvane, neistekle), nova `Active` sesija sa novim
  tokenom i handle-om (`StoreSessionCreator.Lifetime`) i audit zapis. Odgovor postavlja cookie nove sesije, pa
  cookie iz vremena pre drugog faktora posle toga dobija `401` (IDN-11, zaštita od session fixation).
  CSRF token je vezan za sesiju (US-15, `CsrfSessionBinding`), pa odgovor izdaje i novi token za novu sesiju,
  kao login. Token `MfaPending` sesije posle toga dobija `403 csrf_invalid`
  (`Verification_ValidCode_IssuesACsrfTokenForTheNewSession_AndTheOldTokenIsRejected`).
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

US-18 i audit testovi (unit testovi u `backend/Platform.Identity.Tests/Services/Mfa/` i PostgreSQL testovi u `backend/Platform.Identity.Tests/Integration/Mfa/`) pokrivaju i:

- nova `Active` sesija posle uspeha, uz opozvanu pending sesiju i odbijen stari cookie
  (`Verification_ValidCode_*`);
- isti kod iz dve paralelne `MfaPending` sesije (tačno jedan uspeh) i stariji kod posle novijeg;
- dve paralelne potvrde enrollment-a (jedan uspeh);
- blokiran nalog ili opozvana sesija između čitanja i upisa, za enrollment i potvrdu (`401`, ništa nije upisano;
  `*_AccountBlockedBeforeTheWrite_*` čeka otvoreno blokiranje na lock-u, `*_SessionRevokedBeforeTheWrite_*`);
- prag pogrešnih kodova, pa nova prijava lozinkom i ispravan kod (`401`), i uspeh posle `LockoutMinutes`;
- paralelne pogrešne kodove (tačan brojač, lockout tačno na pragu);
- nedostupan audit pri potvrdi i verifikaciji (`503`, ništa nije promenjeno);
- zapise `auth.mfa.enabled` i `auth.mfa.verified` u `audit` šemi bez koda, tajne i e-maila;
- svaki razlog koji servis piše prolazi pravi `AuditDraftValidator` (`MfaRefusals_WithEveryReasonTheServiceWrites_PassTheAuditContract`);
- potvrdu bez enrollment-a (`401`) i dva paralelna enroll-a (jedan `200`, jedan `409`, nijedan `500`);
- ključeve u `KeysPath` (`IdentityModuleTests`): nova instanca sa istim direktorijumom dešifruje sačuvanu tajnu,
  a ključevi iz drugog direktorijuma ne.

## 9. Budući rad

Recovery kodovi i administratorski MFA reset nisu deo US-17. TOTP nije
phishing-resistant MFA; WebAuthn/passkeys mogu biti buduće poboljšanje.

HTTP rate limit (SEC-04) za `POST /api/auth/mfa/verify` i `POST /api/auth/mfa/enroll/confirm` nije uključen.
Pokušaje danas ograničava MFA lockout po nalogu (odeljak 7), a zahtev bez važeće sesije odbija
`SessionCheckMiddleware` pre handler-a. Po IP adresi (više naloga, opterećenje baze) zaštite nema. Limiter i
njegove politike pripadaju IDN-05 (`Platform.Api/Modules/Identity/RateLimiting`). Postojeća politika
`identity-login` ne odgovara ovim rutama: delila bi brojač sa prijavom, a njen `429` upisuje
`auth.login.throttled`.

Administratorsko otključavanje (IDN-05, `POST /api/admin/accounts/{id}/unlock`) briše samo lockout lozinke
(`lockout_end` i brojač u `identity.accounts`). `mfa_failed_attempts` i `mfa_locked_until` ostaju, pa nalog
zaključan samo na MFA koraku dobija `409 account_not_locked` i čeka istek `Identity:Mfa:Lockout:LockoutMinutes`.
Skidanje MFA lockout-a pri otključavanju i test u `AccountUnlockIntegrationTests` pripadaju IDN-05.

Za `mfa/enroll` i `mfa/enroll/confirm` dovoljna je `Active` sesija; lozinka se ne traži ponovo (step-up).
Ko ima ukraden session cookie može da uključi MFA sa svojim authenticator-om, a vlasnik pri sledećoj prijavi
ne prolazi drugi faktor. Izlaz iz tog stanja su recovery kodovi (US-31) i administratorski MFA reset (US-32),
koji nisu deo US-17. Step-up sa lozinkom, uz brojanje pogrešne lozinke u lockout (IDN-05) i novi razlog u
Audit katalogu, opisan je u #252.

Rok `MfaPending` sesije nije deo US-18: ovde je isti kao za `Active` sesiju (`StoreSessionCreator.Lifetime`).
Poseban, kraći rok za `MfaPending` je kriterijum US-14 (#224).

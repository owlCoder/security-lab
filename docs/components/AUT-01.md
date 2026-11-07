# AUT-01: Uređivanje i čuvanje laboratorijskih zadataka

## 1. Pregled modula
Modul **AUT-01** obuhvata funkcionalnosti vezane za kreiranje, izmenu i ažuriranje laboratorijskih zadataka u okviru platforme. Centralni deo ovog modula čini komponenta `EditTaskForm.tsx` koja korisniku omogućava modifikaciju postojećih zadataka uz odgovarajuću validaciju i komunikaciju sa backend servisima.

## 2. Arhitektura i komponente
* **Frontend komponenta:** `EditTaskForm.tsx` (zadužena za renderovanje forme, prikupljanje unosa od korisnika i slanje API zahteva za izmenu).
* **API / Servisi:** Komunikacija sa serverom kroz definisane endpointe za upravljanje zadacima.

## 3. DFD dijagram (Data Flow Diagram)
Prikaz toka podataka za modul AUT-01:

```mermaid
graph TD
    A[Korisnik / Administrator] -->|Unos izmena u EditTaskForm| B(AUT-01: EditTaskForm.tsx)
    B -->|Validacija i slanje API zahteva| C[Backend / Server]
    C -->|Ažuriranje i čuvanje| D[(Baza podataka)]
    D -->|Potvrda uspešne izmene| C
    C -->|Odgovor / Poruka o statusu| B
const SQL = '11111111-1111-1111-1111-111111111111'
const LOGS = '22222222-2222-2222-2222-222222222222'
const CONFIG = '33333333-3333-3333-3333-333333333333'
const INCIDENT = '44444444-4444-4444-4444-444444444444'
const MFA = '55555555-5555-5555-5555-555555555555'
const HIDDEN = '99999999-9999-9999-9999-999999999999'

export const FAKE_CATALOG: unknown[] = [
  {
    id: SQL,
    title: 'Kontrola pristupa nad tuđim resursima',
    status: 'Published',
    objective: 'Prepoznati kada korisnik pokušava da pročita tuđi resurs zamenom ID vrednosti.',
    type: 'AccessControl',
    difficulty: 'Easy',
    tags: ['rbac', 'idor'],
    access: { isUnlocked: true, missingPrerequisites: [] },
  },
  {
    id: LOGS,
    title: 'Analiza dnevnika prijava',
    status: 'Published',
    objective: 'Izdvojiti povezane bezbednosne događaje iz dnevnika i objasniti njihov značaj.',
    type: 'LogAnalysis',
    difficulty: 'Medium',
    tags: ['logovi', 'detekcija'],
    access: { isUnlocked: true, missingPrerequisites: [] },
  },
  {
    id: CONFIG,
    title: 'Bezbedna konfiguracija veb servisa',
    status: 'Published',
    objective: 'Pronaći nebezbedna podešavanja simuliranog servisa i predložiti izmene.',
    type: 'SecurityConfiguration',
    difficulty: 'Medium',
    tags: ['konfiguracija'],
    access: { isUnlocked: false, missingPrerequisites: [SQL] },
  },
  {
    id: INCIDENT,
    title: 'Odgovor na incident',
    status: 'Published',
    objective: 'Odrediti prioritet događaja i izabrati sledeći korak reagovanja.',
    type: 'IncidentResponse',
    difficulty: 'Hard',
    tags: ['incident', 'detekcija'],
    access: { isUnlocked: false, missingPrerequisites: [LOGS, HIDDEN] },
  },
  {
    id: MFA,
    title: 'MFA i upravljanje sesijama',
    status: 'Published',
    objective: 'Odrediti dozvoljene prelaze sesije i opozvati nevažeće sesije.',
    type: 'MfaAndSessions',
    difficulty: 'Hard',
    tags: ['mfa', 'sesije'],
  },
]
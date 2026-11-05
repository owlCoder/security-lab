// LAB-07 frontend: svi pozivi ka serveru za laboratorije su na ovom mestu.
// Komponente nikad ne zovu fetch direktno, pa se pri povezivanju sa pravim
// endpointima menja samo ovaj fajl.
//
// Frontend nije bezbednosna granica: server proverava prava i validira unos.
// Ovde se odgovori servera samo prevode u poruke za korisnika.

import { fakeLabContentResponse } from './fakeLabContent'

/** Putanja iz LAB-01 kostura u main grani. Timski plan pominje /api/labs; ako se to usvoji, menja se samo ova linija. */
export const LABS_BASE_PATH = '/api/laboratories'

/**
 * URAĐENO: kreiranje ide na pravi server.
 * PLANIRANO: čitanje po jeziku ide na fake dok LAB-07 backend nema HTTP endpoint u Platform.Api.
 * Fake radi samo u razvojnom režimu (npm run dev) i ne ulazi u produkcioni build,
 * isto kao fake identitet na serveru. Kad endpoint postoji, ovde se stavi false.
 */
export const USE_FAKE_LAB_CONTENT = import.meta.env.DEV

export const MAX_TITLE_LENGTH = 200

export type Language = 'sr' | 'en'

export const LANGUAGES: { code: Language; label: string }[] = [
  { code: 'sr', label: 'Srpski' },
  { code: 'en', label: 'English' },
]

/**
 * Test korisnik za razvojni režim (fake iz LAB-01 kostura, zaglavlje X-Test-User).
 * Zameniće se pravom prijavom kad se poveže IDN modul.
 */
export type TestUser = { id: string; role: 'Student' | 'Teacher' } | null

export const TEST_USER_HEADER = 'X-Test-User'

export type ApiFailure =
  | { kind: 'invalid'; messages: string[]; requestId?: string }
  | { kind: 'unauthenticated'; requestId?: string }
  | { kind: 'forbidden'; requestId?: string }
  | { kind: 'notFound'; requestId?: string }
  | { kind: 'conflict'; requestId?: string }
  | { kind: 'notImplemented'; requestId?: string }
  | { kind: 'error' }

export type CreateLabOutcome = { kind: 'created'; id: string } | ApiFailure

/** Isti oblik kao LabContentView iz Platform.LabLocalization. */
export interface LabContent {
  revisionId: string
  evaluatorVersion: string
  language: Language
  title: string
  description: string
  instructions: string
}

export type LabContentOutcome =
  | { kind: 'ok'; content: LabContent; usedFallback: boolean }
  | ApiFailure

// Poznati kodovi grešaka sa servera prevode se u naše poruke.
// Proizvoljan tekst sa servera se ne prikazuje, da poruka ne bi otkrila interne detalje.
const KNOWN_ERRORS: Record<string, string> = {
  INVALID_TITLE: `Naslov je obavezan i može imati najviše ${MAX_TITLE_LENGTH} znakova.`,
  INVALID_LANGUAGE: 'Jezik nije podržan. Dozvoljeni su srpski (sr) i engleski (en).',
  INVALID_DESCRIPTION: 'Opis nije ispravan.',
  INVALID_INSTRUCTIONS: 'Uputstvo nije ispravno.',
  title: `Naslov je obavezan i može imati najviše ${MAX_TITLE_LENGTH} znakova.`,
  lang: 'Jezik nije podržan. Dozvoljeni su srpski (sr) i engleski (en).',
  language: 'Jezik nije podržan. Dozvoljeni su srpski (sr) i engleski (en).',
}

const GENERIC_INVALID = 'Unos nije ispravan.'

export function isLanguage(value: unknown): value is Language {
  return value === 'sr' || value === 'en'
}

function headersFor(user: TestUser, withJsonBody: boolean): Record<string, string> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (withJsonBody) headers['Content-Type'] = 'application/json'
  // Fake identitet se šalje samo u razvojnom režimu. Server ga van Development režima ionako odbija.
  if (import.meta.env.DEV && user) headers[TEST_USER_HEADER] = `${user.id}:${user.role}`
  return headers
}

async function readJson(res: Response): Promise<Record<string, unknown> | null> {
  try {
    const body: unknown = await res.json()
    return body !== null && typeof body === 'object' ? (body as Record<string, unknown>) : null
  } catch {
    return null
  }
}

function requestIdFrom(body: Record<string, unknown> | null): string | undefined {
  const value = body?.traceId ?? body?.correlationId
  return typeof value === 'string' && value.length <= 128 ? value : undefined
}

function invalidMessages(body: Record<string, unknown> | null): string[] {
  const keys: string[] = []
  if (typeof body?.code === 'string') keys.push(body.code)
  const errors = body?.errors
  if (errors && typeof errors === 'object' && !Array.isArray(errors)) keys.push(...Object.keys(errors))
  if (Array.isArray(errors)) keys.push(...errors.filter((e): e is string => typeof e === 'string'))

  const messages = [...new Set(keys.map(k => KNOWN_ERRORS[k] ?? KNOWN_ERRORS[k.toLowerCase()]).filter(Boolean))]
  return messages.length > 0 ? messages : [GENERIC_INVALID]
}

/** Pretvara neuspešan HTTP odgovor u ishod koji komponente znaju da prikažu. */
export async function toFailure(res: Response): Promise<ApiFailure> {
  const body = await readJson(res)
  const requestId = requestIdFrom(body)
  switch (res.status) {
    case 400: return { kind: 'invalid', messages: invalidMessages(body), requestId }
    case 401: return { kind: 'unauthenticated', requestId }
    case 403: return { kind: 'forbidden', requestId }
    case 404: return { kind: 'notFound', requestId }
    case 409: return { kind: 'conflict', requestId }
    case 501: return { kind: 'notImplemented', requestId }
    default: return { kind: 'error' }
  }
}

/**
 * LAB-01 kreiranje laboratorije. Šalje se samo naslov: ID dodeljuje server,
 * pa ga klijent nikad ne šalje.
 */
export async function createLab(title: string, user: TestUser): Promise<CreateLabOutcome> {
  let res: Response
  try {
    res = await fetch(`${LABS_BASE_PATH}/`, {
      method: 'POST',
      headers: headersFor(user, true),
      credentials: 'same-origin',
      body: JSON.stringify({ title }),
    })
  } catch {
    return { kind: 'error' }
  }

  if (res.status === 201) {
    const body = await readJson(res)
    return typeof body?.id === 'string' ? { kind: 'created', id: body.id } : { kind: 'error' }
  }
  return toFailure(res)
}

/**
 * LabLanguage je C# enum (Sr = 0, En = 1). Bez JsonStringEnumConverter server ga šalje kao broj,
 * a sa njim kao tekst ("Sr"), pa se prihvataju oba oblika.
 */
export function parseLanguage(value: unknown): Language | null {
  if (value === 0) return 'sr'
  if (value === 1) return 'en'
  if (typeof value !== 'string') return null
  const lower = value.toLowerCase()
  return isLanguage(lower) ? lower : null
}

function parseContent(body: Record<string, unknown> | null): LabContent | null {
  if (!body) return null
  const { revisionId, evaluatorVersion, language, title, description, instructions } = body
  const strings = [revisionId, evaluatorVersion, title, description, instructions]
  if (!strings.every(v => typeof v === 'string')) return null
  const lang = parseLanguage(language)
  if (!lang) return null
  return {
    revisionId: revisionId as string,
    evaluatorVersion: evaluatorVersion as string,
    language: lang,
    title: title as string,
    description: description as string,
    instructions: instructions as string,
  }
}

/**
 * LAB-07 čitanje laboratorije na izabranom jeziku. Ako prevod ne postoji,
 * server vraća podrazumevani srpski, a ovde se to označava kao usedFallback.
 * Jezik je string, a ne Language, da bi test mogao da proveri i nepodržan jezik.
 */
export async function getLabContent(labId: string, language: string, user: TestUser): Promise<LabContentOutcome> {
  let res: Response
  try {
    res = USE_FAKE_LAB_CONTENT
      ? await fakeLabContentResponse(labId, language, user)
      : await fetch(`${LABS_BASE_PATH}/${encodeURIComponent(labId)}?lang=${encodeURIComponent(language)}`, {
          headers: headersFor(user, false),
          credentials: 'same-origin',
          cache: 'no-store',
        })
  } catch {
    return { kind: 'error' }
  }

  if (res.status !== 200) return toFailure(res)

  const content = parseContent(await readJson(res))
  if (!content) return { kind: 'error' }
  return { kind: 'ok', content, usedFallback: content.language !== language.toLowerCase() }
}

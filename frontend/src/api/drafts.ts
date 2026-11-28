import type { AuthContextValue } from '../types/auth'
import { isDraftStatus, isLabType } from '../components/drafts/DraftTypes'
import type { CreateDraftData, Draft } from '../components/drafts/DraftTypes'

// AUT-01: pozivi ka serveru za nacrte zadataka su na ovom mestu.
// Stranica zavisi od DraftsGateway, a ne od fetch-a, pa se izbor demo ili serverske
// implementacije menja samo u DraftsRoute.
//
// Ugovor koji se očekuje od servera (oblik nacrta prati DraftResponse iz authoring modula):
//   GET  /api/authoring/drafts  -> 200 { drafts: DraftResponse[] }
//   POST /api/authoring/drafts  telo { title, goal, type } -> 201 DraftResponse
// ID, status i vlasnika dodeljuje server. Klijent ih nikad ne šalje.
export const DRAFTS_PATH = '/api/authoring/drafts'

export type DraftsRequest = AuthContextValue['request']

export interface DraftsGateway {
  /** false kada se nacrti ne čuvaju na serveru (demo). */
  readonly persistsOnServer: boolean
  list(signal?: AbortSignal): Promise<Draft[]>
  create(data: CreateDraftData, signal?: AbortSignal): Promise<Draft>
}

export class DraftsApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'DraftsApiError'
    this.status = status
  }
}

/** Build-time prekidač: VITE_DRAFTS_API=true uključuje serverski režim stranice. */
export function isDraftsApiEnabled(): boolean {
  return import.meta.env.VITE_DRAFTS_API === 'true'
}

const invalidResponseMessage = 'Server je vratio neočekivan odgovor za nacrte.'

// Server ne diktira tekst poruke: prikazuju se samo naše poruke, da greška ne otkrije interne detalje.
function errorMessage(status: number): string {
  switch (status) {
    case 400:
    case 422: return 'Podaci nacrta nisu prošli proveru. Proverite naziv, cilj i tip laboratorije.'
    case 401: return 'Sesija je istekla. Prijavite se ponovo.'
    case 403: return 'Nemate pravo da radite sa nacrtima zadataka.'
    case 409: return 'Podaci su u međuvremenu izmenjeni. Osvežite prikaz i pokušajte ponovo.'
    case 429: return 'Previše zahteva. Pokušajte ponovo za nekoliko trenutaka.'
    case 503: return 'Nacrti trenutno nisu dostupni. Pokušajte ponovo.'
    default: return 'Zahtev za nacrte nije uspeo. Pokušajte ponovo.'
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

function rethrowAbort(error: unknown, signal?: AbortSignal | null): void {
  signal?.throwIfAborted()
  if (isRecord(error) && error.name === 'AbortError') throw error
}

async function send(request: DraftsRequest, init: RequestInit): Promise<Response> {
  const signal = init.signal
  signal?.throwIfAborted()
  let response: Response
  try {
    response = await request(DRAFTS_PATH, init)
  } catch (error) {
    rethrowAbort(error, signal)
    throw new DraftsApiError(0, 'Server nije dostupan. Proverite vezu i pokušajte ponovo.')
  }
  signal?.throwIfAborted()
  if (!response.ok) throw new DraftsApiError(response.status, errorMessage(response.status))
  return response
}

async function readJson(response: Response, signal?: AbortSignal | null): Promise<unknown> {
  try {
    const body: unknown = await response.json()
    signal?.throwIfAborted()
    return body
  } catch (error) {
    rethrowAbort(error, signal)
    throw new DraftsApiError(response.status, invalidResponseMessage)
  }
}

// Iz odgovora se uzima samo ono što ekran prikazuje; polja kao što je ownerId ne prelaze u stanje klijenta.
function toDraft(value: unknown): Draft | null {
  if (!isRecord(value)) return null
  const { id, title, goal, type, status } = value
  if (typeof id !== 'string' || id.trim() === '') return null
  if (typeof title !== 'string' || typeof goal !== 'string') return null
  if (!isLabType(type) || !isDraftStatus(status)) return null
  return { id, title, goal, type, status }
}

export function createHttpDraftsGateway(request: DraftsRequest): DraftsGateway {
  return {
    persistsOnServer: true,

    async list(signal) {
      const response = await send(request, { method: 'GET', cache: 'no-store', signal })
      const body = await readJson(response, signal)
      if (!isRecord(body) || !Array.isArray(body.drafts)) {
        throw new DraftsApiError(response.status, invalidResponseMessage)
      }
      const drafts: Draft[] = []
      for (const item of body.drafts) {
        const draft = toDraft(item)
        if (!draft) throw new DraftsApiError(response.status, invalidResponseMessage)
        drafts.push(draft)
      }
      return drafts
    },

    async create(data, signal) {
      const response = await send(request, {
        method: 'POST',
        cache: 'no-store',
        headers: { 'Content-Type': 'application/json' },
        // Samo polja koja bira korisnik; ID, status i vlasnika dodeljuje server.
        body: JSON.stringify({ title: data.title, goal: data.goal, type: data.type }),
        signal,
      })
      if (response.status !== 201) throw new DraftsApiError(response.status, invalidResponseMessage)
      const draft = toDraft(await readJson(response, signal))
      if (!draft) throw new DraftsApiError(response.status, invalidResponseMessage)
      return draft
    },
  }
}

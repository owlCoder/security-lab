

export type ApiError = {
  status: number
  code: string
  message: string
  /** Detalji validacije (422 validation_failed, REV-02). */
  errors?: { field: string; message: string }[]
  retryAfterSeconds?: number
}

export type ApiResult<T> = { ok: true; data: T } | { ok: false; error: ApiError }

export const DEFAULT_RETRY_AFTER_SECONDS = 60
export const MAX_RETRY_AFTER_SECONDS = 60 * 60


export function parseRetryAfter(header: string | null | undefined, now: Date = new Date()): number {
  if (header == null) return DEFAULT_RETRY_AFTER_SECONDS
  const value = header.trim()
  if (value === '') return DEFAULT_RETRY_AFTER_SECONDS

  let seconds: number
  if (/^\d+$/.test(value)) {
    seconds = Number(value)
  } else {
    if (!/[A-Za-z]/.test(value)) return DEFAULT_RETRY_AFTER_SECONDS
    const at = Date.parse(value)
    if (Number.isNaN(at)) return DEFAULT_RETRY_AFTER_SECONDS
    seconds = Math.ceil((at - now.getTime()) / 1000)
  }

  if (!Number.isFinite(seconds)) return DEFAULT_RETRY_AFTER_SECONDS
  return Math.min(Math.max(seconds, 1), MAX_RETRY_AFTER_SECONDS)
}

const FALLBACK_MESSAGES: Record<number, string> = {
  400: 'Zahtev nije ispravan.',
  401: 'Niste prijavljeni.',
  403: 'Nemate dozvolu za ovu radnju.',
  404: 'Traženi resurs ne postoji.',
  409: 'Podaci su u međuvremenu izmenjeni. Osvežite prikaz i pokušajte ponovo.',
  422: 'Podaci nisu prošli proveru.',
  429: 'Previše pokušaja. Pokušajte kasnije.',
}

export async function toApiError(response: Response): Promise<ApiError> {
  let code = 'http_' + response.status
  let message = FALLBACK_MESSAGES[response.status] ?? 'Došlo je do greške. Pokušajte ponovo.'
  let errors: ApiError['errors']

  try {
    const body = await response.json()
    if (body && typeof body === 'object') {
      if (typeof body.code === 'string') code = body.code
      if (typeof body.message === 'string' && body.message.trim() !== '') message = body.message
      if (Array.isArray(body.errors)) {
        errors = body.errors
          .filter((e: unknown) => e && typeof e === 'object')
          .map((e: { field?: unknown; message?: unknown }) => ({ field: String(e.field ?? ''), message: String(e.message ?? '') }))
      }
    }
  } catch {
  }

  const error: ApiError = { status: response.status, code, message }
  if (errors?.length) error.errors = errors
  if (response.status === 429) {
    error.retryAfterSeconds = parseRetryAfter(response.headers.get('Retry-After'))
  }
  return error
}

let accessToken: string | null = null
export const session = {
  get token() { return accessToken },
  set(token: string | null) { accessToken = token },
}

export type Fetcher = (input: string, init?: RequestInit) => Promise<Response>

let fetcher: Fetcher = (input, init) => fetch(input, init)
/** Za testove i mock rezim. */
export function setFetcher(f: Fetcher) { fetcher = f }

export async function apiRequest<T>(
  method: 'GET' | 'POST' | 'PUT' | 'DELETE',
  path: string,
  body?: unknown,
  extraHeaders: Record<string, string> = {},
): Promise<ApiResult<T>> {
  const headers: Record<string, string> = { Accept: 'application/json', ...extraHeaders }
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  if (session.token) headers.Authorization = `Bearer ${session.token}`

  let response: Response
  try {
    response = await fetcher(path, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch {
    return { ok: false, error: { status: 0, code: 'network_error', message: 'Server nije dostupan. Proverite vezu.' } }
  }

  if (!response.ok) return { ok: false, error: await toApiError(response) }
  if (response.status === 204) return { ok: true, data: undefined as T }

  try {
    return { ok: true, data: (await response.json()) as T }
  } catch {
    return { ok: false, error: { status: response.status, code: 'invalid_response', message: 'Server je vratio neočekivan odgovor.' } }
  }
}
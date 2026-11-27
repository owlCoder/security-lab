/**
 * US-15 / SEC-03: every state-changing request sends the CSRF token in the X-CSRF-TOKEN header.
 *
 * GET /api/auth/csrf sets two cookies: an HttpOnly one (the secret half, sent by the browser on its own) and a
 * readable one with the request token. This module copies the readable one into the header, synchronously,
 * so a request leaves at once and in the same order as before. A foreign page cannot read our cookies,
 * so it cannot build this header.
 *
 * When the server answers 403 csrf_invalid (no token yet, expired keys, server restart), a fresh token is
 * fetched and the request is repeated exactly once.
 */
export const CSRF_HEADER = 'X-CSRF-TOKEN'
export const CSRF_COOKIE = '__Host-SecurityLab.CsrfToken'
export const CSRF_INVALID_CODE = 'csrf_invalid'
export const CSRF_TOKEN_URL = '/api/auth/csrf'

export type SendFn = (input: RequestInfo | URL, init: RequestInit) => Promise<Response>

const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE'])

export function isStateChanging(method: string | undefined): boolean {
  return !SAFE_METHODS.has((method ?? 'GET').toUpperCase())
}

/** The request token from the readable cookie, or null when the browser has none yet. */
export function readCsrfCookie(): string | null {
  if (typeof document === 'undefined') return null
  const prefix = `${CSRF_COOKIE}=`
  const part = document.cookie.split('; ').find(cookie => cookie.startsWith(prefix))
  return part ? decodeURIComponent(part.slice(prefix.length)) : null
}

/**
 * Asks the server for a token (it also renews both cookies). Always the browser's own fetch: the token belongs
 * to the real server, never to a test or mock fetcher. Returns null when the server cannot be reached.
 */
export async function refreshCsrfToken(): Promise<string | null> {
  try {
    const response = await fetch(CSRF_TOKEN_URL, { credentials: 'same-origin', cache: 'no-store' })
    if (!response.ok) return null
    const body: unknown = await response.json()
    const token = (body as { token?: unknown } | null)?.token
    return typeof token === 'string' && token !== '' ? token : null
  } catch {
    return null
  }
}

// Keeps the caller's header shape: other modules read init.headers as a plain object.
function withToken(init: RequestInit, token: string | null): RequestInit {
  if (!token) return init
  const { headers } = init
  if (headers instanceof Headers) {
    const copy = new Headers(headers)
    copy.set(CSRF_HEADER, token)
    return { ...init, headers: copy }
  }
  if (Array.isArray(headers)) return { ...init, headers: [...headers, [CSRF_HEADER, token]] }
  return { ...init, headers: { ...headers, [CSRF_HEADER]: token } }
}

async function isCsrfRejection(response: Response): Promise<boolean> {
  if (response.status !== 403) return false
  try {
    const body: unknown = await response.clone().json()
    return (body as { code?: unknown } | null)?.code === CSRF_INVALID_CODE
  } catch {
    return false
  }
}

/**
 * Drop-in replacement for fetch(): adds the CSRF header to POST, PUT, PATCH and DELETE and passes
 * GET/HEAD/OPTIONS through untouched. `send` is the function that really sends the request (default: fetch).
 * A retried request sends its body again, so pass a string or nothing, never a stream.
 */
export async function csrfFetch(
  input: RequestInfo | URL,
  init: RequestInit = {},
  send: SendFn = (url, options) => fetch(url, options),
): Promise<Response> {
  if (!isStateChanging(init.method)) return send(input, init)

  const response = await send(input, withToken(init, readCsrfCookie()))
  if (!(await isCsrfRejection(response))) return response

  return send(input, withToken(init, await refreshCsrfToken()))
}

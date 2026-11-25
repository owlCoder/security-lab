import { apiFetch } from '../auth/apiClient'

// IDN-07: the caller's own sessions. The server returns only the handle and times, never a token.
export type SessionInfo = {
  handle: string
  createdAt: string
  lastActivityAt: string
  expiresAt: string
}

export type AuthRequest = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>

export type ListSessionsOutcome =
  | { kind: 'ok'; sessions: SessionInfo[] }
  | { kind: 'unauthenticated' }
  | { kind: 'forbidden' }
  | { kind: 'error' }

export type RevokeSessionOutcome =
  | { kind: 'revoked' }
  | { kind: 'not_found' }
  | { kind: 'unauthenticated' }
  | { kind: 'forbidden' }
  | { kind: 'error' }

/**
 * `request` is `useAuth().request`: on any 401 (`session_invalid` or `unauthenticated`) it clears the signed-in
 * user, and ProtectedRoute then sends the browser to the login page.
 */
export async function listSessions(request: AuthRequest = apiFetch): Promise<ListSessionsOutcome> {
  let res: Response
  try {
    res = await request('/api/auth/sessions', { headers: { Accept: 'application/json' } })
  } catch {
    return { kind: 'error' }
  }

  switch (res.status) {
    case 200: {
      const body: unknown = await res.json().catch(() => null)
      return Array.isArray(body) ? { kind: 'ok', sessions: body as SessionInfo[] } : { kind: 'error' }
    }
    case 401:
      return { kind: 'unauthenticated' }
    case 403:
      return { kind: 'forbidden' }
    default:
      return { kind: 'error' }
  }
}

export async function revokeSession(handle: string, request: AuthRequest = apiFetch): Promise<RevokeSessionOutcome> {
  let res: Response
  try {
    res = await request(`/api/auth/sessions/${encodeURIComponent(handle)}`, { method: 'DELETE' })
  } catch {
    return { kind: 'error' }
  }

  switch (res.status) {
    case 204:
      return { kind: 'revoked' }
    case 401:
      return { kind: 'unauthenticated' }
    case 403:
      return { kind: 'forbidden' }
    case 404:
      return { kind: 'not_found' }
    default:
      return { kind: 'error' }
  }
}

import { parseRetryAfter } from './http'

export type CreateAccountOutcome =
  | { kind: 'created'; id: string; correlationId?: string }
  | { kind: 'taken'; correlationId?: string }
  | { kind: 'invalid'; errors: string[]; correlationId?: string }
  | { kind: 'unauthenticated'; correlationId?: string }
  | { kind: 'forbidden'; correlationId?: string }
  | { kind: 'error' }

interface ApiBody {
  id?: string
  errors?: string[]
  correlationId?: string
}

export async function createAccount(email: string, password: string): Promise<CreateAccountOutcome> {
  let res: Response
  try {
    res = await fetch('/api/admin/accounts', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
    })
  } catch {
    return { kind: 'error' }
  }

  const body = (await res.json().catch(() => null)) as ApiBody | null
  const correlationId = body?.correlationId

  switch (res.status) {
    case 201:
      return body?.id ? { kind: 'created', id: body.id, correlationId } : { kind: 'error' }
    case 400:
      return { kind: 'invalid', errors: body?.errors ?? ['Zahtev nije validan.'], correlationId }
    case 401:
      return { kind: 'unauthenticated', correlationId }
    case 403:
      return { kind: 'forbidden', correlationId }
    case 409:
      return { kind: 'taken', correlationId }
    default:
      return { kind: 'error' }
  }
}
export const ACCOUNT_STATUSES = ['ACTIVE', 'BLOCKED', 'ARCHIVED'] as const
export type AccountStatus = (typeof ACCOUNT_STATUSES)[number]

export type ChangeAccountStatusOutcome =
  | { kind: 'changed'; id: string; status: AccountStatus }
  | { kind: 'not_found' }
  | { kind: 'invalid_status' }
  | { kind: 'invalid_transition' }
  | { kind: 'self_change' }
  | { kind: 'unauthenticated' }
  | { kind: 'forbidden' }
  | { kind: 'throttled'; retryAfterSeconds: number }
  | { kind: 'unavailable' }
  | { kind: 'error' }

interface StatusBody {
  id?: string
  status?: string
  code?: string
}

// IDN-03 / UC-05: PUT /api/admin/accounts/{id}/status. The reason is not sent: the Audit contract stores no free text.
export async function changeAccountStatus(id: string, status: AccountStatus): Promise<ChangeAccountStatusOutcome> {
  let res: Response
  try {
    res = await fetch(`/api/admin/accounts/${encodeURIComponent(id)}/status`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ status }),
    })
  } catch {
    return { kind: 'error' }
  }

  const body = (await res.json().catch(() => null)) as StatusBody | null

  switch (res.status) {
    case 200: {
      const confirmed = ACCOUNT_STATUSES.find(candidate => candidate === body?.status)
      return body?.id && confirmed ? { kind: 'changed', id: body.id, status: confirmed } : { kind: 'error' }
    }
    case 400:
      return { kind: 'invalid_status' }
    case 401:
      return { kind: 'unauthenticated' }
    case 403:
      return { kind: 'forbidden' }
    case 404:
      return { kind: 'not_found' }
    case 409:
      return body?.code === 'self_change_not_allowed' ? { kind: 'self_change' } : { kind: 'invalid_transition' }
    case 429:
      return { kind: 'throttled', retryAfterSeconds: parseRetryAfter(res.headers.get('Retry-After')) }
    case 503:
      return { kind: 'unavailable' }
    default:
      return { kind: 'error' }
  }
}

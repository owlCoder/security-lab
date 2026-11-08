export type CreateAccountOutcome =
  | { kind: 'created'; id: string; correlationId?: string }
  | { kind: 'taken'; correlationId?: string }
  | { kind: 'invalid'; errors: string[]; correlationId?: string }
  | { kind: 'unauthenticated'; correlationId?: string }
  | { kind: 'forbidden'; correlationId?: string }
  | { kind: 'confirmationUnavailable'; correlationId?: string }
  | { kind: 'error' }

interface ApiBody {
  id?: string
  errors?: string[]
  error?: string
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
      if (res.status === 503 && body?.error === 'confirmation_email_unavailable')
        return { kind: 'confirmationUnavailable', correlationId }
      return { kind: 'error' }
  }
}
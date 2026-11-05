import { apiRequest, session, type ApiError } from './http'



type LoginResponse = { tokenType: string; accessToken: string }

export type LoginState =
  | { kind: 'success' }
  | { kind: 'invalid' }
  | { kind: 'throttled'; retryAfterSeconds: number }
  | { kind: 'error'; message: string }

export const INVALID_CREDENTIALS_MESSAGE = 'Pogrešan imejl ili lozinka.'

export function mapLoginError(error: ApiError): LoginState {
  switch (error.status) {
    case 401:
      return { kind: 'invalid' }
    case 429:
      return { kind: 'throttled', retryAfterSeconds: error.retryAfterSeconds ?? 60 }
    default:
      return { kind: 'error', message: error.message }
  }
}

export async function login(email: string, password: string): Promise<LoginState> {
  const result = await apiRequest<LoginResponse>('POST', '/api/auth/login', { email, password })
  if (!result.ok) return mapLoginError(result.error)
  if (!result.data?.accessToken) return { kind: 'error', message: 'Server je vratio neočekivan odgovor.' }
  session.set(result.data.accessToken)
  return { kind: 'success' }
}

export function logout() {
  session.set(null)
}
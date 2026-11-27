import { apiFetch } from '../auth/apiClient'
import { toApiError, type ApiResult } from './http'
import type { AuthContextValue } from '../types/auth'
import type { LoginHistoryFilter, LoginHistoryPage } from '../types/loginHistory'
// IDN-22 TIM 09

// UX limits only; the server validates every request again and is the only real check (SEC-05).
export const LOGIN_HISTORY_PAGE_SIZES = [10, 20, 50] as const
export const DEFAULT_PAGE_SIZE = 20
export const MAX_RANGE_DAYS = 90
export const MS_PER_DAY = 24 * 60 * 60 * 1000

const LOGIN_HISTORY_PATH = '/api/auth/login-history'
const DATE_INPUT_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/

const NETWORK_ERROR = { status: 0, code: 'network_error', message: '' }

/**
 * IDN-22: reads the caller's own login history. Only the whitelisted filter fields are copied into the query,
 * so a userId/subjectId can never reach the server from here (SEC-01).
 * `request` is `useAuth().request`: on a 401 (`session_invalid` after IDN-07 revoke or IDN-03 block, or
 * `unauthenticated`) it clears the signed-in user, and ProtectedRoute then sends the browser to the login page.
 */
export async function getLoginHistory(
  filter: LoginHistoryFilter,
  request: AuthContextValue['request'] = apiFetch,
): Promise<ApiResult<LoginHistoryPage>> {
  const params = new URLSearchParams()
  if (filter.from) params.set('from', filter.from)
  if (filter.to) params.set('to', filter.to)
  if (filter.outcome) params.set('outcome', filter.outcome)
  params.set('page', String(filter.page))
  params.set('pageSize', String(filter.pageSize))

  let response: Response
  try {
    response = await request(`${LOGIN_HISTORY_PATH}?${params}`, { method: 'GET', headers: { Accept: 'application/json' } })
  } catch {
    return { ok: false, error: NETWORK_ERROR }
  }
  if (!response.ok) return { ok: false, error: await toApiError(response) }
  try {
    return { ok: true, data: (await response.json()) as LoginHistoryPage }
  } catch {
    return { ok: false, error: { status: response.status, code: 'invalid_response', message: '' } }
  }
}

function localDate(value: string, hours: number, minutes: number, seconds: number, ms: number): string | undefined {
  const match = DATE_INPUT_PATTERN.exec(value)
  if (!match) return undefined
  const [year, month, day] = [Number(match[1]), Number(match[2]) - 1, Number(match[3])]
  const date = new Date(year, month, day, hours, minutes, seconds, ms)
  // Rejects rollover such as 2026-02-30 becoming March 2.
  if (date.getFullYear() !== year || date.getMonth() !== month || date.getDate() !== day) return undefined
  return date.toISOString()
}

/** Start of a local calendar day (`YYYY-MM-DD` from `<input type="date">`) as UTC ISO 8601; undefined when empty or invalid. */
export function localDayStartIso(value: string): string | undefined {
  return localDate(value, 0, 0, 0, 0)
}

/** End of a local calendar day (23:59:59.999) as UTC ISO 8601; the server treats both bounds as inclusive. */
export function localDayEndIso(value: string): string | undefined {
  return localDate(value, 23, 59, 59, 999)
}

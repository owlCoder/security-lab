import { DEFAULT_RETRY_AFTER_SECONDS, type ApiError } from '../../api/http'
// IDN-22 TIM 09

// Fixed user-facing texts (OPS-07): the server's message and raw codes are never shown.
export const LOGIN_HISTORY_MESSAGES = {
  invalidTimeRange: 'Početni datum mora biti pre krajnjeg.',
  rangeTooLarge: 'Period može biti najviše 90 dana.',
  invalidFilter: 'Filter nije ispravan. Proverite unete vrednosti.',
  sessionExpired: 'Sesija je istekla. Prijavite se ponovo.',
  unavailable: 'Istorija prijava trenutno nije dostupna. Pokušajte kasnije.',
  empty: 'Nema prijava za izabrani period.',
  loading: 'Učitavanje…',
} as const

const BAD_REQUEST_MESSAGES: Record<string, string> = {
  invalid_time_range: LOGIN_HISTORY_MESSAGES.invalidTimeRange,
  range_too_large: LOGIN_HISTORY_MESSAGES.rangeTooLarge,
  invalid_page: LOGIN_HISTORY_MESSAGES.invalidFilter,
  invalid_page_size: LOGIN_HISTORY_MESSAGES.invalidFilter,
  invalid_request: LOGIN_HISTORY_MESSAGES.invalidFilter,
}

/** IDN-22: maps an API failure to a short Serbian message using only status and stable code. */
export function loginHistoryErrorMessage(error: ApiError): string {
  if (error.status === 400) return BAD_REQUEST_MESSAGES[error.code] ?? LOGIN_HISTORY_MESSAGES.invalidFilter
  if (error.status === 401) return LOGIN_HISTORY_MESSAGES.sessionExpired
  if (error.status === 429) {
    const seconds = error.retryAfterSeconds ?? DEFAULT_RETRY_AFTER_SECONDS
    return `Previše zahteva. Pokušajte ponovo za ${seconds} s.`
  }
  return LOGIN_HISTORY_MESSAGES.unavailable
}

export function foundMessage(count: number): string {
  return `Pronađeno: ${count}`
}

export function pageLabel(page: number): string {
  return `Strana ${page}`
}

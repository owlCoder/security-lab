import { useCallback, useEffect, useRef, useState } from 'react'
import { useAuth } from '../auth/AuthContext'
import { DEFAULT_PAGE_SIZE, getLoginHistory, localDayEndIso, localDayStartIso, MAX_RANGE_DAYS, MS_PER_DAY } from '../api/loginHistory'
import LoginHistoryFilters from '../components/loginHistory/LoginHistoryFilters'
import LoginHistoryTable from '../components/loginHistory/LoginHistoryTable'
import { foundMessage, LOGIN_HISTORY_MESSAGES, loginHistoryErrorMessage } from '../components/loginHistory/loginHistoryMessages'
import type { LoginHistoryFilter, LoginHistoryFormValues, LoginHistoryPage as LoginHistoryResult } from '../types/loginHistory'
import '../components/loginHistory/loginHistory.css'
// IDN-22 TIM 09

const INITIAL_VALUES: LoginHistoryFormValues = { from: '', to: '', outcome: 'all', pageSize: DEFAULT_PAGE_SIZE }
const INITIAL_FILTER: LoginHistoryFilter = { page: 1, pageSize: DEFAULT_PAGE_SIZE }

type Validation = { ok: true; filter: LoginHistoryFilter } | { ok: false; message: string }

// Client-side check is UX only: it avoids an obviously invalid request, the server validates again (SEC-05).
function toFilter(values: LoginHistoryFormValues): Validation {
  const from = localDayStartIso(values.from)
  const to = localDayEndIso(values.to)
  if ((values.from !== '' && !from) || (values.to !== '' && !to)) return { ok: false, message: LOGIN_HISTORY_MESSAGES.invalidFilter }
  if (from && to) {
    const span = Date.parse(to) - Date.parse(from)
    if (span < 0) return { ok: false, message: LOGIN_HISTORY_MESSAGES.invalidTimeRange }
    if (span > MAX_RANGE_DAYS * MS_PER_DAY) return { ok: false, message: LOGIN_HISTORY_MESSAGES.rangeTooLarge }
  }
  const outcome = values.outcome === 'all' ? undefined : values.outcome
  return { ok: true, filter: { from, to, outcome, page: 1, pageSize: values.pageSize } }
}

/**
 * IDN-22: "Istorija prijava" page. Shows only the signed-in user's own logins; the subject comes from the
 * server session, never from this page (SEC-01). Errors are fixed Serbian messages (OPS-07).
 * Requests go through `useAuth().request`, so a 401 signs the user out and ProtectedRoute redirects to login.
 */
export default function LoginHistoryPage() {
  const { request } = useAuth()
  const [values, setValues] = useState<LoginHistoryFormValues>(INITIAL_VALUES)
  const [applied, setApplied] = useState<LoginHistoryFilter>(INITIAL_FILTER)
  const [result, setResult] = useState<LoginHistoryResult | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const latestRequest = useRef(0)
  const focusTarget = useRef<HTMLElement | null>(null)
  const focusPending = useRef(false)

  const load = useCallback(async (filter: LoginHistoryFilter, moveFocus: boolean) => {
    const requestNumber = ++latestRequest.current
    setLoading(true)
    setError('')
    const response = await getLoginHistory(filter, request)
    if (requestNumber !== latestRequest.current) return
    setLoading(false)
    if (!response.ok) {
      setResult(null)
      setError(loginHistoryErrorMessage(response.error))
      return
    }
    focusPending.current = moveFocus
    setApplied(filter)
    setResult(response.data)
  }, [request])

  useEffect(() => {
    void load(INITIAL_FILTER, false)
    return () => { latestRequest.current += 1 }
  }, [load])

  useEffect(() => {
    if (!result || !focusPending.current) return
    focusPending.current = false
    focusTarget.current?.focus()
  }, [result])

  function search(hasIncompleteDate: boolean) {
    if (hasIncompleteDate) {
      setError(LOGIN_HISTORY_MESSAGES.invalidFilter)
      return
    }
    const validation = toFilter(values)
    if (!validation.ok) {
      setError(validation.message)
      return
    }
    void load(validation.filter, true)
  }

  function reset() {
    setValues(INITIAL_VALUES)
    void load(INITIAL_FILTER, true)
  }

  const status = loading ? LOGIN_HISTORY_MESSAGES.loading : result ? foundMessage(result.items.length) : ''

  return <section className="card login-history">
    <h1>Istorija prijava</h1>
    <p className="login-history-hint">Prikazane su samo vaše prijave. Adrese su delimično skrivene.</p>
    <LoginHistoryFilters values={values} onChange={setValues} onSubmit={search} onReset={reset} />
    <p role="status" aria-live="polite" className="login-history-status">{status}</p>
    {error && <p role="alert" className="login-history-error">{error}</p>}
    {result && <LoginHistoryTable
      items={result.items}
      page={result.page}
      pageSize={result.pageSize}
      loading={loading}
      focusRef={element => { focusTarget.current = element }}
      onPrevious={() => void load({ ...applied, page: applied.page - 1 }, true)}
      onNext={() => void load({ ...applied, page: applied.page + 1 }, true)}
    />}
  </section>
}

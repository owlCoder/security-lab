import { useCallback, useEffect, useState } from 'react'
import { useAuth } from '../auth/AuthContext'
import { listSessions, revokeSession } from '../api/sessions'
import type { SessionInfo } from '../api/sessions'

const formatTime = (iso: string) => {
  const date = new Date(iso)
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString('sr-Latn-RS')
}

// IDN-07: review and revoke own sessions. A 401 from either call clears the signed-in user through
// `useAuth().request`, and ProtectedRoute then redirects to the login page.
export default function SessionsPage() {
  const { request } = useAuth()
  const [sessions, setSessions] = useState<SessionInfo[] | null>(null)
  const [error, setError] = useState('')
  const [revoking, setRevoking] = useState<string | null>(null)

  const load = useCallback(async () => {
    const outcome = await listSessions(request)
    if (outcome.kind === 'ok') {
      setSessions(outcome.sessions)
      setError('')
    } else if (outcome.kind === 'forbidden') {
      setError('Nemate dozvolu za pregled sesija.')
    } else if (outcome.kind === 'error') {
      setError('Sesije nije moguće učitati. Pokušajte ponovo.')
    }
  }, [request])

  useEffect(() => { void load() }, [load])

  async function handleRevoke(handle: string) {
    setRevoking(handle)
    setError('')
    const outcome = await revokeSession(handle, request)
    setRevoking(null)

    if (outcome.kind === 'revoked' || outcome.kind === 'not_found') {
      await load() // not_found: it was already gone, the refreshed list shows the truth
    } else if (outcome.kind === 'forbidden') {
      setError('Nemate dozvolu za opoziv sesija.')
    } else if (outcome.kind === 'error') {
      setError('Sesija nije opozvana. Pokušajte ponovo.')
    }
  }

  return (
    <main className="card">
      <h1>Moje sesije</h1>
      <p className="hint">
        Opozivom sesije koju trenutno koristite bićete odjavljeni i preusmereni na prijavu.
      </p>

      {error && <p className="login-error" role="alert">{error}</p>}
      {sessions === null && !error && <p role="status">Učitavanje…</p>}
      {sessions?.length === 0 && <p>Nema aktivnih sesija.</p>}

      {sessions && sessions.length > 0 && (
        <table className="table">
          <thead>
            <tr>
              <th scope="col">Kreirana</th>
              <th scope="col">Poslednja aktivnost</th>
              <th scope="col">Ističe</th>
              <th scope="col">Radnje</th>
            </tr>
          </thead>
          <tbody>
            {sessions.map(session => (
              <tr key={session.handle}>
                <td>{formatTime(session.createdAt)}</td>
                <td>{formatTime(session.lastActivityAt)}</td>
                <td>{formatTime(session.expiresAt)}</td>
                <td>
                  <button
                    type="button"
                    className="secondary"
                    disabled={revoking !== null}
                    onClick={() => void handleRevoke(session.handle)}
                  >
                    {revoking === session.handle ? 'Opoziv…' : 'Opozovi'}
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </main>
  )
}

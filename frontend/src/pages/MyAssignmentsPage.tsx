import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import type { ApiError, ApiResult } from '../api/http'
import { formatUtcForDisplay, getMyAssignments, type MyAssignment } from '../api/myAssignments'

type Props = {
  load?: () => Promise<ApiResult<MyAssignment[]>>
}

// ASN-02 (team 16): "Moji zadaci". Shows exactly what the server returned for the session's student.
// No filtering or deadline decisions here: the browser is outside the trust boundary (OPS-09, SEC-01).
export default function MyAssignmentsPage({ load = getMyAssignments }: Props) {
  const [items, setItems] = useState<MyAssignment[] | null>(null)
  const [error, setError] = useState<ApiError | null>(null)
  const [loading, setLoading] = useState(true)

  const refresh = useCallback(async () => {
    setLoading(true)
    setError(null)
    const r = await load()
    setLoading(false)
    if (r.ok) setItems(r.data)
    else {
      setItems(null)
      setError(r.error)
    }
  }, [load])

  useEffect(() => { void refresh() }, [refresh])

  return (
    <section className="card" aria-labelledby="my-assignments-title">
      <div className="card-head">
        <h2 id="my-assignments-title">Moji zadaci</h2>
        <button className="secondary" onClick={() => void refresh()} disabled={loading}>Osveži</button>
      </div>
      <p className="muted">Laboratorije dodeljene na kursevima na koje ste upisani. Vremena su prikazana u vašoj vremenskoj zoni.</p>

      {loading && items == null && <p className="muted" role="status">Učitavanje…</p>}

      {error && (
        <div className="alert alert-error" role="alert">
          <p>{error.status === 401 ? 'Sesija je istekla ili niste prijavljeni.' : error.message}</p>
          {error.status === 401 && <Link to="/login">Prijavite se ponovo</Link>}
        </div>
      )}

      {items != null && items.length === 0 && <p className="muted">Trenutno nemate dodeljenih zadataka.</p>}

      {items != null && items.length > 0 && (
        <table className="table">
          <thead>
            <tr>
              <th scope="col">Kurs</th>
              <th scope="col">Revizija laboratorije</th>
              <th scope="col">Dostupno od</th>
              <th scope="col">Rok</th>
              <th scope="col">Najviše pokušaja</th>
            </tr>
          </thead>
          <tbody>
            {items.map(a => (
              <tr key={a.assignmentId}>
                <td>{a.courseId}</td>
                <td>{a.publishedLabRevisionId}</td>
                <td>{a.availableFromUtc ? formatUtcForDisplay(a.availableFromUtc) : 'Odmah'}</td>
                <td>{formatUtcForDisplay(a.dueAtUtc)}</td>
                <td>{a.maxAttempts}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}

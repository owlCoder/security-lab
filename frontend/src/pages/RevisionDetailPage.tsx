import { useCallback, useEffect, useRef, useState } from 'react'
import type { ApiError } from '../api/http'
import {
  COMMENT_MAX_LENGTH, actionErrorMessage, formatDateTime, getRevision, newIdempotencyKey,
  runRevisionAction, shortHash, type RevisionAction, type RevisionDetail,
} from '../api/revisions'
import { DecisionHistory, StatusBadge } from '../components/RevisionBits'

type Props = { revisionId: string; onBack: () => void; onChanged?: () => void }

type Mode = 'idle' | 'approve' | 'return'

const SUCCESS: Record<RevisionAction, string> = {
  submit: 'Revizija je predata na pregled.',
  approve: 'Revizija je odobrena i objavljena.',
  return: 'Revizija je vraćena autoru na doradu.',
}

export default function RevisionDetailPage({ revisionId, onBack, onChanged }: Props) {
  const [revision, setRevision] = useState<RevisionDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [mode, setMode] = useState<Mode>('idle')
  const [comment, setComment] = useState('')
  const [busy, setBusy] = useState<RevisionAction | null>(null)
  const [actionError, setActionError] = useState<ApiError | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  // Jedan Idempotency-Key po nameri korisnika; zadrzava se posle mrezne greske
  // da ponovni pokusaj ne napravi drugu odluku.
  const keys = useRef<Partial<Record<RevisionAction, string>>>({})

  const load = useCallback(async () => {
    setLoadError(null)
    const r = await getRevision(revisionId)
    if (r.ok) setRevision(r.data)
    else setLoadError(actionErrorMessage(r.error.code, r.error.message))
  }, [revisionId])

  useEffect(() => { load() }, [load])

  function openMode(next: Mode) {
    // Komentar pripada jednoj nameri: otkazivanje ili promena radnje ga brise,
    // da komentar za "vrati" ne zavrsi uz odobrenje.
    setMode(next)
    setComment('')
    setActionError(null)
  }

  async function refresh() {
    setActionError(null)
    setSuccess(null)
    keys.current = {}
    await load()
  }

  async function run(action: RevisionAction) {
    if (!revision || busy) return
    if (action === 'return' && comment.trim() === '') {
      setActionError({ status: 400, code: 'comment_required', message: 'Unesite komentar za autora.' })
      return
    }

    setBusy(action)
    setActionError(null)
    setSuccess(null)
    const key = keys.current[action] ?? (keys.current[action] = newIdempotencyKey())

    const r = await runRevisionAction(revision, action, key, action === 'submit' ? undefined : comment)
    setBusy(null)

    if (r.ok) {
      delete keys.current[action]
      setRevision(r.data)
      setMode('idle')
      setComment('')
      setSuccess(SUCCESS[action])
      onChanged?.()
      return
    }
    // Konacan odgovor servera (4xx/5xx) -> nova namera dobija novi kljuc.
    if (r.error.status !== 0) delete keys.current[action]
    setActionError(r.error)
  }

  if (loadError) {
    return <section className="card">
      <button className="link" onClick={onBack}>← Nazad</button>
      <p className="alert alert-error" role="alert">{loadError}</p>
    </section>
  }
  if (!revision) return <section className="card"><p className="muted">Učitavanje…</p></section>

  const can = (a: RevisionAction) => revision.allowedActions.includes(a)
  const isStale = actionError?.code === 'revision_changed' || actionError?.code === 'invalid_state'
  const commentTooLong = comment.length > COMMENT_MAX_LENGTH

  return (
    <>
      <section className="card">
        <button className="link" onClick={onBack}>← Nazad</button>
        <div className="card-head">
          <h2>{revision.labTitle}</h2>
          <StatusBadge status={revision.status} />
        </div>
        <dl className="meta">
          <dt>Kurs</dt><dd>{revision.courseName}</dd>
          <dt>Autor</dt><dd>{revision.author.displayName}</dd>
          <dt>Broj revizije</dt><dd>{revision.revisionNumber ?? 'dodeljuje se pri objavi'}</dd>
          <dt>Poslednja izmena</dt><dd>{formatDateTime(revision.updatedAt)}</dd>
          <dt>Heš sadržaja</dt><dd><code title={revision.contentHash}>{shortHash(revision.contentHash)}</code></dd>
          <dt>Politika</dt>
          <dd>{revision.policy.requiresSeparateApprover ? 'Obavezan drugi odobravalac (autor ne može sam da odobri)' : 'Autor može sam da objavi'}</dd>
        </dl>

        {success && <p className="alert alert-ok" role="status">{success}</p>}
        {actionError && (
          <div className="alert alert-error" role="alert">
            <p>{actionErrorMessage(actionError.code, actionError.message)}</p>
            {actionError.errors && actionError.errors.length > 0 && (
              <ul>{actionError.errors.map((e, i) => <li key={i}>{e.field ? <code>{e.field}</code> : null} {e.message}</li>)}</ul>
            )}
            {isStale && <button className="secondary" onClick={refresh}>Osveži reviziju</button>}
          </div>
        )}

        <div className="actions">
          {can('submit') && (
            <button onClick={() => run('submit')} disabled={busy != null}>
              {busy === 'submit' ? 'Predaja…' : 'Predaj na pregled'}
            </button>
          )}
          {can('approve') && mode !== 'approve' && (
            <button onClick={() => openMode('approve')} disabled={busy != null}>Odobri</button>
          )}
          {can('return') && mode !== 'return' && (
            <button className="secondary" onClick={() => openMode('return')} disabled={busy != null}>Vrati sa komentarom</button>
          )}
          {revision.allowedActions.length === 0 && <p className="muted">Nemate dostupnih radnji nad ovom revizijom.</p>}
        </div>

        {mode !== 'idle' && (
          <div className="decision-form">
            <label>
              {mode === 'return' ? 'Komentar za autora (obavezno)' : 'Komentar (opciono)'}
              <textarea
                rows={4}
                value={comment}
                onChange={e => setComment(e.target.value)}
                maxLength={COMMENT_MAX_LENGTH + 100}
                aria-invalid={commentTooLong}
              />
            </label>
            <p className={`small ${commentTooLong ? 'error-text' : 'muted'}`}>{comment.length} / {COMMENT_MAX_LENGTH}</p>
            {mode === 'approve' && (
              <p className="small muted">
                Odobravate sadržaj sa hešom <code>{shortHash(revision.contentHash)}</code>. Ako se sadržaj u međuvremenu promeni, server će odbiti odobrenje.
              </p>
            )}
            <div className="actions">
              <button
                onClick={() => run(mode)}
                disabled={busy != null || commentTooLong || (mode === 'return' && comment.trim() === '')}
              >
                {busy === mode ? 'Slanje…' : mode === 'approve' ? 'Potvrdi odobrenje i objavi' : 'Vrati na doradu'}
              </button>
              <button className="secondary" onClick={() => openMode('idle')} disabled={busy != null}>Otkaži</button>
            </div>
          </div>
        )}
      </section>

      <section className="card">
        <h2>Istorija odluka</h2>
        <DecisionHistory decisions={revision.decisions} />
      </section>
    </>
  )
}

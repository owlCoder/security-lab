import { useState } from 'react'
import { csrfFetch } from '../auth/csrf'
const id = (group: number, number: number) => `${group}0000000-0000-0000-0000-${String(number).padStart(12, '0')}`
type Attempt = { id: string; answer: string; version: number }
type Course = { courseId: string; displayName: string; version: number; capabilities: string[] }
type Grant = { id: string; version: number; state: string }

export default function CourseworkKt1() {
  const [tab, setTab] = useState<'attempt' | 'admin'>('attempt')
  const [persona, setPersona] = useState('ana')
  const [session, setSession] = useState('Anonymous')
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState('Choose a synthetic account, then sign in.')
  const [attemptId, setAttemptId] = useState(id(3, 1))
  const [attempt, setAttempt] = useState<Attempt | null>(null)
  const [answer, setAnswer] = useState('')
  const [courseId, setCourseId] = useState(id(2, 1))
  const [course, setCourse] = useState<Course | null>(null)
  const [title, setTitle] = useState('')
  const [grant, setGrant] = useState<Grant | null>(null)
  const [canRename, setCanRename] = useState(true)
  const [revokeId, setRevokeId] = useState('')
  const [revokeVersion, setRevokeVersion] = useState(1)
  function clearPrivateState() { setAttempt(null); setAnswer(''); setCourse(null); setTitle(''); setGrant(null) }
  async function request<T>(path: string, method = 'GET', body?: unknown): Promise<T | null> {
    setBusy(true)
    try {
      const response = await csrfFetch(`/api/kt1${path}`, {
        method, credentials: 'same-origin', cache: 'no-store',
        headers: method === 'GET' ? {} : { 'X-Kt1-Demo': '1', ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
        body: body === undefined ? undefined : JSON.stringify(body),
      })
      const text = await response.text()
      setResult(`HTTP ${response.status}\n${text ? JSON.stringify(JSON.parse(text), null, 2) : 'Session cleared.'}`)
      return response.ok && text ? JSON.parse(text) as T : null
    } catch { setResult('API unavailable. Start the AccessKt1 backend profile.'); return null }
    finally { setBusy(false) }
  }
  async function login() { clearPrivateState(); setSession('Anonymous'); if (await request(`/session/${persona}`, 'POST')) setSession(persona) }
  async function logout() { await request('/logout', 'POST'); clearPrivateState(); setSession('Anonymous') }
  async function loadAttempt() {
    setAttempt(null); setAnswer('')
    const value = await request<Attempt>(`/attempts/${attemptId}`)
    if (value) { setAttempt(value); setAnswer(value.answer) }
  }
  async function saveDraft() {
    if (!attempt) return
    const value = await request<Attempt>(`/attempts/${attemptId}/answer`, 'PUT', { answer, expectedVersion: attempt.version })
    setAttempt(value); if (value) setAnswer(value.answer); else setAnswer('')
  }
  async function loadCourse() {
    setCourse(null); setTitle('')
    const value = await request<Course>(`/courses/${courseId}/administration`)
    if (value) { setCourse(value); setTitle(value.displayName) }
  }
  async function rename() {
    if (!course) return
    const value = await request<Course>(`/courses/${courseId}/settings`, 'PUT', { displayName: title, expectedVersion: course.version })
    setCourse(value); if (!value) setTitle('')
  }
  async function delegate() {
    const value = await request<Grant>(`/courses/${courseId}/delegations`, 'POST', {
      granteeId: id(1, 9), capabilities: canRename ? ['ViewAdministration', 'ManageSettings'] : ['ViewAdministration'],
    })
    setGrant(value); if (value) { setRevokeId(value.id); setRevokeVersion(value.version) }
  }
  async function revoke() {
    if (!revokeId) return
    const value = await request<Grant>(`/courses/${courseId}/delegations/${revokeId}/revoke`, 'POST', { expectedVersion: revokeVersion })
    setGrant(value); if (value) setRevokeVersion(value.version)
  }
  return <section className="kt1" aria-labelledby="kt1-title">
    <div className="kt1-heading"><div><p className="eyebrow">ACC-04 + ADM-08</p><h2 id="kt1-title">Ownership & course administration</h2></div><span className="badge">KT1 simulator</span></div>
    <p className="notice">Development only · synthetic accounts and in-memory seed · restart the API to reset. Administrative grants never give teaching access.</p>
    <fieldset disabled={busy} className="session-row"><legend>Synthetic session</legend>
      <label>Account<select value={persona} onChange={e => setPersona(e.target.value)}>
        {['ana', 'marko', 'teacher', 'foreign-teacher', 'blocked', 'assistant', 'admin', 'other-admin', 'delegate'].map(p => <option key={p}>{p}</option>)}
      </select></label><button onClick={login}>Sign in</button><button className="secondary" onClick={logout}>Sign out</button><p>Session: <strong>{session}</strong></p>
    </fieldset>
    <div className="tabs" role="tablist" aria-label="KT1 component">
      <button role="tab" aria-selected={tab === 'attempt'} disabled={busy} onClick={() => setTab('attempt')}>ACC-04 · Attempts</button>
      <button role="tab" aria-selected={tab === 'admin'} disabled={busy} onClick={() => setTab('admin')}>ADM-08 · Administration</button>
    </div>
    <fieldset disabled={busy} className="work-panel"><legend>{tab === 'attempt' ? 'Resource access' : 'Course delegation'}</legend>
      {tab === 'attempt' ? <>
        <label>Attempt<select value={attemptId} onChange={e => { setAttemptId(e.target.value); setAttempt(null); setAnswer('') }}>
          {[[1, 'Ana · submitted'], [2, 'Marko · submitted'], [4, 'Ana · draft'], [5, 'Ana · completed'], [99, 'Missing resource']].map(([n, name]) => <option key={n} value={id(3, Number(n))}>{name}</option>)}
        </select></label>
        <button onClick={loadAttempt}>Read attempt</button>
        <label>Answer<textarea rows={4} maxLength={4000} value={answer} disabled={!attempt} onChange={e => setAnswer(e.target.value)} /></label>
        <div className="action-row"><button onClick={saveDraft} disabled={!attempt}>Save own draft</button><span>Version: {attempt?.version ?? '—'}</span></div>
        <p className="hint">Try Marko against Ana's ID; try a teacher against an unsubmitted draft. Submitted and completed answers are immutable.</p>
      </> : <>
        <label>Course<select value={courseId} onChange={e => { setCourseId(e.target.value); setCourse(null); setTitle(''); setGrant(null) }}>
          <option value={id(2, 1)}>Course A · admin</option><option value={id(2, 2)}>Course B · other-admin</option>
        </select></label><button onClick={loadCourse}>Load administration</button>
        <p>Effective capabilities: {course?.capabilities.join(', ') ?? 'No authorized snapshot loaded'}</p>
        <label>Course display name<input value={title} disabled={!course} maxLength={100} onChange={e => setTitle(e.target.value)} /></label>
        <button onClick={rename} disabled={!course}>Save course settings</button>
        <div className="delegation"><h3>Delegate account</h3><p>Grant the synthetic <strong>delegate</strong> account access to this course.</p>
          <label className="checkbox"><input type="checkbox" checked={canRename} onChange={e => setCanRename(e.target.checked)} /> Allow settings changes (view is always included)</label>
          <button onClick={delegate}>Create delegation</button>
          <label>Delegation ID to revoke<input aria-label="Delegation ID" value={revokeId} onChange={e => setRevokeId(e.target.value)} /></label>
          <label>Expected grant version<input type="number" min={1} value={revokeVersion} onChange={e => setRevokeVersion(Number(e.target.value))} /></label>
          <button className="secondary" onClick={revoke} disabled={!revokeId}>Revoke delegation</button>
          {grant && <p className="hint">Grant: {grant.id} · {grant.state} · v{grant.version}</p>}
        </div>
        <p className="hint">Use admin on A and other-admin on B. A delegate cannot delegate again. After revocation, the delegate's existing session loses access.</p>
      </>}
    </fieldset>
    <div className="result" role="status" aria-live="polite"><h3>{busy ? 'Request in progress…' : 'Server response'}</h3><pre>{result}</pre></div>
  </section>
}

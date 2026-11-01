import { useEffect, useState } from 'react'

type DemoInfo = { personas: string[]; attempts: { id: string; label: string }[] }

export default function AccessDemo() {
  const [info, setInfo] = useState<DemoInfo | null>(null)
  const [persona, setPersona] = useState('ana')
  const [attempt, setAttempt] = useState('')
  const [session, setSession] = useState('Bez testne sesije')
  const [result, setResult] = useState('Izaberite testni identitet, zatim proverite pristup.')
  const [busy, setBusy] = useState(false)
  const [answer, setAnswer] = useState('')
  const [loadedVersion, setLoadedVersion] = useState<number | null>(null)

  useEffect(() => {
    fetch('/api/access-demo/info').then(async response => {
      if (!response.ok) return
      const data: DemoInfo = await response.json()
      setInfo(data)
      setAttempt(data.attempts[0]?.id ?? '')
    }).catch(() => setResult('API trenutno nije dostupan.'))
  }, [])

  async function run(action: 'session' | 'read' | 'update' | 'logout') {
    if (action === 'update' && loadedVersion === null) return
    setBusy(true)
    try {
      const path = action === 'session' ? `session/${persona}` :
        action === 'read' ? `attempts/${attempt}` : action === 'update' ? `attempts/${attempt}/answer` : 'logout'
      const response = await fetch(`/api/access-demo/${path}`, action === 'read' ? {} : action === 'update' ?
        { method: 'PUT', headers: { 'X-Access-Demo': '1', 'Content-Type': 'application/json' },
          body: JSON.stringify({ answer, expectedVersion: loadedVersion }) } :
        { method: 'POST', headers: { 'X-Access-Demo': '1' } })
      const text = await response.text()
      const data = text ? JSON.parse(text) : null
      if (action === 'session' && response.ok) setSession(`Testni identitet: ${persona}`)
      if (action === 'logout' && response.ok) setSession('Bez testne sesije')
      if ((action === 'read' || action === 'update') && response.ok) {
        setAnswer(data.answer)
        setLoadedVersion(data.version)
      } else {
        setAnswer('')
        setLoadedVersion(null)
      }
      setResult(`HTTP ${response.status}\n${data ? JSON.stringify(data, null, 2) : 'Testna sesija je zatvorena.'}`)
    } catch {
      setResult('Zahtev nije uspeo. Proverite da li API radi.')
    } finally { setBusy(false) }
  }

  return <section className="access-demo" aria-labelledby="access-title">
    <p className="eyebrow">ACC-04 / ACC-08 · KT1</p>
    <h2 id="access-title">Provera pristupa pokušaju</h2>
    <p>Sintetički podaci za demonstraciju. Izbor testnog identiteta simulira prijavu;
      provere vlasništva, kursa, statusa, verzije i konteksta izvršava server.</p>
    {!info ? <p>Razvojna demonstracija nije uključena. Pokrenite je prema uputstvu za ACC-04/ACC-08.</p> : <>
      <div className="access-fields">
        <label>Testni identitet<select id="access-persona" value={persona} onChange={e => setPersona(e.target.value)}>
          {info.personas.map(value => <option key={value} value={value}>{value}</option>)}
        </select></label>
        <button disabled={busy} onClick={() => void run('session')}>Postavi testni identitet</button>
      </div>
      <p role="status">{session}</p>
      <label>Pokušaj<select id="access-attempt" value={attempt} onChange={e => {
        setAttempt(e.target.value)
        setAnswer('')
        setLoadedVersion(null)
        setResult('Učitajte izabrani pokušaj da proverite pristup.')
      }}>
        {info.attempts.map(value => <option key={value.id} value={value.id}>{value.label}</option>)}
      </select></label>
      <div className="access-actions">
        <button disabled={busy} onClick={() => void run('read')}>Proveri pristup</button>
        <button className="secondary" disabled={busy} onClick={() => void run('logout')}>Zatvori testnu sesiju</button>
      </div>
      <div className="access-editor">
        <label>Sadržaj nacrta<textarea id="access-answer" rows={4} maxLength={4000}
          value={answer} onChange={e => setAnswer(e.target.value)} /></label>
        <p>Učitana verzija: {loadedVersion ?? '—'}. Izmena je dozvoljena samo aktivnom studentu
          za sopstveni nacrt, uz važeći kontekst.</p>
        <button disabled={busy || loadedVersion === null || !answer.trim()}
          onClick={() => void run('update')}>Sačuvaj nacrt</button>
      </div>
      <pre aria-live="polite" aria-label="Odgovor servera">{result}</pre>
      <p className="access-note">Probajte: Ana čita svoj pokušaj; Marko dobija odbijanje za isti ID;
        Ana dobija odbijanje i za svoj pokušaj kada je vremenski kontekst istekao.
        Za drugu operaciju učitajte Anin nepredati pokušaj, promenite sadržaj i sačuvajte nacrt.
        Predati i završeni pokušaji ostaju neizmenjivi.</p>
    </>}
  </section>
}

import { useState } from 'react'
import type { FormEvent } from 'react'
import { getLabContent, LANGUAGES, USE_FAKE_LAB_CONTENT } from '../../api/labs'
import type { Language, LabContent, TestUser } from '../../api/labs'
import { DEMO_LAB_ID, DEMO_XSS_LAB_ID } from '../../api/fakeLabContent'
import { failureLines, requestIdOf, statusOf } from './messages'
import ResultMessage from './ResultMessage'

interface Props {
  user: TestUser
}

const LANGUAGE_NAMES: Record<Language, string> = { sr: 'srpski', en: 'engleski' }

const DEMO_LABS = [
  { id: DEMO_LAB_ID, label: 'Primer: veb server (sr i en)' },
  { id: DEMO_XSS_LAB_ID, label: 'Primer: tekst sa HTML-om (samo sr)' },
]

export default function LabView({ user }: Props) {
  const [labId, setLabId] = useState(DEMO_LAB_ID)
  const [language, setLanguage] = useState<Language>('sr')
  const [loading, setLoading] = useState(false)
  const [content, setContent] = useState<LabContent | null>(null)
  const [fallbackFrom, setFallbackFrom] = useState<Language | null>(null)
  const [failure, setFailure] = useState<{ lines: string[]; status?: number; requestId?: string } | null>(null)

  async function load(id: string, lang: Language) {
    if (id.trim().length === 0) {
      setContent(null)
      setFailure({ lines: ['Unesite ID laboratorije.'] })
      return
    }
    setLoading(true)
    setFailure(null)
    const outcome = await getLabContent(id.trim(), lang, user)
    setLoading(false)

    if (outcome.kind === 'ok') {
      setContent(outcome.content)
      setFallbackFrom(outcome.usedFallback ? lang : null)
      return
    }
    // Posle odbijanja ne ostaje prikazan sadržaj iz prethodnog zahteva.
    setContent(null)
    setFallbackFrom(null)
    setFailure({ lines: failureLines(outcome), status: statusOf(outcome), requestId: requestIdOf(outcome) })
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    void load(labId, language)
  }

  function changeLanguage(next: Language) {
    setLanguage(next)
    // Ako je laboratorija već prikazana, odmah se učitava ista laboratorija na drugom jeziku.
    if (content) void load(labId, next)
  }

  function openDemo(id: string) {
    setLabId(id)
    void load(id, language)
  }

  return (
    <div className="labs-view">
      <form className="labs-form" onSubmit={handleSubmit} noValidate aria-busy={loading} aria-labelledby="labs-view-heading">
        <div className="labs-panel-head">
          <h3 id="labs-view-heading">Prikaz laboratorije</h3>
          <span className="labs-tag">LAB-07</span>
        </div>
        {USE_FAKE_LAB_CONTENT && (
          <p className="hint">
            <span className="badge-fake">FAKE</span> Lažni podaci dok se LAB-07 backend ne poveže sa API-jem.
          </p>
        )}

        {USE_FAKE_LAB_CONTENT && (
          <div className="labs-demos" role="group" aria-label="Primeri laboratorija">
            {DEMO_LABS.map(d => (
              <button key={d.id} type="button" className="labs-chip" onClick={() => openDemo(d.id)} disabled={loading}>
                {d.label}
              </button>
            ))}
          </div>
        )}

        <div className="labs-field">
          <label htmlFor="labs-id">ID laboratorije</label>
          <input
            id="labs-id"
            type="text"
            value={labId}
            maxLength={64}
            spellCheck={false}
            autoComplete="off"
            onChange={e => setLabId(e.target.value)}
            required
          />
        </div>

        <fieldset className="labs-languages">
          <legend>Jezik</legend>
          <div className="labs-segment">
            {LANGUAGES.map(l => (
              <label key={l.code} className="labs-segment-option">
                <input
                  type="radio"
                  name="labs-language"
                  value={l.code}
                  checked={language === l.code}
                  onChange={() => changeLanguage(l.code)}
                />
                <span lang={l.code}>{l.label}</span>
              </label>
            ))}
          </div>
        </fieldset>

        <button type="submit" disabled={loading}>{loading ? 'Učitavanje…' : 'Prikaži laboratoriju'}</button>
      </form>

      {failure && <ResultMessage tone="error" lines={failure.lines} status={failure.status} requestId={failure.requestId} />}

      {content && (
        <article className="labs-content" lang={content.language} aria-labelledby="labs-content-title">
          {fallbackFrom && (
            <p role="status" className="labs-fallback" lang="sr">
              Prevod na jezik „{LANGUAGE_NAMES[fallbackFrom]}” ne postoji. Prikazan je podrazumevani tekst na srpskom.
            </p>
          )}
          <p className="labs-lang" lang="sr">
            <span className="labs-sr-only">Jezik prikaza: </span>{content.language.toUpperCase()}
          </p>
          {/* Tekst se uvek prikazuje kao TEKST: React ga sam escapuje, nigde nema dangerouslySetInnerHTML. */}
          <h4 id="labs-content-title">{content.title}</h4>
          <p className="labs-text">{content.description}</p>
          <h5 lang="sr">Uputstvo</h5>
          <p className="labs-text labs-instructions">{content.instructions}</p>
          <dl className="labs-meta" lang="sr">
            <div>
              <dt>Revizija</dt>
              <dd><code>{content.revisionId}</code></dd>
            </div>
            <div>
              <dt>Evaluator</dt>
              <dd>{content.evaluatorVersion}</dd>
            </div>
          </dl>
          <p className="hint" lang="sr">
            LAB-07: na svakom jeziku su ista revizija i isti evaluator, menja se samo tekst.
          </p>
        </article>
      )}
    </div>
  )
}

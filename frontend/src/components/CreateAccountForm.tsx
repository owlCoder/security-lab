import { useState } from 'react'
import type { FormEvent } from 'react'
import { createAccount } from '../api/accounts'
import type { CreateAccountOutcome } from '../api/accounts'

const MIN_PASSWORD_LENGTH = 12
const MAX_PASSWORD_LENGTH = 128
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

function messageFor(outcome: CreateAccountOutcome): { ok: boolean; lines: string[] } {
  switch (outcome.kind) {
    case 'created':
      return { ok: true, lines: ['Nalog je kreiran. Poslata je poruka za potvrdu email adrese pre prve prijave.'] }
    case 'taken':
      return { ok: false, lines: ['Nalog sa ovom email adresom već postoji.'] }
    case 'invalid':
      return { ok: false, lines: outcome.errors }
    case 'unauthenticated':
      return { ok: false, lines: ['Niste prijavljeni.'] }
    case 'forbidden':
      return { ok: false, lines: ['Nemate dozvolu za kreiranje naloga.'] }
    case 'confirmationUnavailable':
      return {
        ok: false,
        lines: ['Nalog je kreiran, ali email za potvrdu nije poslat. Otvorite stranicu za potvrdu email adrese i zatražite novu poruku.'],
      }
    default:
      return { ok: false, lines: ['Došlo je do greške. Pokušajte ponovo.'] }
  }
}

export default function CreateAccountForm() {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [result, setResult] = useState<{ ok: boolean; lines: string[]; correlationId?: string } | null>(null)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    // Samo korisnička pomoć: server je jedini koji stvarno validira.
    const trimmed = email.trim()
    const problems: string[] = []
    if (!EMAIL_PATTERN.test(trimmed)) problems.push('Unesite ispravnu email adresu.')
    if (password.length < MIN_PASSWORD_LENGTH) {
      problems.push(`Lozinka mora imati najmanje ${MIN_PASSWORD_LENGTH} karaktera.`)
    }
    if (problems.length > 0) {
      setResult({ ok: false, lines: problems })
      return
    }

    setSubmitting(true)
    setResult(null)
    const outcome = await createAccount(trimmed, password)
    setPassword('') // lozinka se ne zadržava u stanju posle slanja
    if (outcome.kind === 'created') setEmail('')
    setResult({
      ...messageFor(outcome),
      correlationId: 'correlationId' in outcome ? outcome.correlationId : undefined,
    })
    setSubmitting(false)
  }

  return (
    <form onSubmit={handleSubmit} noValidate aria-busy={submitting}>
      <label htmlFor="account-email">Email adresa</label>
      <input
        id="account-email"
        type="email"
        autoComplete="off"
        maxLength={254}
        value={email}
        onChange={e => setEmail(e.target.value)}
        required
      />

      <label htmlFor="account-password">Privremena lozinka</label>
      <input
        id="account-password"
        type="password"
        autoComplete="new-password"
        maxLength={MAX_PASSWORD_LENGTH}
        value={password}
        onChange={e => setPassword(e.target.value)}
        aria-describedby="account-password-hint"
        required
      />
      <p id="account-password-hint" className="hint">
        Najmanje {MIN_PASSWORD_LENGTH} karaktera, sa velikim i malim slovom, brojem i specijalnim znakom.
      </p>

      <button type="submit" disabled={submitting}>
        {submitting ? 'Kreiranje…' : 'Kreiraj nalog'}
      </button>

      {result && (
        <div role="status" className={result.ok ? 'result ok' : 'result error'}>
          {result.lines.map(line => <p key={line}>{line}</p>)}
          {result.correlationId && <p className="hint">ID zahteva: {result.correlationId}</p>}
        </div>
      )}
    </form>
  )
}
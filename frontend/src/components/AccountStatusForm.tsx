import { useState } from 'react'
import type { FormEvent } from 'react'
import { ACCOUNT_STATUSES, changeAccountStatus } from '../api/accounts'
import type { AccountStatus, ChangeAccountStatusOutcome } from '../api/accounts'

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

const STATUS_LABELS: Record<AccountStatus, string> = {
  ACTIVE: 'Aktivan',
  BLOCKED: 'Blokiran',
  ARCHIVED: 'Arhiviran',
}

function messageFor(outcome: ChangeAccountStatusOutcome): { ok: boolean; text: string } {
  switch (outcome.kind) {
    case 'changed':
      return { ok: true, text: `Status naloga je promenjen u: ${STATUS_LABELS[outcome.status]}.` }
    case 'not_found':
      return { ok: false, text: 'Nalog ne postoji.' }
    case 'invalid_status':
      return { ok: false, text: 'Status nije ispravan.' }
    case 'invalid_transition':
      return { ok: false, text: 'Nalog već ima izabrani status.' }
    case 'self_change':
      return { ok: false, text: 'Administrator ne može da menja status sopstvenog naloga.' }
    case 'unauthenticated':
      return { ok: false, text: 'Niste prijavljeni.' }
    case 'forbidden':
      return { ok: false, text: 'Nemate dozvolu za promenu statusa naloga.' }
    case 'throttled':
      return { ok: false, text: `Previše zahteva. Pokušajte ponovo za ${outcome.retryAfterSeconds} s.` }
    case 'unavailable':
      return { ok: false, text: 'Promena nije sačuvana jer je servis trenutno nedostupan. Pokušajte ponovo.' }
    default:
      return { ok: false, text: 'Došlo je do greške. Pokušajte ponovo.' }
  }
}

// IDN-03 / UC-05: there is no account list endpoint yet, so the administrator enters the account id.
export default function AccountStatusForm() {
  const [accountId, setAccountId] = useState('')
  const [status, setStatus] = useState<AccountStatus>('BLOCKED')
  const [submitting, setSubmitting] = useState(false)
  const [result, setResult] = useState<{ ok: boolean; text: string } | null>(null)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const id = accountId.trim()
    if (!GUID_PATTERN.test(id)) {
      setResult({ ok: false, text: 'Unesite ispravan ID naloga (GUID).' })
      return
    }

    setSubmitting(true)
    setResult(null)
    const outcome = await changeAccountStatus(id, status)
    setResult(messageFor(outcome))
    setSubmitting(false)
  }

  return (
    <form onSubmit={handleSubmit} noValidate aria-busy={submitting}>
      <label htmlFor="status-account-id">ID naloga</label>
      <input
        id="status-account-id"
        type="text"
        autoComplete="off"
        maxLength={36}
        value={accountId}
        onChange={e => setAccountId(e.target.value)}
        required
      />

      <label htmlFor="status-new-status">Novi status</label>
      <select id="status-new-status" value={status} onChange={e => setStatus(e.target.value as AccountStatus)}>
        {ACCOUNT_STATUSES.map(option => <option key={option} value={option}>{STATUS_LABELS[option]}</option>)}
      </select>
      <p className="hint">Blokiranje i arhiviranje odmah opozivaju sve sesije naloga.</p>

      <button type="submit" disabled={submitting}>
        {submitting ? 'Promena…' : 'Promeni status'}
      </button>

      {result && (
        <div role="status" className={result.ok ? 'result ok' : 'result error'}>
          <p>{result.text}</p>
        </div>
      )}
    </form>
  )
}

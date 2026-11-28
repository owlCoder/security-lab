import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { apiFetch } from '../auth/apiClient'

type ConfirmationState = 'loading' | 'confirmed' | 'invalid' | 'missing' | 'unavailable'

export default function ConfirmEmailPage() {
  const [state, setState] = useState<ConfirmationState>('loading')
  const [email, setEmail] = useState('')
  const [requestMessage, setRequestMessage] = useState('')
  const [requesting, setRequesting] = useState(false)

  useEffect(() => {
    const token = window.location.hash.slice(1)
    window.history.replaceState(null, '', `${window.location.pathname}${window.location.search}`)

    if (!token) {
      setState('missing')
      return
    }

    let active = true
    void apiFetch('/api/auth/email-confirmation/confirm', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ token }),
    }).then(response => {
      if (active) setState(response.ok ? 'confirmed' : response.status === 400 ? 'invalid' : 'unavailable')
    }).catch(() => {
      if (active) setState('unavailable')
    })

    return () => { active = false }
  }, [])

  async function requestConfirmation(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setRequesting(true)
    setRequestMessage('')
    try {
      const response = await apiFetch('/api/auth/email-confirmation/request', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email }),
      })
      if (!response.ok) throw new Error('Request failed')
      setRequestMessage('Ako nalogu treba potvrda, email će biti poslat.')
    } catch {
      setRequestMessage('Zahtev nije uspeo. Pokušajte ponovo kasnije.')
    } finally {
      setRequesting(false)
    }
  }

  const messages: Record<ConfirmationState, string> = {
    loading: 'Potvrda email adrese je u toku…',
    confirmed: 'Email adresa je potvrđena. Sada možete da se prijavite.',
    invalid: 'Link za potvrdu je nevažeći, istekao ili je već iskorišćen.',
    missing: 'Link ne sadrži token za potvrdu.',
    unavailable: 'Potvrda trenutno nije dostupna. Pokušajte ponovo kasnije.',
  }

  return (
    <section className="card" aria-live="polite">
      <h1>Potvrda email adrese</h1>
      <p>{messages[state]}</p>
      {state !== 'loading' && state !== 'confirmed' && (
        <form onSubmit={requestConfirmation}>
          <label htmlFor="confirmation-email">Email adresa</label>
          <input
            id="confirmation-email"
            type="email"
            maxLength={254}
            autoComplete="email"
            value={email}
            onChange={event => setEmail(event.target.value)}
            required
          />
          <button type="submit" disabled={requesting}>
            {requesting ? 'Slanje…' : 'Pošalji novi email za potvrdu'}
          </button>
          {requestMessage && <p role="status">{requestMessage}</p>}
        </form>
      )}
    </section>
  )
}

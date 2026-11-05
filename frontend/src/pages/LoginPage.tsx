import { useState, type FormEvent } from 'react'
import { INVALID_CREDENTIALS_MESSAGE, login } from '../api/auth'
import { formatSeconds, useCountdown } from '../hooks/useCountdown'

type Props = { onLoggedIn: () => void }

export default function LoginPage({ onLoggedIn }: Props) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [blockedUntil, setBlockedUntil] = useState<number | null>(null)
  const remaining = useCountdown(blockedUntil)
  const throttled = remaining > 0

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    if (submitting || throttled) return
    setSubmitting(true)
    setError(null)

    const result = await login(email, password)
    setSubmitting(false)
    setPassword('')

    switch (result.kind) {
      case 'success':
        setBlockedUntil(null)
        onLoggedIn()
        return
      case 'invalid':
        setError(INVALID_CREDENTIALS_MESSAGE)
        return
      case 'throttled':
        setBlockedUntil(Date.now() + result.retryAfterSeconds * 1000)
        return
      case 'error':
        setError(result.message)
        return
    }
  }

  return (
    <section className="card login">
      <h2>Prijava</h2>
      <form onSubmit={handleSubmit} noValidate>
        <label>
          Imejl
          <input
            type="email"
            autoComplete="username"
            value={email}
            onChange={e => setEmail(e.target.value)}
            required
          />
        </label>
        <label>
          Lozinka
          <input
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={e => setPassword(e.target.value)}
            required
          />
        </label>

        {throttled && (
          <p className="alert alert-warn" role="alert" data-testid="throttle-message">
            Previše neuspešnih pokušaja prijave. Pokušajte kasnije
            (ponovo za <strong>{formatSeconds(remaining)}</strong>).
          </p>
        )}
        {!throttled && error && (
          <p className="alert alert-error" role="alert">{error}</p>
        )}

        <button type="submit" disabled={submitting || throttled || !email.trim() || !password}>
          {submitting ? 'Prijavljivanje…' : throttled ? `Sačekajte ${formatSeconds(remaining)}` : 'Prijavi se'}
        </button>
      </form>
    </section>
  )
}

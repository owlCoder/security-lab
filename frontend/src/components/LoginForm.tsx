import { SubmitEvent, useRef, useState } from 'react';
import { useAuth } from '../auth/AuthContext'
import { parseRetryAfter } from '../api/http'
import { formatSeconds, useCountdown } from '../hooks/useCountdown'

type Props = {
  onSuccess: () => boolean
}

const GENERIC_ERROR = 'Incorrect email or password.'
// US-15: login answers 403 only when the CSRF check refused it, before the password is read, even after
// csrfFetch fetched a new token and retried once. It says nothing about the account.
const CSRF_ERROR = 'Security check failed. Refresh the page and try again.'

export default function LoginForm({ onSuccess }: Props) {
  const { request } = useAuth()
  const [email, setEmail] = useState('')
	const [password, setPassword] = useState('')
	const [error, setError] = useState('')
	const [loading, setLoading] = useState(false)
	const inFlight = useRef(false)
	// IDN-05: posle 429 forma ceka Retry-After. Ovo je samo UX; limit sprovodi server.
	const [blockedUntil, setBlockedUntil] = useState<number | null>(null)
	const remaining = useCountdown(blockedUntil)
	const throttled = remaining > 0

	const handleSubmit = async (e: SubmitEvent<HTMLFormElement>): Promise<boolean> => {
		e.preventDefault()
		if(inFlight.current || throttled) return false
		inFlight.current = true
		setLoading(true)
		setError('')

		const result = await request('/api/auth/login', {
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify({ email, password })
		})
      	.then(r => ({ ok: r.ok, status: r.status, retryAfter: r.headers.get('Retry-After') }))
      	.catch(() => ({ ok: false, status: 0, retryAfter: null }))

		inFlight.current = false
		setLoading(false)
		if(result.ok) return onSuccess()
		if(result.status === 429) {
			setBlockedUntil(Date.now() + parseRetryAfter(result.retryAfter) * 1000)
			return false
		}
		setError(result.status === 403 ? CSRF_ERROR : GENERIC_ERROR)
		return false
	}

	return <form className="login-form" onSubmit={handleSubmit}>
		<div className="login-field">
			<label htmlFor="login-email">E-mail:</label>
			<input id="login-email" type="email" required
				value={email} onChange={e => setEmail(e.target.value)} />
		</div>

		<div className="login-field">
			<label htmlFor="login-password">Password:</label>
			<input id="login-password" type="password" required
				value={password} onChange={e => setPassword(e.target.value)} />
		</div>

		{throttled && <p className="login-error" role="alert" data-testid="throttle-message">
			Too many login attempts. Please try again later (in <strong>{formatSeconds(remaining)}</strong>).
		</p>}
		{!throttled && error !== '' && <p className="login-error" role="alert">{error}</p>}

		<button type="submit" disabled={loading || throttled}>
			{loading ? "Logging in..." : throttled ? `Wait ${formatSeconds(remaining)}` : "Log in"}
		</button>

	</form>
}
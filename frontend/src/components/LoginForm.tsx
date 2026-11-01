import { SubmitEvent, useRef, useState } from 'react';

type Props = {
  onSuccess: () => boolean
}

const GENERIC_ERROR = 'Incorrect email or password.'

export default function LoginForm({ onSuccess }: Props) {
  const [email, setEmail] = useState('')
	const [password, setPassword] = useState('')
	const [error, setError] = useState('')
	const [loading, setLoading] = useState(false)
	const inFlight = useRef(false)

	const handleSubmit = async (e: SubmitEvent<HTMLFormElement>): Promise<boolean> => {
		e.preventDefault()
		if(inFlight.current) return false
		inFlight.current = true
		setLoading(true)
		setError('')

		const ok = await fetch('/api/auth/login', {
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify({ email, password })
		})
      	.then(r => r.ok)
      	.catch(() => false)

		inFlight.current = false
		setLoading(false)
		if(ok) return onSuccess()
		setError(GENERIC_ERROR)
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

		{error !== '' && <p className="login-error" role="alert">{error}</p>}

		<button type="submit" disabled={loading}>
			{loading ? "Logging in..." : "Log in"}
		</button>

	</form>
}
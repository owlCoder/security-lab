// IDN-05 UI: poruka "pokusajte kasnije" na formi za prijavu (components/LoginForm.tsx).
// Odgovor 429 je isti kao iz IdentityRateLimitingExtensions.OnRejected: Retry-After u sekundama
// i telo { code: "rate_limited", message }.
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthProvider } from '../auth/AuthContext'
import LoginForm from '../components/LoginForm'

type Handler = (url: string, init?: RequestInit) => Response
let loginCalls = 0

function backend(onLogin: Handler) {
  loginCalls = 0
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url === '/api/auth/me') return new Response(null, { status: 401 })
    if (url === '/api/auth/login') { loginCalls++; return onLogin(url, init) }
    return new Response(null, { status: 404 })
  }))
}

const rateLimited = (seconds: string) => new Response(
  JSON.stringify({ code: 'rate_limited', message: 'Previse pokusaja. Pokusajte ponovo kasnije.' }),
  { status: 429, headers: { 'Retry-After': seconds, 'Content-Type': 'application/json' } })

async function renderForm(onSuccess = () => true) {
  render(<AuthProvider><LoginForm onSuccess={onSuccess} /></AuthProvider>)
  await act(async () => {})
}

async function submit() {
  fireEvent.change(screen.getByLabelText('E-mail:'), { target: { value: 'student@example.com' } })
  fireEvent.change(screen.getByLabelText('Password:'), { target: { value: 'x' } })
  await act(async () => { fireEvent.submit(screen.getByRole('button').closest('form')!) })
}

describe('LoginForm (IDN-05 UI)', () => {
  beforeEach(() => { vi.useRealTimers() })
  afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.useRealTimers() })

  it('401 i dalje prikazuje genericku poruku', async () => {
    backend(() => new Response(JSON.stringify({ code: 'invalid_credentials' }), { status: 401 }))
    await renderForm()
    await submit()
    expect(screen.getByRole('alert').textContent).toBe('Incorrect email or password.')
    expect(screen.queryByTestId('throttle-message')).toBeNull()
  })

  it('429 prikazuje "try again later" sa odbrojavanjem i blokira dugme', async () => {
    backend(() => rateLimited('30'))
    await renderForm()
    await submit()
    const message = screen.getByTestId('throttle-message').textContent ?? ''
    expect(message).toContain('try again later')
    expect(message).toMatch(/30 s|29 s/)
    expect((screen.getByRole('button') as HTMLButtonElement).disabled).toBe(true)
  })

  it('dok traje blokada forma ne salje nove zahteve ni preko Enter-a', async () => {
    backend(() => rateLimited('30'))
    await renderForm()
    await submit()
    await submit()
    expect(loginCalls).toBe(1)
  })

  it('posle isteka Retry-After forma ponovo radi', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    backend(() => rateLimited('2'))
    await renderForm()
    await submit()
    expect(screen.queryByTestId('throttle-message')).not.toBeNull()
    await act(async () => { vi.advanceTimersByTime(2500) })
    expect(screen.queryByTestId('throttle-message')).toBeNull()
    expect((screen.getByRole('button') as HTMLButtonElement).disabled).toBe(false)
  })

  it('429 bez Retry-After headera koristi bezbednu podrazumevanu vrednost (60 s)', async () => {
    backend(() => new Response('{}', { status: 429 }))
    await renderForm()
    await submit()
    expect(screen.getByTestId('throttle-message').textContent).toMatch(/1 min|59 s/)
  })

  it('uspesna prijava poziva onSuccess', async () => {
    backend(() => new Response(JSON.stringify({ userId: '00000000-0000-0000-0000-000000000001' }), { status: 200 }))
    const onSuccess = vi.fn(() => true)
    await renderForm(onSuccess)
    await submit()
    expect(onSuccess).toHaveBeenCalledOnce()
  })
})
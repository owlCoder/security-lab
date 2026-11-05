import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { session, setFetcher } from '../api/http'
import LoginPage from '../pages/LoginPage'

function respond(status: number, body: unknown, headers: Record<string, string> = {}) {
  return vi.fn(async () => new Response(JSON.stringify(body), { status, headers }))
}

async function submit(email = 'student@example.com', password = 'x') {
  fireEvent.change(screen.getByLabelText('Imejl'), { target: { value: email } })
  fireEvent.change(screen.getByLabelText('Lozinka'), { target: { value: password } })
  await act(async () => { fireEvent.click(screen.getByRole('button')) })
}

describe('LoginPage (IDN-05 UI)', () => {
  beforeEach(() => session.set(null))
  afterEach(() => { cleanup(); vi.useRealTimers() })

  it('401 prikazuje genericku poruku i brise lozinku', async () => {
    setFetcher(respond(401, { code: 'invalid_credentials' }))
    render(<LoginPage onLoggedIn={() => {}} />)
    await submit()
    expect(screen.getByRole('alert').textContent).toBe('Pogrešan imejl ili lozinka.')
    expect((screen.getByLabelText('Lozinka') as HTMLInputElement).value).toBe('')
  })

  it('429 prikazuje "pokusajte kasnije", onemogucava dugme i ne salje nove zahteve', async () => {
    const fetcher = respond(429, {}, { 'Retry-After': '30' })
    setFetcher(fetcher)
    render(<LoginPage onLoggedIn={() => {}} />)
    await submit()

    expect(screen.getByTestId('throttle-message').textContent).toContain('Pokušajte kasnije')
    expect(screen.getByTestId('throttle-message').textContent).toMatch(/30 s|29 s/)
    const button = screen.getByRole('button') as HTMLButtonElement
    expect(button.disabled).toBe(true)

    fireEvent.change(screen.getByLabelText('Lozinka'), { target: { value: 'y' } })
    await act(async () => { fireEvent.submit(button.closest('form')!) })
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

  it('posle isteka Retry-After forma ponovo radi', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    setFetcher(respond(429, {}, { 'Retry-After': '2' }))
    render(<LoginPage onLoggedIn={() => {}} />)
    await submit()
    expect(screen.queryByTestId('throttle-message')).not.toBeNull()

    await act(async () => { vi.advanceTimersByTime(2500) })
    expect(screen.queryByTestId('throttle-message')).toBeNull()
    fireEvent.change(screen.getByLabelText('Lozinka'), { target: { value: 'y' } })
    expect((screen.getByRole('button') as HTMLButtonElement).disabled).toBe(false)
  })

  it('uspesna prijava cuva token u memoriji i zove onLoggedIn', async () => {
    setFetcher(respond(200, { tokenType: 'Bearer', accessToken: 'tok' }))
    const onLoggedIn = vi.fn()
    render(<LoginPage onLoggedIn={onLoggedIn} />)
    await submit()
    expect(onLoggedIn).toHaveBeenCalledOnce()
    expect(session.token).toBe('tok')
  })

  it('403 blokiran nalog prikazuje poruku servera', async () => {
    setFetcher(respond(403, { code: 'account_blocked', message: 'Nalog je blokiran.' }))
    render(<LoginPage onLoggedIn={() => {}} />)
    await submit()
    expect(screen.getByRole('alert').textContent).toBe('Nalog je blokiran.')
  })

  it('mrezna greska ne rusi stranicu', async () => {
    setFetcher(vi.fn(async () => { throw new TypeError('fail') }))
    render(<LoginPage onLoggedIn={() => {}} />)
    await submit()
    expect(screen.getByRole('alert').textContent).toContain('Server nije dostupan')
  })
})

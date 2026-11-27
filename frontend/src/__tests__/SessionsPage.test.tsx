import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import App from '../App'

const user = { userId: '10000000-0000-0000-0000-000000000001', level: 'Active' }
const first = { handle: 'a0000000-0000-0000-0000-000000000001', createdAt: '2026-11-23T08:00:00Z', lastActivityAt: '2026-11-23T09:00:00Z', expiresAt: '2026-11-23T16:00:00Z' }
const second = { ...first, handle: 'a0000000-0000-0000-0000-000000000002' }

const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status })

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

function openPage() {
  render(<MemoryRouter initialEntries={['/account/sessions']}><App /></MemoryRouter>)
}

it('lists the sessions and reloads the list after a revoke', async () => {
  let sessions = [first, second]
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    if (url === '/api/auth/me') return json(200, user)
    if (url === '/api/auth/sessions') return json(200, sessions)
    if (url === `/api/auth/sessions/${first.handle}` && init?.method === 'DELETE') {
      sessions = [second]
      return new Response(null, { status: 204 })
    }
    throw new Error(`Unexpected request: ${url}`)
  })
  vi.stubGlobal('fetch', fetchMock)
  openPage()

  await screen.findByRole('heading', { name: 'Moje sesije' })
  const buttons = await screen.findAllByRole('button', { name: 'Opozovi' })
  expect(buttons).toHaveLength(2)

  fireEvent.click(buttons[0])
  await vi.waitFor(() => expect(screen.getAllByRole('button', { name: 'Opozovi' })).toHaveLength(1))
})

it('sends the user to the login page when the session is no longer valid', async () => {
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input)
    if (url === '/api/auth/me') return json(200, user)
    if (url === '/api/auth/sessions') return json(401, { code: 'session_invalid' })
    throw new Error(`Unexpected request: ${url}`)
  }))
  openPage()

  await screen.findByRole('heading', { name: 'Log in' })
  expect(screen.queryByRole('heading', { name: 'Moje sesije' })).toBeNull()
})

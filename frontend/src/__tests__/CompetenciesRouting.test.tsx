import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { MemoryRouter, useLocation } from 'react-router-dom'
import App from '../App'

const laboratoryId = '30000000-0000-0000-0000-000000000001'
const path = `/laboratories/${laboratoryId}/competencies`
const user = { userId: '10000000-0000-0000-0000-000000000001', level: 'Active' }

function LocationProbe() {
  const location = useLocation()
  return <output aria-label="Trenutna putanja">{location.pathname}</output>
}

function server(initiallyAuthenticated: boolean) {
  let authenticated = initiallyAuthenticated
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status })
    if (url === '/api/auth/me') return json(authenticated ? 200 : 401, authenticated ? user : {})
    if (url === '/api/auth/login' && init?.method === 'POST') { authenticated = true; return json(200, {}) }
    if (url === '/api/auth/logout' && init?.method === 'POST') { authenticated = false; return json(200, {}) }
    if (url === `/api/laboratories/${laboratoryId}/competencies`) return json(200, {
      laboratoryId, laboratoryTitle: 'Laboratorija za proveru rutiranja', canManage: false, competencies: [],
    })
    throw new Error(`Unexpected request: ${url}`)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function openPage() {
  render(<MemoryRouter initialEntries={[path]}><App /><LocationProbe /></MemoryRouter>)
}

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

it('requires a real session and returns to the requested competency page after login', async () => {
  const fetchMock = server(false)
  openPage()
  await screen.findByRole('heading', { name: 'Log in' })
  expect(fetchMock.mock.calls.some(([url]) => String(url).includes('/competencies'))).toBe(false)
  fireEvent.change(screen.getByLabelText('E-mail:'), { target: { value: 'instructor@example.test' } })
  fireEvent.change(screen.getByLabelText('Password:'), { target: { value: 'test-password' } })
  fireEvent.click(screen.getByRole('button', { name: 'Log in' }))
  await screen.findByRole('heading', { name: 'Laboratorija za proveru rutiranja' })
  expect(screen.getByLabelText('Trenutna putanja').textContent).toBe(path)
})

it('removes the protected competency page after logout', async () => {
  server(true)
  openPage()
  await screen.findByRole('heading', { name: 'Laboratorija za proveru rutiranja' })
  fireEvent.click(screen.getByRole('button', { name: 'Log out' }))
  await screen.findByRole('heading', { name: 'Log in' })
  expect(screen.queryByRole('heading', { name: 'Laboratorija za proveru rutiranja' })).toBeNull()
  expect(screen.getByLabelText('Trenutna putanja').textContent).toBe('/login')
})

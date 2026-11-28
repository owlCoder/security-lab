import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import DraftsRoute from '../pages/DraftsRoute'

const { request } = vi.hoisted(() => ({ request: vi.fn() }))

vi.mock('../auth/AuthContext', () => ({
  useAuth: () => ({ request }),
}))

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

beforeEach(() => {
  request.mockReset()
})

afterEach(() => {
  cleanup()
  vi.unstubAllEnvs()
})

describe('AUT-01 DraftsRoute: izbor izvora podataka', () => {
  it('bez VITE_DRAFTS_API je demo i ne zove server', async () => {
    render(<DraftsRoute />)

    await screen.findByText(/nemate nijedan nacrt/i)

    expect(screen.getByRole('status').textContent).toMatch(/ne čuvaju/i)
    expect(request).not.toHaveBeenCalled()
  })

  it('sa VITE_DRAFTS_API=true učitava nacrte preko zajedničkog zahteva sesije', async () => {
    vi.stubEnv('VITE_DRAFTS_API', 'true')
    request.mockImplementation(async () => jsonResponse({
      drafts: [{
        id: 'srv-1', ownerId: 'o1', status: 'Draft', version: 1,
        title: 'Sa servera', goal: '', type: 'LogAnalysis', correlationId: 'c',
      }],
      correlationId: 'c',
    }))

    render(<DraftsRoute />)

    expect(await screen.findByText('Sa servera')).not.toBeNull()
    expect(screen.queryByRole('status')).toBeNull()
    expect(request).toHaveBeenCalledWith('/api/authoring/drafts', expect.objectContaining({ method: 'GET' }))
  })

  it('sa VITE_DRAFTS_API=true kreira nacrt sa tri polja i ponovo učitava listu', async () => {
    vi.stubEnv('VITE_DRAFTS_API', 'true')
    let stored: unknown[] = []
    request.mockImplementation(async (_input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'POST') {
        const created = {
          id: 'srv-1', ownerId: 'o1', status: 'Draft', version: 1, correlationId: 'c',
          ...JSON.parse(String(init.body)),
        }
        stored = [created]
        return jsonResponse(created, 201)
      }
      return jsonResponse({ drafts: stored, correlationId: 'c' })
    })

    render(<DraftsRoute />)
    await screen.findByText(/nemate nijedan nacrt/i)

    fireEvent.click(screen.getByRole('button', { name: 'Novi nacrt' }))
    fireEvent.change(screen.getByLabelText('Naziv'), { target: { value: 'Nov nacrt' } })
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Kreiraj nacrt' }))
    })

    expect(await screen.findByText('Nov nacrt')).not.toBeNull()

    const calls = request.mock.calls as [string, RequestInit][]
    const posts = calls.filter(([, init]) => init?.method === 'POST')
    const gets = calls.filter(([, init]) => init?.method === 'GET')
    expect(posts).toHaveLength(1)
    expect(JSON.parse(String(posts[0][1].body))).toEqual({
      title: 'Nov nacrt', goal: '', type: 'AccessControl',
    })
    expect(gets).toHaveLength(2)
  })
})

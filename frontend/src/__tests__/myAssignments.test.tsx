import { act, cleanup, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { session, setFetcher } from '../api/http'
import { getMyAssignments, MY_ASSIGNMENTS_PATH, type MyAssignment } from '../api/myAssignments'
import MyAssignmentsPage from '../pages/MyAssignmentsPage'

// ASN-02 (team 16): frontend for GET /api/me/assignments.

const oib: MyAssignment = {
  assignmentId: 'a0000000-0000-0000-0000-000000000001',
  courseId: 'course-oib',
  publishedLabRevisionId: 'rev-sql-injection-1',
  availableFromUtc: null,
  dueAtUtc: '2027-01-31T22:59:00+00:00',
  maxAttempts: 3,
}
const security: MyAssignment = {
  ...oib,
  assignmentId: 'a0000000-0000-0000-0000-000000000002',
  courseId: 'course-security',
  publishedLabRevisionId: 'rev-xss-1',
  availableFromUtc: '2026-11-01T08:00:00+00:00',
  maxAttempts: 2,
}

type Call = { url: string; init?: RequestInit }
function server(status: number, body: unknown) {
  const calls: Call[] = []
  setFetcher(async (url, init) => {
    calls.push({ url, init })
    return new Response(JSON.stringify(body), { status })
  })
  return calls
}
const ok = (assignments: unknown[]) => server(200, { assignments, correlationId: 'c1' })

async function renderPage() {
  let view!: ReturnType<typeof render>
  await act(async () => {
    view = render(<MemoryRouter><MyAssignmentsPage /></MemoryRouter>)
  })
  return view
}

beforeEach(() => session.set(null))
afterEach(cleanup)

describe('getMyAssignments (API contract)', () => {
  it('sends GET to /api/me/assignments without a body or a student ID', async () => {
    const calls = ok([oib])
    await getMyAssignments()
    expect(calls).toHaveLength(1)
    expect(calls[0].url).toBe(MY_ASSIGNMENTS_PATH)
    expect(calls[0].url).not.toContain('?')
    expect(calls[0].init?.method).toBe('GET')
    expect(calls[0].init?.body).toBeUndefined()
  })

  it('keeps only the documented fields', async () => {
    ok([{ ...oib, allowStartAfterDeadline: true, internalNote: 'x' }])
    const r = await getMyAssignments()
    expect(r.ok && r.data[0]).toEqual(oib)
  })

  it('rejects a response with the wrong shape', async () => {
    ok([{ ...oib, maxAttempts: '3' }])
    const r = await getMyAssignments()
    expect(r.ok).toBe(false)
    expect(!r.ok && r.error.code).toBe('invalid_response')
  })
})

describe('MyAssignmentsPage', () => {
  it('shows every assignment returned by the server', async () => {
    ok([oib, security])
    await renderPage()
    const rows = screen.getAllByRole('row')
    expect(rows).toHaveLength(3) // header + 2
    expect(screen.getByText('course-oib')).toBeTruthy()
    expect(screen.getByText('rev-xss-1')).toBeTruthy()
    expect(screen.getByText('Odmah')).toBeTruthy()
  })

  it('shows a message when the student has no assignments', async () => {
    ok([])
    await renderPage()
    expect(screen.getByText('Trenutno nemate dodeljenih zadataka.')).toBeTruthy()
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('401 asks the user to log in again and shows no data', async () => {
    server(401, { error: 'unauthenticated', correlationId: 'c1' })
    await renderPage()
    expect(screen.getByRole('alert').textContent).toContain('Sesija je istekla ili niste prijavljeni.')
    expect(screen.getByRole('link', { name: 'Prijavite se ponovo' }).getAttribute('href')).toBe('/login')
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('403 shows a denial and no data', async () => {
    server(403, {})
    await renderPage()
    expect(screen.getByRole('alert').textContent).toContain('Nemate dozvolu za ovu radnju.')
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('network failure shows an error instead of an empty list', async () => {
    setFetcher(async () => { throw new TypeError('offline') })
    await renderPage()
    expect(screen.getByRole('alert').textContent).toContain('Server nije dostupan.')
    expect(screen.queryByText('Trenutno nemate dodeljenih zadataka.')).toBeNull()
  })

  it('renders server text as plain text, never as HTML (SEC-05)', async () => {
    const payload = '<img src=x onerror="alert(1)"><script>alert(2)</script>'
    ok([{ ...oib, courseId: payload }])
    const { container } = await renderPage()
    expect(screen.getByText(payload)).toBeTruthy()
    expect(container.querySelector('img')).toBeNull()
    expect(container.querySelector('script')).toBeNull()
  })

  it('shows the deadline with a time zone label', async () => {
    ok([oib])
    await renderPage()
    const dueCell = screen.getAllByRole('cell')[3]
    expect(dueCell.textContent).toMatch(/2027/)
    expect(dueCell.textContent).toMatch(/GMT|UTC|CES?T|[+-]\d/)
  })
})

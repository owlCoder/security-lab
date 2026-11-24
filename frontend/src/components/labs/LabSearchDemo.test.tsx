import { afterEach, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { TEST_USER_HEADER } from '../../api/labs'
import LabsSection from './LabsSection'

const TEACHER = '11111111-1111-1111-1111-111111111111:Teacher'
const STUDENT = '22222222-2222-2222-2222-222222222222:Student'
const LAB = {
  id: '66666666-6666-6666-6666-666666666666',
  title: 'LAB-05: Pretraga i kontrola pristupa',
  status: 'Draft',
  createdAt: '2026-11-04T10:00:00+00:00',
}

function json(status: number, body: unknown) {
  return new Response(JSON.stringify(body), { status })
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

it('uses the seeded course identities from the actual user picker and clears search results when users change', async () => {
  const searchIdentities: (string | null)[] = []
  // This verifies the real picker -> LabsSection -> search API wiring. The backend
  // fixture separately verifies these identities against actual seeded membership.
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    if (!url.startsWith('/api/laboratories/search')) throw new Error(`Unexpected request: ${url}`)
    const identity = new Headers(init?.headers).get(TEST_USER_HEADER)
    const isSearch = url.includes('/search?')
    if (isSearch) searchIdentities.push(identity)
    if (!identity) return json(401, { title: 'Unauthenticated' })
    if (identity !== TEACHER && identity !== STUDENT) return json(403, { title: 'Access denied' })
    if (!isSearch) return json(200, { difficulties: [], types: [], tags: [], metadataAvailable: false })
    return json(200, { items: identity === TEACHER ? [LAB] : [], truncated: false })
  }))
  render(<LabsSection />)
  const picker = screen.getByLabelText('Test korisnik')

  fireEvent.change(picker, { target: { value: 'teacher' } })
  fireEvent.change(screen.getByLabelText('Naziv ili deo naziva'), { target: { value: 'pretraga' } })
  fireEvent.submit(screen.getByRole('search'))
  expect(await screen.findByText(LAB.title)).toBeTruthy()
  const session = screen.getByText('Trenutno:', { exact: false })
  expect(session.textContent).toContain('marko – Nastavnik')
  expect(session.textContent).not.toContain(TEACHER.split(':')[0])

  fireEvent.change(picker, { target: { value: 'student' } })
  expect(screen.queryByText(LAB.title)).toBeNull()
  fireEvent.submit(screen.getByRole('search'))
  expect((await screen.findByRole('status')).textContent).toContain('Nijedna dostupna laboratorija')
  expect(screen.queryByText(LAB.title)).toBeNull()

  fireEvent.change(picker, { target: { value: 'teacher' } })
  fireEvent.submit(screen.getByRole('search'))
  expect(await screen.findByText(LAB.title)).toBeTruthy()
  fireEvent.change(picker, { target: { value: 'none' } })
  expect(screen.queryByText(LAB.title)).toBeNull()
  fireEvent.submit(screen.getByRole('search'))
  expect(await screen.findByRole('heading', { name: 'Pristup pretrazi nije potvrđen' })).toBeTruthy()
  expect(screen.getAllByRole('alert').some(alert => alert.textContent?.includes('HTTP 401'))).toBe(true)
  expect(screen.queryByRole('region', { name: 'Dostupne laboratorije' })).toBeNull()
  expect(searchIdentities).toEqual([TEACHER, STUDENT, TEACHER, null])
})

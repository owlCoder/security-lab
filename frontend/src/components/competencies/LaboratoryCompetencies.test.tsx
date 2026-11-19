import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import type { AuthContextValue } from '../../types/auth'
import type { Competency, LaboratoryCompetencies as LaboratoryData } from '../../api/competencies'
import LaboratoryCompetencies from './LaboratoryCompetencies'

const { useAuthMock } = vi.hoisted(() => ({ useAuthMock: vi.fn() }))
vi.mock('../../auth/AuthContext', () => ({ useAuth: useAuthMock }))

const laboratoryId = '30000000-0000-0000-0000-000000000001'
const secondLaboratoryId = '30000000-0000-0000-0000-000000000002'
const firstCompetency: Competency = {
  id: '4b677e63-204f-4688-a0eb-a339c343f101', name: 'Kontrola pristupa', description: 'Provera dozvola nad resursima.',
}
const secondCompetency: Competency = {
  id: '4b677e63-204f-4688-a0eb-a339c343f102', name: 'Analiza bezbednosnih logova', description: null,
}
const catalog = [firstCompetency, secondCompetency]
const linksPath = `/api/laboratories/${laboratoryId}/competencies`

function laboratory(overrides: Partial<LaboratoryData> = {}): LaboratoryData {
  return {
    laboratoryId, laboratoryTitle: 'Bezbednost kontrolisanog pristupa', canManage: true,
    competencies: [firstCompetency, secondCompetency], ...overrides,
  }
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function auth(request: AuthContextValue['request'], userId = 'signed-in-user') {
  const context: AuthContextValue = {
    request, user: { userId, level: 'Active' }, isLoading: false, isAuthenticated: true,
    initializationError: null, refreshUser: vi.fn(), login: vi.fn(), logout: vi.fn(),
  }
  useAuthMock.mockReturnValue(context)
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(complete => { resolve = complete })
  return { promise, resolve }
}

function linked() {
  return within(screen.getByRole('complementary', { name: /^Povezane kompetencije \(\d+\)$/ }))
}

beforeEach(() => {
  useAuthMock.mockReset()
})

afterEach(() => {
  cleanup()
})

describe('PRG-04 laboratory competencies', () => {
  it('displays both linked competencies and obeys server read-only permission without fetching the catalog', async () => {
    const request = vi.fn().mockResolvedValue(json(laboratory({ canManage: false })))
    auth(request)
    render(<LaboratoryCompetencies laboratoryId={laboratoryId} />)

    await screen.findByRole('heading', { name: 'Bezbednost kontrolisanog pristupa' })
    expect(linked().getAllByRole('listitem')).toHaveLength(2)
    expect(linked().getByText(firstCompetency.name)).toBeTruthy()
    expect(linked().getByText(secondCompetency.name)).toBeTruthy()
    expect(linked().getByText(firstCompetency.description!)).toBeTruthy()
    expect(screen.getByText('Samo pregled')).toBeTruthy()
    expect(screen.queryByRole('checkbox')).toBeNull()
    expect(screen.queryByRole('searchbox')).toBeNull()
    expect(screen.queryByRole('button', { name: /Ukloni kompetenciju/ })).toBeNull()
    expect(request).toHaveBeenCalledTimes(1)
    expect(request).toHaveBeenCalledWith(linksPath, expect.objectContaining({ method: 'GET' }))
  })

  it('enables editing only from canManage returned by the server and shows current selections', async () => {
    const request = vi.fn(async (input: RequestInfo | URL) => String(input) === '/api/competencies'
      ? json({ competencies: catalog }) : json(laboratory({ competencies: [firstCompetency] })))
    auth(request, 'user-with-server-granted-management')
    render(<LaboratoryCompetencies laboratoryId={laboratoryId} />)

    const first = await screen.findByRole('checkbox', { name: firstCompetency.name }) as HTMLInputElement
    const second = screen.getByRole('checkbox', { name: secondCompetency.name }) as HTMLInputElement
    expect(first.checked).toBe(true)
    expect(second.checked).toBe(false)
    expect(first.closest('fieldset')?.disabled).toBe(false)
    expect(screen.getByRole('button', { name: `Ukloni kompetenciju: ${firstCompetency.name}` })).toBeTruthy()
    expect(request).toHaveBeenCalledWith('/api/competencies', expect.objectContaining({ method: 'GET' }))
  })

  it('saves checkbox additions and removals, then displays the canonical server state after each write', async () => {
    const saved = deferred<Response>()
    const refreshed = deferred<Response>()
    let reads = 0
    const request = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = String(input)
      if (path === '/api/competencies') return json({ competencies: catalog })
      if (init?.method === 'PUT') return saved.promise
      if (init?.method === 'DELETE') return new Response(null, { status: 204 })
      if (path === linksPath) {
        reads += 1
        if (reads === 1) return json(laboratory({ competencies: [] }))
        if (reads === 2) return refreshed.promise
        return json(laboratory({ competencies: [secondCompetency] }))
      }
      throw new Error(`Unexpected request: ${path}`)
    })
    auth(request)
    render(<LaboratoryCompetencies laboratoryId={laboratoryId} />)
    const first = await screen.findByRole('checkbox', { name: firstCompetency.name }) as HTMLInputElement

    fireEvent.click(first)
    expect(request).toHaveBeenCalledWith(`${linksPath}/${firstCompetency.id}`, expect.objectContaining({ method: 'PUT' }))
    expect(first.closest('fieldset')?.disabled).toBe(true)
    expect(first.checked).toBe(false)
    expect(linked().queryByRole('listitem')).toBeNull()
    expect(screen.getByRole('status').textContent).toContain('Čuvanje:')

    await act(async () => { saved.resolve(new Response(null, { status: 201 })) })
    await waitFor(() => expect(reads).toBe(2))
    expect(linked().queryByRole('listitem')).toBeNull()
    expect(screen.getByRole('status').textContent).not.toContain('je dodata')

    await act(async () => { refreshed.resolve(json(laboratory())) })
    await waitFor(() => expect(linked().getAllByRole('listitem')).toHaveLength(2))
    expect((screen.getByRole('checkbox', { name: secondCompetency.name }) as HTMLInputElement).checked).toBe(true)
    expect(screen.getByRole('status').textContent).toContain('je dodata')

    fireEvent.click(screen.getByRole('checkbox', { name: firstCompetency.name }))
    await waitFor(() => expect(linked().getAllByRole('listitem')).toHaveLength(1))
    expect(request).toHaveBeenCalledWith(`${linksPath}/${firstCompetency.id}`, expect.objectContaining({ method: 'DELETE' }))
    expect(reads).toBe(3)
    expect(linked().queryByText(firstCompetency.name)).toBeNull()
    expect(linked().getByText(secondCompetency.name)).toBeTruthy()
    expect((screen.getByRole('checkbox', { name: firstCompetency.name }) as HTMLInputElement).checked).toBe(false)
    expect(screen.getByRole('status').textContent).toContain('je uklonjena')
  })

  it.each(['save', 'refresh'] as const)('provides recovery without false success when the %s fails', async failure => {
    let failureActive = true
    let wrote = false
    const request = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      if (String(input) === '/api/competencies') return json({ competencies: catalog })
      if (init?.method === 'PUT') {
        if (failure === 'save') return json({ detail: 'private exception' }, 503)
        wrote = true
        return new Response(null, { status: 201 })
      }
      if (wrote && failureActive) return json({ detail: 'private exception' }, 503)
      return json(laboratory({ competencies: wrote ? [secondCompetency] : [] }))
    })
    auth(request)
    render(<LaboratoryCompetencies laboratoryId={laboratoryId} />)
    fireEvent.click(await screen.findByRole('checkbox', { name: secondCompetency.name }))

    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain('Izmena nije potvrđena.')
    expect(alert.textContent).not.toContain('private exception')
    expect(screen.queryByText(/je dodata/)).toBeNull()
    expect(screen.queryByRole('checkbox')).toBeNull()

    failureActive = false
    fireEvent.click(screen.getByRole('button', { name: 'Učitaj ponovo' }))
    const selection = await screen.findByRole('checkbox', { name: secondCompetency.name }) as HTMLInputElement
    expect(selection.checked).toBe(failure === 'refresh')
    expect(screen.queryByRole('alert')).toBeNull()
    if (failure === 'save') expect(linked().queryByRole('listitem')).toBeNull()
    else expect(linked().getByText(secondCompetency.name)).toBeTruthy()
  })

  it('explains empty catalog and links without offering a selection that cannot be saved', async () => {
    const request = vi.fn(async (input: RequestInfo | URL) => String(input) === '/api/competencies'
      ? json({ competencies: [] }) : json(laboratory({ competencies: [] })))
    auth(request)
    render(<LaboratoryCompetencies laboratoryId={laboratoryId} />)

    expect(await screen.findByText('U katalogu trenutno nema kompetencija.')).toBeTruthy()
    expect(linked().getByText('Još nema povezanih kompetencija.')).toBeTruthy()
    expect(screen.queryByRole('checkbox')).toBeNull()
    expect(screen.queryByRole('button', { name: /Ukloni kompetenciju/ })).toBeNull()
  })

  it('retains existing links after catalog failure and can reload the catalog', async () => {
    let catalogUnavailable = true
    const request = vi.fn(async (input: RequestInfo | URL) => String(input) === '/api/competencies'
      ? catalogUnavailable ? json({}, 503) : json({ competencies: catalog })
      : json(laboratory()))
    auth(request)
    render(<LaboratoryCompetencies laboratoryId={laboratoryId} />)

    expect((await screen.findByRole('alert')).textContent).toContain('Katalog nije učitan.')
    expect(linked().getAllByRole('listitem')).toHaveLength(2)
    expect(screen.queryByRole('checkbox')).toBeNull()
    catalogUnavailable = false
    fireEvent.click(screen.getByRole('button', { name: 'Učitaj katalog ponovo' }))

    await screen.findByRole('checkbox', { name: firstCompetency.name })
    expect(screen.queryByRole('alert')).toBeNull()
    expect(linked().getAllByRole('listitem')).toHaveLength(2)
  })

  it('hides linked data if the session expires while the catalog is loading', async () => {
    const request = vi.fn(async (input: RequestInfo | URL) => String(input) === '/api/competencies'
      ? json({ code: 'session_invalid' }, 401) : json(laboratory()))
    auth(request)
    render(<LaboratoryCompetencies laboratoryId={laboratoryId} />)

    expect((await screen.findByRole('alert')).textContent).toContain('Prijavite se ponovo.')
    expect(screen.queryByRole('complementary')).toBeNull()
    expect(screen.queryByText(firstCompetency.name)).toBeNull()
  })

  it('retries an unavailable laboratory without displaying server exception details', async () => {
    const request = vi.fn().mockResolvedValueOnce(json({ detail: 'private exception' }, 503))
      .mockResolvedValueOnce(json(laboratory({ canManage: false })))
    auth(request)
    render(<LaboratoryCompetencies laboratoryId={laboratoryId} />)

    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain('Kompetencije trenutno nisu dostupne.')
    expect(alert.textContent).not.toContain('private exception')
    fireEvent.click(screen.getByRole('button', { name: 'Učitaj ponovo' }))

    await screen.findByRole('heading', { name: 'Bezbednost kontrolisanog pristupa' })
    expect(screen.queryByRole('alert')).toBeNull()
    expect(request).toHaveBeenCalledTimes(2)
  })

  it.each(['laboratory', 'user'] as const)('never displays a stale in-flight response after the %s changes', async change => {
    const stale = deferred<Response>()
    const nextId = change === 'laboratory' ? secondLaboratoryId : laboratoryId
    const nextLaboratory = laboratory({
      laboratoryId: nextId, laboratoryTitle: 'Trenutno dostupna laboratorija',
      canManage: false, competencies: [secondCompetency],
    })
    let calls = 0
    const request = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => {
      calls += 1
      return calls === 1 ? stale.promise : json(nextLaboratory)
    })
    auth(request, 'first-user')
    const view = render(<LaboratoryCompetencies laboratoryId={laboratoryId} />)
    await waitFor(() => expect(request).toHaveBeenCalledTimes(1))
    const staleSignal = request.mock.calls[0][1]?.signal

    if (change === 'user') auth(request, 'second-user')
    view.rerender(<LaboratoryCompetencies laboratoryId={nextId} />)
    await screen.findByRole('heading', { name: nextLaboratory.laboratoryTitle })
    expect(staleSignal?.aborted).toBe(true)

    await act(async () => { stale.resolve(json(laboratory({ laboratoryTitle: 'Stari privatni podaci' }))) })
    expect(screen.queryByText('Stari privatni podaci')).toBeNull()
    expect(linked().queryByText(firstCompetency.name)).toBeNull()
    expect(linked().getByText(secondCompetency.name)).toBeTruthy()
    expect(screen.queryByRole('checkbox')).toBeNull()
    expect(request).toHaveBeenCalledTimes(2)
  })
})

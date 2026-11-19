import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import LabSearch from './LabSearch'

const teacher = { id: 'marko', role: 'Teacher' } as const
const student = { id: 'ana', role: 'Student' } as const
const OPTIONS = { difficulties: ['Easy', 'Medium', 'Hard'], types: ['AccessControl', 'LogAnalysis'], tags: ['security', 'access'], metadataAvailable: true }

function lab(id: string, title: string) {
  return { id, title, status: 'Draft', createdAt: '2026-11-04T10:00:00+00:00' }
}

function json(status: number, body: unknown) {
  return new Response(JSON.stringify(body), { status })
}

function serverReturns(status: number, body: unknown, options = OPTIONS) {
  const fetchMock = vi.fn(async (url: string) => url.endsWith('/search/filters') ? json(200, options) : json(status, body))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function search(text: string) {
  fireEvent.change(screen.getByLabelText('Naziv ili deo naziva'), { target: { value: text } })
  fireEvent.submit(screen.getByRole('search'))
}

async function chooseFilters() {
  await screen.findByRole('option', { name: 'Laka' })
  fireEvent.change(screen.getByLabelText('Težina'), { target: { value: 'Easy' } })
  fireEvent.change(screen.getByLabelText('Tip'), { target: { value: 'AccessControl' } })
  fireEvent.click(screen.getByRole('checkbox', { name: 'security' }))
  fireEvent.click(screen.getByRole('checkbox', { name: 'access' }))
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('LAB-05 laboratory search', () => {
  it('displays results returned by the server', async () => {
    serverReturns(200, { items: [lab('id-1', 'RBAC i kontrola pristupa'), lab('id-2', 'RBAC napredno')], truncated: false })
    render(<LabSearch user={teacher} />)

    search('rbac')

    expect((await screen.findByRole('status')).textContent).toContain('Pronađeno laboratorija: 2')
    expect(screen.getByText('RBAC i kontrola pristupa')).toBeTruthy()
    expect(screen.getByText('RBAC napredno')).toBeTruthy()
    expect(screen.getByText('id-1')).toBeTruthy()
    expect(screen.getByRole('region', { name: 'Dostupne laboratorije' })).toBeTruthy()
  })

  it('clearly indicates when no results are available', async () => {
    serverReturns(200, { items: [], truncated: false })
    render(<LabSearch user={student} />)

    search('rbac')

    expect((await screen.findByRole('status')).textContent).toContain('Nijedna dostupna laboratorija')
    expect(screen.queryByRole('list')).toBeNull()
  })

  it('shows all accessible laboratories when the title is empty', async () => {
    const fetchMock = serverReturns(200, { items: [lab('id-all', 'Analiza logova')], truncated: false })
    render(<LabSearch user={teacher} />)

    search('   ')

    expect(await screen.findByText('Analiza logova', { selector: '.labs-search-title' })).toBeTruthy()
    const searchCall = fetchMock.mock.calls.find(([url]) => url.includes('/search?'))!
    expect(new URL(searchCall[0], 'http://localhost').searchParams.get('q')).toBe('   ')
  })

  it('renders HTML in a title as text without creating elements', async () => {
    serverReturns(200, { items: [lab('id-x', '<img src=x onerror=alert(1)>')], truncated: false })
    const { container } = render(<LabSearch user={teacher} />)

    search('img')

    expect(await screen.findByText('<img src=x onerror=alert(1)>')).toBeTruthy()
    expect(container.querySelector('img')).toBeNull()
  })

  it.each([
    [401, 'Pristup pretrazi nije potvrđen'],
    [403, 'Pretraga nije dozvoljena'],
  ])('clears previous results and explains access after denial (%s)', async (status, heading) => {
    serverReturns(200, { items: [lab('id-1', 'RBAC i kontrola pristupa')], truncated: false })
    render(<LabSearch user={teacher} />)
    search('rbac')
    await screen.findByText('RBAC i kontrola pristupa')

    serverReturns(status, { title: 'Access denied' })
    search('rbac')

    expect((await screen.findByRole('alert')).textContent).toContain(`HTTP ${status}`)
    expect(screen.getByRole('heading', { name: heading })).toBeTruthy()
    expect(screen.queryByText('RBAC i kontrola pristupa')).toBeNull()
    expect(screen.queryByRole('region', { name: 'Dostupne laboratorije' })).toBeNull()
  })

  it('clears previous results when the user changes', async () => {
    serverReturns(200, { items: [lab('id-1', 'Markov nacrt')], truncated: false })
    const { rerender } = render(<LabSearch user={teacher} />)
    search('nacrt')
    await screen.findByText('Markov nacrt')

    rerender(<LabSearch user={student} />)

    expect(screen.queryByText('Markov nacrt')).toBeNull()
  })

  it('indicates when the server truncates the results', async () => {
    serverReturns(200, { items: [lab('id-1', 'Lab 001')], truncated: true })
    render(<LabSearch user={teacher} />)

    search('lab')

    expect(await screen.findByText(/Prikazani su samo prvi rezultati/)).toBeTruthy()
  })

  it('provides a search landmark, input label and length limit', () => {
    serverReturns(200, { items: [], truncated: false })
    render(<LabSearch user={teacher} />)
    expect(screen.getByRole('search')).toBeTruthy()
    const input = screen.getByRole('searchbox', { name: 'Naziv ili deo naziva' })
    expect(input.getAttribute('aria-describedby')).toBe('labs-search-hint')
    expect(input.getAttribute('maxlength')).toBe('200')
    expect(input.hasAttribute('required')).toBe(false)
  })

  it('combines difficulty, type and existing tags without requiring a title', async () => {
    const fetchMock = serverReturns(200, { items: [lab('id-match', 'Dostupna vežba')], truncated: false })
    render(<LabSearch user={teacher} />)
    await chooseFilters()

    fireEvent.submit(screen.getByRole('search'))

    expect(await screen.findByText('Dostupna vežba')).toBeTruthy()
    const params = new URL(fetchMock.mock.calls.find(([url]) => url.includes('/search?'))![0], 'http://localhost').searchParams
    expect(params.get('q')).toBe('')
    expect(params.get('difficulty')).toBe('Easy')
    expect(params.get('type')).toBe('AccessControl')
    expect(params.getAll('tag')).toEqual(['security', 'access'])
    expect(screen.getByRole('button', { name: 'Ukloni oznaku: security' })).toBeTruthy()
    expect(screen.queryByRole('textbox', { name: /oznaka/i })).toBeNull()
  })

  it.each([
    ['Ukloni filter težine: Laka', null, 'AccessControl', ['security', 'access']],
    ['Ukloni filter tipa: Kontrola pristupa', 'Easy', null, ['security', 'access']],
    ['Ukloni oznaku: security', 'Easy', 'AccessControl', ['access']],
  ] as const)('refreshes results immediately after %s while keeping other filters', async (button, difficulty, type, tags) => {
    const fetchMock = serverReturns(200, { items: [lab('id-broader', 'Prošireni rezultat')], truncated: false })
    render(<LabSearch user={teacher} />)
    await chooseFilters()
    fireEvent.change(screen.getByLabelText('Naziv ili deo naziva'), { target: { value: 'kontrola' } })

    fireEvent.click(screen.getByRole('button', { name: button }))

    expect(await screen.findByText('Prošireni rezultat')).toBeTruthy()
    const params = new URL(fetchMock.mock.calls.find(([url]) => url.includes('/search?'))![0], 'http://localhost').searchParams
    expect(params.get('q')).toBe('kontrola')
    expect(params.get('difficulty')).toBe(difficulty)
    expect(params.get('type')).toBe(type)
    expect(params.getAll('tag')).toEqual(tags)
    expect(screen.queryByRole('button', { name: button })).toBeNull()
  })

  it('resets all filters, preserves the title and shows broader results', async () => {
    const fetchMock = serverReturns(200, { items: [lab('id-1', 'RBAC početni')], truncated: false })
    render(<LabSearch user={teacher} />)
    await chooseFilters()
    search('rbac')
    await screen.findByText('RBAC početni')
    fetchMock.mockImplementation(async (url: string) => url.endsWith('/search/filters') ? json(200, OPTIONS)
      : json(200, { items: [lab('id-1', 'RBAC početni'), lab('id-2', 'RBAC napredni')], truncated: false }))

    fireEvent.click(screen.getByRole('button', { name: 'Resetuj sve filtere' }))

    expect(await screen.findByText('RBAC napredni')).toBeTruthy()
    const lastSearch = fetchMock.mock.calls.filter(([url]) => url.includes('/search?')).at(-1)!
    expect(lastSearch[0]).toBe('/api/laboratories/search?q=rbac')
    expect((screen.getByLabelText('Naziv ili deo naziva') as HTMLInputElement).value).toBe('rbac')
    expect((screen.getByLabelText('Težina') as HTMLSelectElement).value).toBe('')
    expect((screen.getByLabelText('Tip') as HTMLSelectElement).value).toBe('')
    expect(screen.getAllByRole('checkbox').every(input => !(input as HTMLInputElement).checked)).toBe(true)
    expect(screen.queryByRole('button', { name: /Ukloni/ })).toBeNull()
  })

  it('clears old results when criteria change until the next search', async () => {
    serverReturns(200, { items: [lab('id-old', 'Prethodna vežba')], truncated: false })
    render(<LabSearch user={teacher} />)
    search('vežba')
    await screen.findByText('Prethodna vežba')
    await screen.findByRole('option', { name: 'Laka' })

    fireEvent.change(screen.getByLabelText('Težina'), { target: { value: 'Easy' } })

    expect(screen.queryByText('Prethodna vežba')).toBeNull()
  })

  it('ignores a delayed search response after resetting filters', async () => {
    let resolveOld!: (response: Response) => void
    let searches = 0
    vi.stubGlobal('fetch', vi.fn((url: string) => {
      if (url.endsWith('/search/filters')) return Promise.resolve(json(200, OPTIONS))
      searches += 1
      return searches === 1 ? new Promise<Response>(resolve => { resolveOld = resolve })
        : Promise.resolve(json(200, { items: [lab('id-new', 'Posle resetovanja')], truncated: false }))
    }))
    render(<LabSearch user={teacher} />)
    await chooseFilters()
    search('rbac')

    fireEvent.click(screen.getByRole('button', { name: 'Resetuj sve filtere' }))
    await screen.findByText('Posle resetovanja')
    await act(async () => { resolveOld(json(200, { items: [lab('id-old', 'Zakašneli rezultat')], truncated: false })) })

    expect(screen.queryByText('Zakašneli rezultat')).toBeNull()
    expect(screen.getByText('Posle resetovanja')).toBeTruthy()
  })

  it('clears tags and selections and ignores delayed responses when the user changes', async () => {
    let resolveOld!: (response: Response) => void
    vi.stubGlobal('fetch', vi.fn((url: string, init: RequestInit) => {
      const currentTeacher = (init.headers as Record<string, string>)['X-Test-User'] === 'marko:Teacher'
      if (url.endsWith('/search/filters')) return Promise.resolve(json(200, currentTeacher ? OPTIONS : { ...OPTIONS, tags: ['student-tag'] }))
      return new Promise<Response>(resolve => { resolveOld = resolve })
    }))
    const { rerender } = render(<LabSearch user={teacher} />)
    await chooseFilters()
    search('rbac')

    rerender(<LabSearch user={student} />)

    expect(screen.queryByRole('checkbox', { name: 'security' })).toBeNull()
    expect(screen.queryByRole('button', { name: /Ukloni/ })).toBeNull()
    expect((screen.getByLabelText('Naziv ili deo naziva') as HTMLInputElement).value).toBe('')
    await screen.findByRole('checkbox', { name: 'student-tag' })
    await act(async () => { resolveOld(json(200, { items: [lab('private', 'Markov privatni rezultat')], truncated: false })) })
    expect(screen.queryByText('Markov privatni rezultat')).toBeNull()
  })

  it('disables metadata filters while allowing title search when metadata is unavailable', async () => {
    serverReturns(200, { items: [lab('id-1', 'Analiza logova')], truncated: false }, { ...OPTIONS, tags: [], metadataAvailable: false })
    render(<LabSearch user={teacher} />)
    await screen.findByText(/Filteri po osobinama trenutno nisu dostupni/)

    expect(screen.getByRole('group', { name: 'Filteri po osobinama' }).hasAttribute('disabled')).toBe(true)
    search('log')
    expect(await screen.findByText('Analiza logova', { selector: '.labs-search-title' })).toBeTruthy()
  })

  it('allows retrying a failed filter options request', async () => {
    let attempts = 0
    vi.stubGlobal('fetch', vi.fn(async () => ++attempts === 1 ? json(503, {}) : json(200, OPTIONS)))
    render(<LabSearch user={teacher} />)
    await screen.findByText('Dostupni filteri nisu učitani.')

    fireEvent.click(screen.getByRole('button', { name: 'Učitaj filtere ponovo' }))

    await waitFor(() => expect(screen.getByRole('checkbox', { name: 'security' })).toBeTruthy())
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('broadens the search immediately after clearing a tag or difficulty', async () => {
    const fetchMock = serverReturns(200, { items: [lab('id-1', 'Dostupna vežba')], truncated: false })
    render(<LabSearch user={teacher} />)
    await chooseFilters()

    fireEvent.click(screen.getByRole('checkbox', { name: 'security' }))
    await screen.findByText('Dostupna vežba')
    fireEvent.change(screen.getByLabelText('Težina'), { target: { value: '' } })
    await screen.findByText('Dostupna vežba')

    const searches = fetchMock.mock.calls.filter(([url]) => url.includes('/search?'))
    expect(searches).toHaveLength(2)
    const params = new URL(searches.at(-1)![0], 'http://localhost').searchParams
    expect(params.has('difficulty')).toBe(false)
    expect(params.get('type')).toBe('AccessControl')
    expect(params.getAll('tag')).toEqual(['access'])
  })
})

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import LabSearch from './LabSearch'

const teacher = { id: 'marko', role: 'Teacher' } as const
const student = { id: 'ana', role: 'Student' } as const

function lab(id: string, title: string) {
  return { id, title, status: 'Draft', createdAt: '2026-11-04T10:00:00+00:00' }
}

function serverReturns(status: number, body: unknown) {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { status }))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function search(text: string) {
  fireEvent.change(screen.getByLabelText('Naziv ili deo naziva'), { target: { value: text } })
  fireEvent.submit(screen.getByRole('button', { name: 'Pretraži' }))
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('LAB-05 pretraga laboratorija', () => {
  it('prikazuje rezultate koje je vratio server', async () => {
    serverReturns(200, { items: [lab('id-1', 'RBAC i kontrola pristupa'), lab('id-2', 'RBAC napredno')], truncated: false })
    render(<LabSearch user={teacher} />)

    search('rbac')

    expect((await screen.findByRole('status')).textContent).toContain('Pronađeno laboratorija: 2')
    expect(screen.getByText('RBAC i kontrola pristupa')).toBeTruthy()
    expect(screen.getByText('RBAC napredno')).toBeTruthy()
    expect(screen.getByText('id-1')).toBeTruthy()
  })

  it('kada nema rezultata, to jasno piše', async () => {
    serverReturns(200, { items: [], truncated: false })
    render(<LabSearch user={student} />)

    search('rbac')

    expect((await screen.findByRole('status')).textContent).toContain('Nijedna dostupna laboratorija')
    expect(screen.queryByRole('list')).toBeNull()
  })

  it('prazan upit se ne šalje serveru', () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    render(<LabSearch user={teacher} />)

    search('   ')

    expect(fetchMock).not.toHaveBeenCalled()
    expect(screen.getByRole('alert').textContent).toContain('Unesite naziv')
    expect(screen.getByLabelText('Naziv ili deo naziva').getAttribute('aria-invalid')).toBe('true')
  })

  it('HTML u nazivu se prikazuje kao tekst, a ne kao element', async () => {
    serverReturns(200, { items: [lab('id-x', '<img src=x onerror=alert(1)>')], truncated: false })
    const { container } = render(<LabSearch user={teacher} />)

    search('img')

    expect(await screen.findByText('<img src=x onerror=alert(1)>')).toBeTruthy()
    expect(container.querySelector('img')).toBeNull()
  })

  it('posle odbijanja (401) ne ostaju prikazani prethodni rezultati', async () => {
    serverReturns(200, { items: [lab('id-1', 'RBAC i kontrola pristupa')], truncated: false })
    render(<LabSearch user={teacher} />)
    search('rbac')
    await screen.findByText('RBAC i kontrola pristupa')

    serverReturns(401, { title: 'Unauthenticated' })
    search('rbac')

    expect((await screen.findByRole('alert')).textContent).toContain('HTTP 401')
    expect(screen.queryByText('RBAC i kontrola pristupa')).toBeNull()
  })

  it('promena korisnika briše rezultate prethodnog korisnika', async () => {
    serverReturns(200, { items: [lab('id-1', 'Markov nacrt')], truncated: false })
    const { rerender } = render(<LabSearch user={teacher} />)
    search('nacrt')
    await screen.findByText('Markov nacrt')

    rerender(<LabSearch user={student} />)

    expect(screen.queryByText('Markov nacrt')).toBeNull()
  })

  it('označava kada je server skratio listu rezultata', async () => {
    serverReturns(200, { items: [lab('id-1', 'Lab 001')], truncated: true })
    render(<LabSearch user={teacher} />)

    search('lab')

    expect(await screen.findByText(/Prikazani su samo prvi rezultati/)).toBeTruthy()
  })

  it('pristupačnost: forma je označena kao pretraga, polje ima label i ograničenje dužine', () => {
    render(<LabSearch user={teacher} />)
    expect(screen.getByRole('search')).toBeTruthy()
    const input = screen.getByRole('searchbox', { name: 'Naziv ili deo naziva' })
    expect(input.getAttribute('aria-describedby')).toBe('labs-search-hint')
    expect(input.getAttribute('maxlength')).toBe('200')
  })
})

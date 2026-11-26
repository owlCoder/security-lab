import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import CreateLabForm from './CreateLabForm'

const student = { id: 'ana', role: 'Student' } as const
const teacher = { id: 'marko', role: 'Teacher' } as const

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

function submitTitle(title: string) {
  fireEvent.change(screen.getByLabelText('Naslov laboratorije'), { target: { value: title } })
  // submit forme je isto što i Enter u polju ili klik na dugme
  fireEvent.submit(screen.getByRole('button', { name: 'Kreiraj laboratoriju' }))
}

describe('LAB-01/LAB-07 forma za kreiranje', () => {
  it('student: server vraća 403 i forma prikazuje zabranu', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ title: 'Access denied' }), { status: 403 })))
    render(<CreateLabForm user={student} />)

    submitTitle('Moja laboratorija')

    expect((await screen.findByRole('alert')).textContent).toContain('Nemate dozvolu')
  })

  it('400: poruka je vezana za polje naslova', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'INVALID_TITLE' }), { status: 400 })))
    render(<CreateLabForm user={teacher} />)

    submitTitle('Naslov')

    expect((await screen.findByRole('alert')).textContent).toContain('najviše 200 znakova')
    expect(screen.getByLabelText('Naslov laboratorije').getAttribute('aria-invalid')).toBe('true')
  })

  it('prazan naslov se ne šalje serveru', () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    render(<CreateLabForm user={teacher} />)

    submitTitle('   ')

    expect(fetchMock).not.toHaveBeenCalled()
    expect(screen.getByRole('alert').textContent).toContain('Unesite naslov')
  })

  it('uspešno kreiranje prikazuje ID koji je dodelio server', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: 'srv-42' }), { status: 201 })))
    render(<CreateLabForm user={teacher} />)

    submitTitle('Nova laboratorija')

    expect((await screen.findByRole('status')).textContent).toContain('srv-42')
  })

  it('nastavnik: 501 iz LAB-01 kostura je obaveštenje, a ne greška', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 501 })))
    render(<CreateLabForm user={teacher} />)

    submitTitle('Nova laboratorija')

    const message = await screen.findByRole('status')
    expect(message.textContent).toContain('HTTP 501')
    expect(message.textContent).toContain('Prava su proverena')
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('pristupačnost: polje ima label, opis i ograničenje dužine', () => {
    render(<CreateLabForm user={teacher} />)
    const input = screen.getByRole('textbox', { name: 'Naslov laboratorije' })
    expect(input.getAttribute('aria-describedby')).toBe('labs-title-hint')
    expect(input.getAttribute('maxlength')).toBe('200')
  })
})

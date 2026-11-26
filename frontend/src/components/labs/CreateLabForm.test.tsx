import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import CreateLabForm from './CreateLabForm'
import { DEV_DEMO_COURSES } from '../../api/labs'

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

  it('šalje izabrani kurs i naslov, bez ID-ja i autora', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: 'srv-7' }), { status: 201 }))
    vi.stubGlobal('fetch', fetchMock)
    render(<CreateLabForm user={teacher} />)

    fireEvent.change(screen.getByLabelText('Kurs'), { target: { value: DEV_DEMO_COURSES[1].id } })
    submitTitle('Lab na drugom kursu')

    await screen.findByRole('status')
    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({ title: 'Lab na drugom kursu', courseId: DEV_DEMO_COURSES[1].id })
  })

  it('400 INVALID_COURSE: poruka je vezana za polje kursa, ne za naslov', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'INVALID_COURSE' }), { status: 400 })))
    render(<CreateLabForm user={teacher} />)

    submitTitle('Naslov')

    expect((await screen.findByRole('alert')).textContent).toContain('Izaberite kurs na kome predajete')
    expect(screen.getByLabelText('Kurs').getAttribute('aria-invalid')).toBe('true')
    expect(screen.getByLabelText('Naslov laboratorije').getAttribute('aria-invalid')).toBe('false')
  })

  it('nastavnik koji nije na kursu: server vraća 403 i forma prikazuje zabranu', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ title: 'Access denied' }), { status: 403 })))
    render(<CreateLabForm user={teacher} />)

    fireEvent.change(screen.getByLabelText('Kurs'), { target: { value: DEV_DEMO_COURSES[1].id } })
    submitTitle('Lab na tuđem kursu')

    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain('Nemate dozvolu')
    expect(alert.textContent).toContain('HTTP 403')
  })

  it('pristupačnost: polja imaju label, opis i ograničenje dužine', () => {
    render(<CreateLabForm user={teacher} />)
    const input = screen.getByRole('textbox', { name: 'Naslov laboratorije' })
    expect(input.getAttribute('aria-describedby')).toBe('labs-title-hint')
    expect(input.getAttribute('maxlength')).toBe('200')
    const course = screen.getByRole('combobox', { name: 'Kurs' })
    expect(course.getAttribute('aria-describedby')).toBe('labs-course-hint')
  })
})
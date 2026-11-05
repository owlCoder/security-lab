import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import LabView from './LabView'
import { DEMO_LAB_ID, DEMO_XSS_LAB_ID, XSS_TITLE } from '../../api/fakeLabContent'

const student = { id: 'ana', role: 'Student' } as const

afterEach(cleanup)

function openLab(id: string) {
  fireEvent.change(screen.getByLabelText('ID laboratorije'), { target: { value: id } })
  fireEvent.click(screen.getByRole('button', { name: 'Prikaži laboratoriju' }))
}

describe('LAB-07 prikaz laboratorije', () => {
  it('SEC-05: naslov <script>alert(1)</script> prikazuje se kao običan tekst i ne izvršava se', async () => {
    const { container } = render(<LabView user={student} />)
    openLab(DEMO_XSS_LAB_ID)

    const heading = await screen.findByRole('heading', { level: 4 })
    expect(heading.textContent).toBe(XSS_TITLE)
    expect(container.querySelector('script')).toBeNull()
    expect(container.querySelector('img')).toBeNull()
    expect(container.querySelector('article b')).toBeNull()
    expect(screen.getByText(/<img src="x" onerror="alert\(1\)">/)).toBeTruthy()
  })

  it('promena jezika menja tekst, a revizija ostaje ista', async () => {
    render(<LabView user={student} />)
    openLab(DEMO_LAB_ID)
    expect((await screen.findByRole('heading', { level: 4 })).textContent).toBe('Bezbedna konfiguracija veb servera')
    const revisionSr = screen.getByText(/b2c4e6a8-/).textContent

    fireEvent.click(screen.getByRole('radio', { name: 'English' }))

    expect((await screen.findByRole('heading', { level: 4, name: 'Secure web server configuration' })).getAttribute('id')).toBe('labs-content-title')
    expect(screen.getByRole('article').getAttribute('lang')).toBe('en')
    expect(screen.getByText(/b2c4e6a8-/).textContent).toBe(revisionSr)
  })

  it('kad prevod ne postoji, jasno piše da je prikazan srpski', async () => {
    render(<LabView user={student} />)
    fireEvent.click(screen.getByRole('radio', { name: 'English' }))
    openLab(DEMO_XSS_LAB_ID)

    expect((await screen.findByRole('status')).textContent).toContain('Prikazan je podrazumevani tekst na srpskom')
    expect(screen.getByRole('article').getAttribute('lang')).toBe('sr')
  })

  it('anonimni korisnik dobija poruku o prijavi i ne vidi sadržaj', async () => {
    render(<LabView user={null} />)
    openLab(DEMO_LAB_ID)

    expect((await screen.findByRole('alert')).textContent).toContain('Niste prijavljeni')
    expect(screen.queryByRole('article')).toBeNull()
  })

  it('pristupačnost: sva polja i dugmad imaju ime koje čitač ekrana čita', () => {
    render(<LabView user={student} />)
    expect(screen.getByRole('textbox', { name: 'ID laboratorije' })).toBeTruthy()
    expect(screen.getByRole('group', { name: 'Jezik' })).toBeTruthy()
    expect(screen.getByRole('radio', { name: 'Srpski' })).toBeTruthy()
    expect(screen.getByRole('radio', { name: 'English' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Prikaži laboratoriju' }).getAttribute('type')).toBe('submit')
  })
})

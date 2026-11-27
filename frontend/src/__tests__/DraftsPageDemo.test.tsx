import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import DraftsPage from '../pages/DraftsPage'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

function stubBrowserApis() {
  const fetchSpy = vi.fn()
  vi.stubGlobal('fetch', fetchSpy)
  vi.stubGlobal('crypto', { randomUUID: () => 'draft-demo-1' })
  return fetchSpy
}

function createDraft(title: string) {
  fireEvent.click(screen.getByRole('button', { name: 'Novi nacrt' }))
  fireEvent.change(screen.getByLabelText('Naziv'), { target: { value: title } })
  fireEvent.click(screen.getByRole('button', { name: 'Kreiraj nacrt' }))
}

// AUT-01: stranica radi samo nad lokalnim stanjem, pa mora jasno da kaže da se ništa ne čuva.
describe('AUT-01 DraftsPage demo režim', () => {
  it('odmah prikazuje da se nacrti ne čuvaju', () => {
    stubBrowserApis()
    render(<DraftsPage />)

    const notice = screen.getByRole('status')

    expect(notice.textContent).toMatch(/demo/i)
    expect(notice.textContent).toMatch(/ne čuvaju/i)
  })

  it('upozorenje ostaje vidljivo i nakon kreiranja nacrta', () => {
    stubBrowserApis()
    render(<DraftsPage />)

    createDraft('Moj nacrt')

    expect(screen.getByText('Moj nacrt')).not.toBeNull()
    expect(screen.getByRole('status').textContent).toMatch(/ne čuvaju/i)
  })

  it('nigde ne tvrdi da je nacrt sačuvan', () => {
    stubBrowserApis()
    render(<DraftsPage />)

    createDraft('Moj nacrt')

    expect(document.body.textContent).not.toMatch(/sačuvan|uspešno|trajno/i)
  })

  it('kreiranje nacrta ne šalje nijedan zahtev serveru', () => {
    const fetchSpy = stubBrowserApis()
    render(<DraftsPage />)

    createDraft('Moj nacrt')

    expect(fetchSpy).not.toHaveBeenCalled()
  })
})

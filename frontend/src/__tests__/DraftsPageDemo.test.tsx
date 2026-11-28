import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import DraftsPage from '../pages/DraftsPage'
import { createLocalDraftsGateway } from '../api/localDraftsGateway'

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

async function renderDemo() {
  render(<DraftsPage gateway={createLocalDraftsGateway()} />)
  await screen.findByText(/nemate nijedan nacrt/i)
}

async function createDraft(title: string) {
  fireEvent.click(screen.getByRole('button', { name: 'Novi nacrt' }))
  fireEvent.change(screen.getByLabelText('Naziv'), { target: { value: title } })
  await act(async () => {
    fireEvent.click(screen.getByRole('button', { name: 'Kreiraj nacrt' }))
  })
  await screen.findByText(title)
}

// AUT-01: demo režim radi samo nad lokalnim stanjem, pa mora jasno da kaže da se ništa ne čuva.
describe('AUT-01 DraftsPage demo režim', () => {
  it('prikazuje da se nacrti ne čuvaju', async () => {
    stubBrowserApis()
    await renderDemo()

    const notice = screen.getByRole('status')

    expect(notice.textContent).toMatch(/demo/i)
    expect(notice.textContent).toMatch(/ne čuvaju/i)
  })

  it('upozorenje ostaje vidljivo i nakon kreiranja nacrta', async () => {
    stubBrowserApis()
    await renderDemo()

    await createDraft('Moj nacrt')

    expect(screen.getByRole('status').textContent).toMatch(/ne čuvaju/i)
  })

  it('nigde ne tvrdi da je nacrt sačuvan', async () => {
    stubBrowserApis()
    await renderDemo()

    await createDraft('Moj nacrt')

    expect(document.body.textContent).not.toMatch(/sačuvan|uspešno|trajno/i)
  })

  it('kreiranje nacrta ne šalje nijedan zahtev serveru', async () => {
    const fetchSpy = stubBrowserApis()
    await renderDemo()

    await createDraft('Moj nacrt')

    expect(fetchSpy).not.toHaveBeenCalled()
  })
})

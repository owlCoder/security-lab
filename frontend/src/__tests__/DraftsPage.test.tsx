import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import DraftsPage from '../pages/DraftsPage'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

let idCounter = 0

function stubIds() {
  idCounter = 0
  vi.stubGlobal('crypto', {
    randomUUID: () => `draft-test-${++idCounter}`,
  })
}

function createDraft(title: string, goal = '') {
  fireEvent.click(screen.getByRole('button', { name: 'Novi nacrt' }))
  fireEvent.change(screen.getByLabelText('Naziv'), { target: { value: title } })
  if (goal) {
    fireEvent.change(screen.getByLabelText('Cilj zadatka'), { target: { value: goal } })
  }
  fireEvent.click(screen.getByRole('button', { name: 'Kreiraj nacrt' }))
}

describe('AUT-01 DraftsPage', () => {
  it('prikazuje početni ekran i praznu listu', () => {
    render(<DraftsPage />)

    expect(screen.getByRole('heading', { name: 'Nacrti zadataka' })).not.toBeNull()
    expect(screen.getByText(/nemate nijedan nacrt/i)).not.toBeNull()
  })

  it('otvara i zatvara formu za kreiranje', () => {
    render(<DraftsPage />)

    fireEvent.click(screen.getByRole('button', { name: 'Novi nacrt' }))
    expect(screen.getByRole('heading', { name: 'Novi nacrt zadatka' })).not.toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Zatvori formu' }))
    expect(screen.queryByRole('heading', { name: 'Novi nacrt zadatka' })).toBeNull()
  })

  it('kreira nacrt, prikazuje ga u listi i zatvara formu', () => {
    stubIds()
    render(<DraftsPage />)

    createDraft('Moj prvi nacrt', 'Testiranje sistema')

    expect(screen.getByText('Moj prvi nacrt')).not.toBeNull()
    expect(screen.getByText('Nacrt')).not.toBeNull()
    expect(screen.queryByRole('heading', { name: 'Novi nacrt zadatka' })).toBeNull()
  })

  it('prazan naziv ne dodaje nacrt i forma ostaje otvorena', () => {
    render(<DraftsPage />)

    fireEvent.click(screen.getByRole('button', { name: 'Novi nacrt' }))
    fireEvent.click(screen.getByRole('button', { name: 'Kreiraj nacrt' }))

    expect(screen.getByRole('alert')).not.toBeNull()
    expect(screen.getByText(/nemate nijedan nacrt/i)).not.toBeNull()
  })

  it('otvara detalje izabranog nacrta', () => {
    stubIds()
    render(<DraftsPage />)

    createDraft('Testni nacrt', 'Neki cilj')
    fireEvent.click(screen.getByRole('button', { name: 'Otvori nacrt Testni nacrt' }))

    const details = screen.getByRole('region', { name: 'Izabrani nacrt' })

    expect(within(details).getByText('Testni nacrt')).not.toBeNull()
    expect(within(details).getByText('Neki cilj')).not.toBeNull()
    expect(within(details).getByText('Kontrola pristupa')).not.toBeNull()
  })

  it('prikazuje "Nije definisan" kada cilj nije unet', () => {
    stubIds()
    render(<DraftsPage />)

    createDraft('Bez cilja')
    fireEvent.click(screen.getByRole('button', { name: 'Otvori nacrt Bez cilja' }))

    const details = screen.getByRole('region', { name: 'Izabrani nacrt' })

    expect(within(details).getByText('Nije definisan')).not.toBeNull()
  })

  it('bira pravi nacrt kada ih ima više', () => {
    stubIds()
    render(<DraftsPage />)

    createDraft('Prvi nacrt', 'Cilj prvog')
    createDraft('Drugi nacrt', 'Cilj drugog')
    fireEvent.click(screen.getByRole('button', { name: 'Otvori nacrt Drugi nacrt' }))

    const details = screen.getByRole('region', { name: 'Izabrani nacrt' })

    expect(within(details).getByText('Cilj drugog')).not.toBeNull()
    expect(within(details).queryByText('Cilj prvog')).toBeNull()
  })

  it('otvaranje forme zatvara prikaz izabranog nacrta', () => {
    stubIds()
    render(<DraftsPage />)

    createDraft('Testni nacrt')
    fireEvent.click(screen.getByRole('button', { name: 'Otvori nacrt Testni nacrt' }))
    expect(screen.getByRole('region', { name: 'Izabrani nacrt' })).not.toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Novi nacrt' }))

    expect(screen.queryByRole('region', { name: 'Izabrani nacrt' })).toBeNull()
  })
})

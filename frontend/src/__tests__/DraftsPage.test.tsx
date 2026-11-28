import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import DraftsPage from '../pages/DraftsPage'
import { DraftsApiError } from '../api/drafts'
import type { CreateDraftData, Draft } from '../components/drafts/DraftTypes'

afterEach(() => {
  cleanup()
})

function draft(id: string, title: string, extra: Partial<Draft> = {}): Draft {
  return { id, title, goal: '', type: 'AccessControl', status: 'Draft', ...extra }
}

// Gateway koji se ponaša kao server: kreiranje dodaje nacrt, a lista uvek vraća ono što je "sačuvano".
function fakeGateway(initial: Draft[] = []) {
  let stored = [...initial]
  return {
    persistsOnServer: true,
    list: vi.fn(async () => stored.map(item => ({ ...item }))),
    create: vi.fn(async (data: CreateDraftData) => {
      const created: Draft = { ...data, id: `server-${stored.length + 1}`, status: 'Draft' }
      stored = [...stored, created]
      return created
    }),
  }
}

async function renderLoaded(gateway = fakeGateway()) {
  render(<DraftsPage gateway={gateway} />)
  await waitFor(() => expect(screen.queryByText('Učitavanje nacrta…')).toBeNull())
  return gateway
}

function fillTitle(title: string, goal = '') {
  fireEvent.click(screen.getByRole('button', { name: 'Novi nacrt' }))
  fireEvent.change(screen.getByLabelText('Naziv'), { target: { value: title } })
  if (goal) fireEvent.change(screen.getByLabelText('Cilj zadatka'), { target: { value: goal } })
}

async function submitForm() {
  await act(async () => {
    fireEvent.click(screen.getByRole('button', { name: 'Kreiraj nacrt' }))
  })
}

describe('AUT-01 DraftsPage', () => {
  it('prikazuje učitavanje, pa praznu listu', async () => {
    render(<DraftsPage gateway={fakeGateway()} />)

    expect(screen.getByText('Učitavanje nacrta…')).not.toBeNull()
    expect(await screen.findByText(/nemate nijedan nacrt/i)).not.toBeNull()
    expect(screen.queryByText('Učitavanje nacrta…')).toBeNull()
  })

  it('prikazuje nacrte koje vrati server', async () => {
    const gateway = await renderLoaded(fakeGateway([
      draft('1', 'Pristup nastavnika'),
      draft('2', 'Pregled događaja', { type: 'LogAnalysis' }),
    ]))

    expect(screen.getByText('Pristup nastavnika')).not.toBeNull()
    expect(screen.getByText('Pregled događaja')).not.toBeNull()
    expect(screen.getByText('Analiza logova')).not.toBeNull()
    expect(gateway.list).toHaveBeenCalledTimes(1)
  })

  it('otvara i zatvara formu za kreiranje', async () => {
    await renderLoaded()

    fireEvent.click(screen.getByRole('button', { name: 'Novi nacrt' }))
    expect(screen.getByRole('heading', { name: 'Novi nacrt zadatka' })).not.toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Zatvori formu' }))
    expect(screen.queryByRole('heading', { name: 'Novi nacrt zadatka' })).toBeNull()
  })

  it('kreira nacrt na serveru, ponovo učitava listu i zatvara formu', async () => {
    const gateway = await renderLoaded()

    fillTitle('Moj prvi nacrt', 'Testiranje sistema')
    await submitForm()

    expect(await screen.findByText('Moj prvi nacrt')).not.toBeNull()
    expect(gateway.create).toHaveBeenCalledExactlyOnceWith({
      title: 'Moj prvi nacrt', goal: 'Testiranje sistema', type: 'AccessControl',
    })
    expect(gateway.list).toHaveBeenCalledTimes(2)
    expect(screen.queryByRole('heading', { name: 'Novi nacrt zadatka' })).toBeNull()
  })

  it('prazan naziv ne šalje zahtev serveru', async () => {
    const gateway = await renderLoaded()

    fireEvent.click(screen.getByRole('button', { name: 'Novi nacrt' }))
    await submitForm()

    expect(screen.getByRole('alert').textContent).toContain('Unesite naziv')
    expect(gateway.create).not.toHaveBeenCalled()
  })

  it.each([
    [401, 'Sesija je istekla. Prijavite se ponovo.'],
    [403, 'Nemate pravo da radite sa nacrtima zadataka.'],
    [422, 'Podaci nacrta nisu prošli proveru. Proverite naziv, cilj i tip laboratorije.'],
  ])('greška servera %s se prikazuje, a uneti podaci ostaju u formi', async (status, message) => {
    const gateway = await renderLoaded()
    gateway.create.mockRejectedValue(new DraftsApiError(status, message))

    fillTitle('Moj nacrt', 'Moj cilj')
    await submitForm()

    expect((await screen.findByRole('alert')).textContent).toBe(message)
    expect((screen.getByLabelText('Naziv') as HTMLInputElement).value).toBe('Moj nacrt')
    expect((screen.getByLabelText('Cilj zadatka') as HTMLTextAreaElement).value).toBe('Moj cilj')
    expect(screen.getByRole('heading', { name: 'Novi nacrt zadatka' })).not.toBeNull()
    expect(gateway.list).toHaveBeenCalledTimes(1)
    expect(screen.queryByText('Moj nacrt', { selector: 'td' })).toBeNull()
  })

  it('nepoznata greška prikazuje opštu poruku bez tehničkih detalja', async () => {
    const gateway = await renderLoaded()
    gateway.create.mockRejectedValue(new Error('private stack trace'))

    fillTitle('Moj nacrt')
    await submitForm()

    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toBe('Zahtev za nacrte nije uspeo. Pokušajte ponovo.')
    expect(alert.textContent).not.toMatch(/private/)
  })

  it('tokom slanja dugme je onemogućeno, pa dupli klik ne pravi dupli nacrt', async () => {
    const gateway = await renderLoaded()
    let finish!: (created: Draft) => void
    gateway.create.mockImplementation(() => new Promise<Draft>(resolve => { finish = resolve }))

    fillTitle('Moj nacrt')
    await submitForm()

    const busy = screen.getByRole('button', { name: 'Kreiranje…' }) as HTMLButtonElement
    expect(busy.disabled).toBe(true)
    fireEvent.click(busy)
    expect(gateway.create).toHaveBeenCalledTimes(1)

    await act(async () => {
      finish(draft('server-1', 'Moj nacrt'))
    })

    await waitFor(() => expect(screen.queryByRole('heading', { name: 'Novi nacrt zadatka' })).toBeNull())
    expect(gateway.create).toHaveBeenCalledTimes(1)
  })

  it('greška pri učitavanju liste se prikazuje i može da se ponovi', async () => {
    const gateway = fakeGateway()
    gateway.list
      .mockRejectedValueOnce(new DraftsApiError(503, 'Nacrti trenutno nisu dostupni. Pokušajte ponovo.'))
      .mockResolvedValue([draft('1', 'Posle ponovnog pokušaja')])

    render(<DraftsPage gateway={gateway} />)

    expect((await screen.findByRole('alert')).textContent).toContain('trenutno nisu dostupni')

    fireEvent.click(screen.getByRole('button', { name: 'Pokušaj ponovo' }))

    expect(await screen.findByText('Posle ponovnog pokušaja')).not.toBeNull()
    expect(gateway.list).toHaveBeenCalledTimes(2)
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('otvara detalje izabranog nacrta', async () => {
    await renderLoaded(fakeGateway([draft('1', 'Testni nacrt', { goal: 'Neki cilj' })]))

    fireEvent.click(screen.getByRole('button', { name: 'Otvori nacrt Testni nacrt' }))

    const details = screen.getByRole('region', { name: 'Izabrani nacrt' })
    expect(within(details).getByText('Testni nacrt')).not.toBeNull()
    expect(within(details).getByText('Neki cilj')).not.toBeNull()
    expect(within(details).getByText('Kontrola pristupa')).not.toBeNull()
  })

  it('prikazuje "Nije definisan" kada cilj nije unet', async () => {
    await renderLoaded(fakeGateway([draft('1', 'Bez cilja')]))

    fireEvent.click(screen.getByRole('button', { name: 'Otvori nacrt Bez cilja' }))

    const details = screen.getByRole('region', { name: 'Izabrani nacrt' })
    expect(within(details).getByText('Nije definisan')).not.toBeNull()
  })

  it('bira pravi nacrt kada ih ima više', async () => {
    await renderLoaded(fakeGateway([
      draft('1', 'Prvi nacrt', { goal: 'Cilj prvog' }),
      draft('2', 'Drugi nacrt', { goal: 'Cilj drugog' }),
    ]))

    fireEvent.click(screen.getByRole('button', { name: 'Otvori nacrt Drugi nacrt' }))

    const details = screen.getByRole('region', { name: 'Izabrani nacrt' })
    expect(within(details).getByText('Cilj drugog')).not.toBeNull()
    expect(within(details).queryByText('Cilj prvog')).toBeNull()
  })

  it('otvaranje forme zatvara prikaz izabranog nacrta', async () => {
    await renderLoaded(fakeGateway([draft('1', 'Testni nacrt')]))

    fireEvent.click(screen.getByRole('button', { name: 'Otvori nacrt Testni nacrt' }))
    expect(screen.getByRole('region', { name: 'Izabrani nacrt' })).not.toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Novi nacrt' }))

    expect(screen.queryByRole('region', { name: 'Izabrani nacrt' })).toBeNull()
  })

  it('ne prikazuje demo upozorenje kada se nacrti čuvaju na serveru', async () => {
    await renderLoaded()

    expect(screen.queryByRole('status')).toBeNull()
  })
})

import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { setFetcher } from '../api/http'
import { authoringErrorMessage, createDraftFromTemplate, listTemplates } from '../api/authoring'
import AuthoringSection from '../components/authoring/AuthoringSection'
import TemplateDetailPage from '../pages/TemplateDetailPage'
import TemplateListPage from '../pages/TemplateListPage'
import type { LabDraft, LabTemplate } from '../types/authoring'

const sablon: LabTemplate = { id: 't1', name: 'allow-deny', title: 'Allow / Deny', goal: 'Cilj šablona', type: 'AccessControl' }
const drugi: LabTemplate = { id: 't2', name: 'logovi', title: 'Analiza logova', goal: 'Cilj logova', type: 'LogAnalysis' }
const nacrt: LabDraft = {
  id: 'd1', sourceTemplateId: 't1', ownerId: 'o1', status: 'Draft', version: 1,
  title: sablon.title, goal: sablon.goal, type: sablon.type,
}

type Call = { url: string; init?: RequestInit }
function server(routes: (c: Call) => Response | Promise<Response>) {
  const calls: Call[] = []
  setFetcher(async (url, init) => { calls.push({ url, init }); return routes({ url, init }) })
  return calls
}
const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status })
const posts = (calls: Call[]) => calls.filter(c => c.init?.method === 'POST')

afterEach(cleanup)

describe('API klijent za sablone (AUT-06 ugovor)', () => {
  it('listTemplates salje GET i vraca samo niz sablona iz omota', async () => {
    const calls = server(() => json(200, { templates: [sablon], correlationId: 'c1' }))
    const r = await listTemplates()
    expect(calls[0].url).toBe('/api/authoring/templates')
    expect(calls[0].init?.method).toBe('GET')
    expect(r.ok).toBe(true)
    if (r.ok) expect(r.data).toEqual([sablon])
  })

  it('listTemplates prosledjuje gresku 403', async () => {
    server(() => json(403, { error: 'forbidden', correlationId: 'c2' }))
    const r = await listTemplates()
    expect(r.ok).toBe(false)
    if (!r.ok) expect(r.error.status).toBe(403)
  })

  it('createDraftFromTemplate salje POST bez tela i vraca nacrt sa novim ID-jem', async () => {
    const calls = server(() => json(201, { ...nacrt, correlationId: 'c3' }))
    const r = await createDraftFromTemplate('t1')
    expect(calls[0].url).toBe('/api/authoring/templates/t1/drafts')
    expect(calls[0].init?.method).toBe('POST')
    expect(calls[0].init?.body).toBeUndefined()
    expect(r.ok).toBe(true)
    if (r.ok) {
      expect(r.data.id).not.toBe('t1')
      expect(r.data.sourceTemplateId).toBe('t1')
    }
  })

  it('enkoduje ID u putanji (bez path traversal-a)', async () => {
    const calls = server(() => json(201, nacrt))
    await createDraftFromTemplate('../admin')
    expect(calls[0].url).toBe('/api/authoring/templates/..%2Fadmin/drafts')
  })

  it('mrezna greska se prepoznaje', async () => {
    server(() => { throw new TypeError('offline') })
    const r = await createDraftFromTemplate('t1')
    expect(r.ok).toBe(false)
    if (!r.ok) expect(r.error.code).toBe('network_error')
  })

  it.each([
    [401, 'Niste prijavljeni.'],
    [403, 'Samo autori mogu da kreiraju zadatke iz šablona.'],
    [404, 'Šablon ne postoji.'],
    [409, 'Došlo je do konflikta. Osvežite prikaz i pokušajte ponovo.'],
  ])('authoringErrorMessage za status %i', (status, poruka) => {
    expect(authoringErrorMessage({ status, code: `http_${status}`, message: 'x' })).toBe(poruka)
  })

  it('authoringErrorMessage za ostale statuse vraca poruku iz greske', () => {
    expect(authoringErrorMessage({ status: 400, code: 'http_400', message: 'Zahtev nije ispravan.' }))
      .toBe('Zahtev nije ispravan.')
  })
})

describe('TemplateListPage', () => {
  it('prikazuje sablone iz odgovora', async () => {
    server(() => json(200, { templates: [sablon, drugi], correlationId: 'c' }))
    render(<TemplateListPage onOpen={() => {}} />)
    expect(await screen.findByText('Allow / Deny')).toBeTruthy()
    expect(screen.getByText('Analiza logova')).toBeTruthy()
  })

  it('prikazuje praznu listu', async () => {
    server(() => json(200, { templates: [], correlationId: 'c' }))
    render(<TemplateListPage onOpen={() => {}} />)
    expect(await screen.findByText('Nema dostupnih šablona.')).toBeTruthy()
  })

  it.each([
    [401, 'Niste prijavljeni.'],
    [403, 'Samo autori mogu da kreiraju zadatke iz šablona.'],
  ])('status %i prikazuje jasnu poruku', async (status, poruka) => {
    server(() => json(status, { error: 'x', correlationId: 'c' }))
    render(<TemplateListPage onOpen={() => {}} />)
    expect((await screen.findByRole('alert')).textContent).toBe(poruka)
  })

  it('klik na Detalji prosledjuje ceo sablon', async () => {
    server(() => json(200, { templates: [sablon], correlationId: 'c' }))
    const onOpen = vi.fn()
    render(<TemplateListPage onOpen={onOpen} />)
    fireEvent.click(await screen.findByLabelText('Otvori Allow / Deny'))
    await waitFor(() => expect(onOpen).toHaveBeenCalledWith(sablon))
  })
})

async function napraviNacrt() {
  const calls = server(() => json(201, { ...nacrt, correlationId: 'c' }))
  render(<TemplateDetailPage template={sablon} onBack={() => {}} />)
  await act(async () => { fireEvent.click(screen.getByText('Kreiraj zadatak iz šablona')) })
  await screen.findByRole('status')
  return calls
}

describe('TemplateDetailPage', () => {
  it('prikazuje podatke sablona', () => {
    render(<TemplateDetailPage template={sablon} onBack={() => {}} />)
    expect(screen.getByText('Allow / Deny')).toBeTruthy()
    expect(screen.getByText('Cilj šablona')).toBeTruthy()
  })

  it('klik na Nazad poziva onBack', () => {
    const onBack = vi.fn()
    render(<TemplateDetailPage template={sablon} onBack={onBack} />)
    fireEvent.click(screen.getByText('← Nazad na šablone'))
    expect(onBack).toHaveBeenCalledTimes(1)
  })

  it('kreiranje salje POST bez tela na pravu adresu i prikazuje uspeh', async () => {
    const calls = await napraviNacrt()
    expect(posts(calls)).toHaveLength(1)
    expect(calls[0].url).toBe('/api/authoring/templates/t1/drafts')
    expect(calls[0].init?.body).toBeUndefined()
    expect(screen.getByRole('status').textContent).toContain('Zadatak je kreiran')
  })

  it('novi zadatak ima novi ID, a sablon zadrzava svoj', async () => {
    await napraviNacrt()
    expect(screen.getByText(/Novi ID: d1/)).toBeTruthy()
    expect(screen.getByText(/Izvorni šablon: t1/)).toBeTruthy()
  })

  it('polja novog zadatka su prazna i unos radi', async () => {
    await napraviNacrt()
    const naslov = screen.getByLabelText('Naslov') as HTMLInputElement
    const cilj = screen.getByLabelText('Cilj') as HTMLTextAreaElement
    expect(naslov.value).toBe('')
    expect(cilj.value).toBe('')
    fireEvent.change(naslov, { target: { value: 'Moj naslov' } })
    fireEvent.change(cilj, { target: { value: 'Moj cilj' } })
    expect(naslov.value).toBe('Moj naslov')
    expect(cilj.value).toBe('Moj cilj')
  })

  it('Sacuvaj je onemogucen dok backend ne izlozi izmenu (AUT-01)', async () => {
    await napraviNacrt()
    expect((screen.getByText('Sačuvaj') as HTMLButtonElement).disabled).toBe(true)
  })

  it('Kreiraj jos jedan vraca dugme za kreiranje', async () => {
    await napraviNacrt()
    fireEvent.click(screen.getByText('Kreiraj još jedan'))
    expect(screen.getByText('Kreiraj zadatak iz šablona')).toBeTruthy()
    expect(screen.queryByRole('status')).toBeNull()
  })

  it('dvostruki klik salje samo jedan zahtev', async () => {
    let pusti!: () => void
    const kapija = new Promise<void>(resolve => { pusti = resolve })
    const calls = server(async () => { await kapija; return json(201, { ...nacrt, correlationId: 'c' }) })
    render(<TemplateDetailPage template={sablon} onBack={() => {}} />)
    const dugme = screen.getByText('Kreiraj zadatak iz šablona') as HTMLButtonElement
    fireEvent.click(dugme)
    fireEvent.click(dugme)
    expect(dugme.disabled).toBe(true)
    expect(screen.getByText('Kreiranje…')).toBeTruthy()
    expect(posts(calls)).toHaveLength(1)
    await act(async () => { pusti() })
    await screen.findByRole('status')
    expect(posts(calls)).toHaveLength(1)
  })

  it.each([
    [403, 'Samo autori mogu'],
    [404, 'Šablon ne postoji'],
    [409, 'konflikta'],
  ])('greska %i prikazuje poruku, bez uspeha', async (status, deo) => {
    server(() => json(status, { error: 'x', correlationId: 'c' }))
    render(<TemplateDetailPage template={sablon} onBack={() => {}} />)
    await act(async () => { fireEvent.click(screen.getByText('Kreiraj zadatak iz šablona')) })
    expect((await screen.findByRole('alert')).textContent).toContain(deo)
    expect(screen.queryByRole('status')).toBeNull()
    expect(screen.getByText('Kreiraj zadatak iz šablona')).toBeTruthy()
  })
})

describe('AuthoringSection', () => {
  it('lista, pa detalji, pa nazad na listu', async () => {
    server(() => json(200, { templates: [sablon], correlationId: 'c' }))
    render(<AuthoringSection />)
    fireEvent.click(await screen.findByLabelText('Otvori Allow / Deny'))
    fireEvent.click(await screen.findByText('← Nazad na šablone'))
    expect(await screen.findByText('Šabloni laboratorija')).toBeTruthy()
  })
})
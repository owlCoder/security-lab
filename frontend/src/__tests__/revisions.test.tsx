import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { session, setFetcher } from '../api/http'
import { runRevisionAction, type RevisionDetail } from '../api/revisions'
import RevisionDetailPage from '../pages/RevisionDetailPage'
import RevisionListPage from '../pages/RevisionListPage'

const base: RevisionDetail = {
  id: 'r1', labId: 'l1', labTitle: 'SQL lab', courseId: 'c1', courseName: 'OIB',
  status: 'InReview', revisionNumber: null, author: { id: 'a', displayName: 'Ana' },
  submittedAt: '2026-11-02T10:00:00Z', updatedAt: '2026-11-02T10:00:00Z',
  contentHash: 'sha256:abcdef1234567890', rowVersion: 'v1',
  policy: { requiresSeparateApprover: true }, allowedActions: ['approve', 'return'], decisions: [],
}

type Call = { url: string; init?: RequestInit }
function server(routes: (c: Call) => Response | Promise<Response>) {
  const calls: Call[] = []
  setFetcher(async (url, init) => { calls.push({ url, init }); return routes({ url, init }) })
  return calls
}
const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status })
const bodyOf = (c: Call) => JSON.parse(String(c.init?.body))
const headersOf = (c: Call) => c.init?.headers as Record<string, string>

beforeEach(() => session.set('tok'))
afterEach(cleanup)

describe('runRevisionAction (REV-05 ugovor)', () => {
  it('salje rowVersion, contentHash, trimovan komentar, Idempotency-Key i token', async () => {
    const calls = server(() => json(200, base))
    await runRevisionAction(base, 'return', 'key-1', '  dopuni hint  ')
    expect(calls[0].url).toBe('/api/revisions/r1/return')
    expect(calls[0].init?.method).toBe('POST')
    expect(bodyOf(calls[0])).toEqual({ rowVersion: 'v1', contentHash: 'sha256:abcdef1234567890', comment: 'dopuni hint' })
    expect(headersOf(calls[0])['Idempotency-Key']).toBe('key-1')
    expect(headersOf(calls[0]).Authorization).toBe('Bearer tok')
  })
  it('enkoduje ID u putanji (bez path traversal-a)', async () => {
    const calls = server(() => json(200, base))
    await runRevisionAction({ ...base, id: '../admin' }, 'submit', 'k')
    expect(calls[0].url).toBe('/api/revisions/..%2Fadmin/submit')
  })
})

async function openDetail(detail: RevisionDetail, onCommand: (c: Call) => Response) {
  const calls = server(c => (c.init?.method === 'POST' ? onCommand(c) : json(200, detail)))
  render(<RevisionDetailPage revisionId={detail.id} onBack={() => {}} />)
  await screen.findByText(detail.labTitle)
  return calls
}

describe('RevisionDetailPage', () => {
  it('prikazuje samo dugmad iz allowedActions', async () => {
    await openDetail({ ...base, allowedActions: ['submit'], status: 'Draft' }, () => json(200, base))
    expect(screen.queryByText('Predaj na pregled')).not.toBeNull()
    expect(screen.queryByText('Odobri')).toBeNull()
    expect(screen.queryByText('Vrati sa komentarom')).toBeNull()
  })

  it('autor bez prava vidi poruku da nema radnji', async () => {
    await openDetail({ ...base, allowedActions: [] }, () => json(200, base))
    expect(screen.getByText('Nemate dostupnih radnji nad ovom revizijom.')).toBeTruthy()
  })

  it('vracanje ne moze bez komentara', async () => {
    const calls = await openDetail(base, () => json(200, base))
    fireEvent.click(screen.getByText('Vrati sa komentarom'))
    const confirm = screen.getByText('Vrati na doradu') as HTMLButtonElement
    expect(confirm.disabled).toBe(true)
    fireEvent.change(screen.getByRole('textbox'), { target: { value: '   ' } })
    expect(confirm.disabled).toBe(true)
    expect(calls.filter(c => c.init?.method === 'POST')).toHaveLength(0)
  })

  it('otkazivanje brise komentar, pa komentar za vracanje ne zavrsi uz odobrenje', async () => {
    const calls = await openDetail(base, () => json(200, base))
    fireEvent.click(screen.getByText('Vrati sa komentarom'))
    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'za autora' } })
    fireEvent.click(screen.getByText('Otkaži'))
    fireEvent.click(screen.getByText('Odobri'))
    expect((screen.getByRole('textbox') as HTMLTextAreaElement).value).toBe('')
    await act(async () => { fireEvent.click(screen.getByText('Potvrdi odobrenje i objavi')) })
    const post = calls.find(c => c.init?.method === 'POST')!
    expect(bodyOf(post).comment).toBeUndefined()
  })

  it('uspesno odobrenje prikazuje broj revizije i istoriju', async () => {
    const published: RevisionDetail = {
      ...base, status: 'Published', revisionNumber: 3, allowedActions: [],
      decisions: [{ id: 'd1', type: 'Approved', actor: { id: 'p', displayName: 'Petar' }, at: '2026-11-02T11:00:00Z', comment: null, contentHash: base.contentHash, revisionNumber: 3 }],
    }
    await openDetail(base, () => json(200, published))
    fireEvent.click(screen.getByText('Odobri'))
    await act(async () => { fireEvent.click(screen.getByText('Potvrdi odobrenje i objavi')) })
    expect(screen.getByRole('status').textContent).toContain('objavljena')
    expect(screen.getByText('revizija br. 3')).toBeTruthy()
    expect(screen.getByText('Objavljena')).toBeTruthy()
  })

  it('403 self_approval_forbidden prikazuje jasnu poruku', async () => {
    await openDetail(base, () => json(403, { code: 'self_approval_forbidden', message: 'x' }))
    fireEvent.click(screen.getByText('Odobri'))
    await act(async () => { fireEvent.click(screen.getByText('Potvrdi odobrenje i objavi')) })
    expect(screen.getByRole('alert').textContent).toContain('Ne možete odobriti sopstvenu reviziju')
  })

  it('409 revision_changed nudi osvezavanje koje ponovo ucitava reviziju', async () => {
    const calls = await openDetail(base, () => json(409, { code: 'revision_changed', message: 'x' }))
    fireEvent.click(screen.getByText('Odobri'))
    await act(async () => { fireEvent.click(screen.getByText('Potvrdi odobrenje i objavi')) })
    expect(screen.getByRole('alert').textContent).toContain('izmenjena')
    const gets = () => calls.filter(c => (c.init?.method ?? 'GET') === 'GET').length
    const before = gets()
    await act(async () => { fireEvent.click(screen.getByText('Osveži reviziju')) })
    expect(gets()).toBe(before + 1)
  })

  it('422 prikazuje greske validacije (REV-02)', async () => {
    await openDetail(base, () => json(422, { code: 'validation_failed', message: 'x', errors: [{ field: 'evaluator', message: 'Nije registrovan.' }] }))
    fireEvent.click(screen.getByText('Odobri'))
    await act(async () => { fireEvent.click(screen.getByText('Potvrdi odobrenje i objavi')) })
    expect(screen.getByRole('alert').textContent).toContain('Nije registrovan.')
  })

  it('posle mrezne greske ponovni pokusaj koristi ISTI Idempotency-Key', async () => {
    let n = 0
    const calls = server(c => {
      if (c.init?.method !== 'POST') return json(200, base)
      if (++n === 1) throw new TypeError('offline')
      return json(200, { ...base, status: 'Draft', allowedActions: [] })
    })
    render(<RevisionDetailPage revisionId="r1" onBack={() => {}} />)
    await screen.findByText('SQL lab')
    fireEvent.click(screen.getByText('Vrati sa komentarom'))
    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'dopuni' } })
    await act(async () => { fireEvent.click(screen.getByText('Vrati na doradu')) })
    expect(screen.getByRole('alert').textContent).toContain('Server nije dostupan')
    await act(async () => { fireEvent.click(screen.getByText('Vrati na doradu')) })
    const posts = calls.filter(c => c.init?.method === 'POST')
    expect(posts).toHaveLength(2)
    expect(headersOf(posts[0])['Idempotency-Key']).toBe(headersOf(posts[1])['Idempotency-Key'])
  })

  it('komentar u istoriji se prikazuje kao tekst, ne kao HTML', async () => {
    const evil = '<img src=x onerror="alert(1)">'
    await openDetail({ ...base, decisions: [{ id: 'd', type: 'Returned', actor: { id: 'p', displayName: 'P' }, at: base.updatedAt, comment: evil, contentHash: base.contentHash, revisionNumber: null }] }, () => json(200, base))
    expect(screen.getByText(evil)).toBeTruthy()
    expect(document.querySelector('img')).toBeNull()
  })

  it('404 prikazuje poruku bez detalja o reviziji', async () => {
    server(() => json(404, { code: 'not_found', message: 'x' }))
    render(<RevisionDetailPage revisionId="tudja" onBack={() => {}} />)
    expect((await screen.findByRole('alert')).textContent).toContain('ne postoji ili nemate pristup')
  })
})

describe('RevisionListPage', () => {
  it('prikazuje praznu listu i greske', async () => {
    render(<RevisionListPage title="T" emptyText="Prazno" load={async () => ({ ok: true, data: [] })} onOpen={() => {}} />)
    expect(await screen.findByText('Prazno')).toBeTruthy()
    cleanup()
    render(<RevisionListPage title="T" emptyText="Prazno" load={async () => ({ ok: false, error: { status: 500, code: 'x', message: 'Pukao server' } })} onOpen={() => {}} />)
    expect((await screen.findByRole('alert')).textContent).toBe('Pukao server')
  })
  it('klik na Otvori prosledjuje ID', async () => {
    const onOpen = vi.fn()
    render(<RevisionListPage title="T" emptyText="-" load={async () => ({ ok: true, data: [base] })} onOpen={onOpen} />)
    fireEvent.click(await screen.findByLabelText('Otvori SQL lab'))
    await waitFor(() => expect(onOpen).toHaveBeenCalledWith('r1'))
  })
})

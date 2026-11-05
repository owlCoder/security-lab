import { describe, expect, it } from 'vitest'
import { mockFetcher } from '../api/mock'

async function call(method: string, url: string, token?: string, body?: unknown, key?: string) {
  const headers: Record<string, string> = {}
  if (token) headers.Authorization = `Bearer ${token}`
  if (key) headers['Idempotency-Key'] = key
  const res = await mockFetcher(url, { method, headers, body: body ? JSON.stringify(body) : undefined })
  return { status: res.status, body: await res.json() }
}
const PETAR = 'mock-token:u-petar', ANA = 'mock-token:u-ana', STUDENT = 'mock-token:u-student'

describe('mock REV-05 pravila', () => {
  it('autor ne vidi sopstvenu reviziju na listi za odobrenje i ne moze da je odobri', async () => {
    const list = await call('GET', '/api/revisions/pending-reviews', PETAR)
    expect(list.body.map((r: { id: string }) => r.id)).not.toContain('rev-xss')
    const d = await call('GET', '/api/revisions/rev-xss', PETAR)
    expect(d.body.allowedActions).toEqual([])
    const r = await call('POST', '/api/revisions/rev-xss/approve', PETAR, { rowVersion: d.body.rowVersion, contentHash: d.body.contentHash })
    expect(r.status).toBe(403)
    expect(r.body.code).toBe('self_approval_forbidden')
  })

  it('student dobija 404 za reviziju (ne otkriva postojanje)', async () => {
    expect((await call('GET', '/api/revisions/rev-sql', STUDENT)).status).toBe(404)
  })

  it('zastarela verzija daje 409, idempotentno ponavljanje ne pravi dve odluke', async () => {
    const d = (await call('GET', '/api/revisions/rev-xss', ANA)).body
    const stale = await call('POST', '/api/revisions/rev-xss/approve', ANA, { rowVersion: 'stara', contentHash: d.contentHash })
    expect(stale.status).toBe(409)

    const body = { rowVersion: d.rowVersion, contentHash: d.contentHash }
    const first = await call('POST', '/api/revisions/rev-xss/approve', ANA, body, 'k1')
    const again = await call('POST', '/api/revisions/rev-xss/approve', ANA, body, 'k1')
    expect(first.status).toBe(200)
    expect(first.body.revisionNumber).toBe(1)
    expect(again.body).toEqual(first.body)
    expect(first.body.decisions.filter((x: { type: string }) => x.type === 'Approved')).toHaveLength(1)
  })

  it('neispravan evaluator blokira objavu (REV-02)', async () => {
    const d = (await call('GET', '/api/revisions/rev-csrf', ANA)).body
    const r = await call('POST', '/api/revisions/rev-csrf/approve', ANA, { rowVersion: d.rowVersion, contentHash: d.contentHash })
    expect(r.status).toBe(422)
    expect(r.body.errors[0].field).toBe('evaluator')
  })
})
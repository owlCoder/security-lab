import { afterEach, describe, expect, it, vi } from 'vitest'
import { LABS_BASE_PATH, searchLabs, TEST_USER_HEADER } from '../../api/labs'

const teacher = { id: 'marko', role: 'Teacher' } as const

const LAB = {
  id: '6f1c2a9e-1111-4c4c-9a9a-000000000001',
  title: 'RBAC i kontrola pristupa',
  status: 'Draft',
  createdAt: '2026-11-04T10:00:00+00:00',
}

function reply(status: number, body?: unknown) {
  return vi.fn().mockResolvedValue(new Response(body === undefined ? null : JSON.stringify(body), { status }))
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('LAB-05 searchLabs', () => {
  it('šalje GET na /search sa parametrom q i test identitetom, bez tela', async () => {
    const fetchMock = reply(200, { items: [LAB], truncated: false })
    vi.stubGlobal('fetch', fetchMock)

    const outcome = await searchLabs('kontrola', teacher)

    expect(outcome).toEqual({
      kind: 'ok',
      truncated: false,
      items: [{ id: LAB.id, title: LAB.title, status: LAB.status, createdAt: LAB.createdAt }],
    })
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${LABS_BASE_PATH}/search?q=kontrola`)
    expect(init.method).toBeUndefined()
    expect(init.body).toBeUndefined()
    expect(init.headers[TEST_USER_HEADER]).toBe('marko:Teacher')
  })

  it('kodira unos, pa & i = ne mogu da dodaju nove parametre', async () => {
    const fetchMock = reply(200, { items: [], truncated: false })
    vi.stubGlobal('fetch', fetchMock)

    await searchLabs('a&q=b#x', teacher)

    const url = new URL(fetchMock.mock.calls[0][0], 'http://localhost')
    expect(url.searchParams.getAll('q')).toEqual(['a&q=b#x'])
  })

  it('400 INVALID_QUERY daje našu poruku, bez teksta sa servera', async () => {
    vi.stubGlobal('fetch', reply(400, { code: 'INVALID_QUERY', message: 'internal detail' }))

    const outcome = await searchLabs('x', teacher)

    expect(outcome.kind).toBe('invalid')
    expect(outcome.kind === 'invalid' && outcome.messages[0]).toContain('Unesite naziv ili deo naziva')
    expect(JSON.stringify(outcome)).not.toContain('internal detail')
  })

  it('401 i greška mreže imaju svoje ishode', async () => {
    vi.stubGlobal('fetch', reply(401, { title: 'Unauthenticated' }))
    expect((await searchLabs('rbac', null)).kind).toBe('unauthenticated')

    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')))
    expect((await searchLabs('rbac', teacher)).kind).toBe('error')
  })

  it('odgovor bez niza items je greška, a neispravne stavke se odbacuju', async () => {
    vi.stubGlobal('fetch', reply(200, { items: 'nije niz' }))
    expect((await searchLabs('rbac', teacher)).kind).toBe('error')

    vi.stubGlobal('fetch', reply(200, { items: [LAB, { id: 5, title: null }, 'tekst'], truncated: true }))
    const outcome = await searchLabs('rbac', teacher)
    expect(outcome.kind === 'ok' && outcome.items.map(i => i.id)).toEqual([LAB.id])
    expect(outcome.kind === 'ok' && outcome.truncated).toBe(true)
  })
})

import { afterEach, describe, expect, it, vi } from 'vitest'
import { getLabFilterOptions, LABS_BASE_PATH, searchLabs, TEST_USER_HEADER } from '../../api/labs'

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

describe('LAB-05 laboratory search API', () => {
  it('sends GET /search with the query and test identity without a body', async () => {
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

  it('encodes input so special characters cannot inject query parameters', async () => {
    const fetchMock = reply(200, { items: [], truncated: false })
    vi.stubGlobal('fetch', fetchMock)

    await searchLabs('a&q=b#x', teacher)

    const url = new URL(fetchMock.mock.calls[0][0], 'http://localhost')
    expect(url.searchParams.getAll('q')).toEqual(['a&q=b#x'])
  })

  it('maps INVALID_QUERY responses to a local message without exposing server text', async () => {
    vi.stubGlobal('fetch', reply(400, { code: 'INVALID_QUERY', message: 'internal detail' }))

    const outcome = await searchLabs('x', teacher)

    expect(outcome.kind).toBe('invalid')
    expect(outcome.kind === 'invalid' && outcome.messages[0]).toContain('Proverite naziv i izabrane filtere')
    expect(JSON.stringify(outcome)).not.toContain('internal detail')
  })

  it('distinguishes unauthenticated responses from network failures', async () => {
    vi.stubGlobal('fetch', reply(401, { title: 'Unauthenticated' }))
    expect((await searchLabs('rbac', null)).kind).toBe('unauthenticated')

    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')))
    expect((await searchLabs('rbac', teacher)).kind).toBe('error')
  })

  it('rejects responses without an items array and discards invalid items', async () => {
    vi.stubGlobal('fetch', reply(200, { items: 'nije niz' }))
    expect((await searchLabs('rbac', teacher)).kind).toBe('error')

    vi.stubGlobal('fetch', reply(200, { items: [LAB, { id: 5, title: null }, 'tekst'], truncated: true }))
    const outcome = await searchLabs('rbac', teacher)
    expect(outcome.kind === 'ok' && outcome.items.map(i => i.id)).toEqual([LAB.id])
    expect(outcome.kind === 'ok' && outcome.truncated).toBe(true)
  })

  it('sends criteria separately and prevents query parameter injection through tags', async () => {
    const fetchMock = reply(200, { items: [], truncated: false })
    vi.stubGlobal('fetch', fetchMock)
    await searchLabs('', teacher, { difficulty: 'Easy', type: 'AccessControl', tags: ['security', 'x&type=LogAnalysis'] })

    const params = new URL(fetchMock.mock.calls[0][0], 'http://localhost').searchParams
    expect(params.getAll('q')).toEqual([''])
    expect(params.getAll('difficulty')).toEqual(['Easy'])
    expect(params.getAll('type')).toEqual(['AccessControl'])
    expect(params.getAll('tag')).toEqual(['security', 'x&type=LogAnalysis'])
    expect(fetchMock.mock.calls[0][1].cache).toBe('no-store')
  })

  it('loads existing filter options without caching', async () => {
    const options = { difficulties: ['Easy'], types: ['AccessControl'], tags: ['security'], metadataAvailable: true }
    const fetchMock = reply(200, options)
    vi.stubGlobal('fetch', fetchMock)

    expect(await getLabFilterOptions(teacher)).toEqual({ kind: 'ok', options })
    expect(fetchMock.mock.calls[0][0]).toBe(`${LABS_BASE_PATH}/search/filters`)
    expect(fetchMock.mock.calls[0][1].cache).toBe('no-store')
    expect(fetchMock.mock.calls[0][1].headers[TEST_USER_HEADER]).toBe('marko:Teacher')
  })

  it.each([
    { difficulties: ['Easy'], types: [], tags: [5], metadataAvailable: true },
    { difficulties: 'Easy', types: [], tags: [], metadataAvailable: true },
    { difficulties: [], types: [], tags: [], metadataAvailable: 'true' },
    { difficulties: [], types: [], tags: [''], metadataAvailable: true },
  ])('rejects malformed filter options', async options => {
    vi.stubGlobal('fetch', reply(200, options))
    expect((await getLabFilterOptions(teacher)).kind).toBe('error')
  })

  it('returns a distinct outcome for unavailable filters without exposing internal messages', async () => {
    vi.stubGlobal('fetch', reply(503, { code: 'FILTERS_UNAVAILABLE', message: 'connection-string-secret', traceId: 'request-5' }))
    expect(await searchLabs('', teacher, { difficulty: 'Easy' })).toEqual({ kind: 'filtersUnavailable', requestId: 'request-5' })
  })
})

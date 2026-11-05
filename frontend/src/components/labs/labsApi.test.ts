import { afterEach, describe, expect, it, vi } from 'vitest'
import { createLab, getLabContent, LABS_BASE_PATH, parseLanguage, TEST_USER_HEADER } from '../../api/labs'
import { DEMO_LAB_ID, DEMO_XSS_LAB_ID } from '../../api/fakeLabContent'

const teacher = { id: 'marko', role: 'Teacher' } as const
const student = { id: 'ana', role: 'Student' } as const

function reply(status: number, body?: unknown) {
  return vi.fn().mockResolvedValue(new Response(body === undefined ? null : JSON.stringify(body), { status }))
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('LAB-01 createLab', () => {
  it('šalje samo naslov, bez ID-ja, na serversku putanju', async () => {
    const fetchMock = reply(201, { id: 'server-id' })
    vi.stubGlobal('fetch', fetchMock)

    const outcome = await createLab('Nova laboratorija', teacher)

    expect(outcome).toEqual({ kind: 'created', id: 'server-id' })
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe(`${LABS_BASE_PATH}/`)
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body)).toEqual({ title: 'Nova laboratorija' })
    expect(init.headers[TEST_USER_HEADER]).toBe('marko:Teacher')
  })

  it('bez izabranog korisnika ne šalje test identitet', async () => {
    const fetchMock = reply(401)
    vi.stubGlobal('fetch', fetchMock)

    const outcome = await createLab('Naslov', null)

    expect(outcome.kind).toBe('unauthenticated')
    expect(fetchMock.mock.calls[0][1].headers[TEST_USER_HEADER]).toBeUndefined()
  })

  it('student dobija 403 od servera i to se prikazuje kao zabrana', async () => {
    vi.stubGlobal('fetch', reply(403, { title: 'Access denied', traceId: 'abc-123' }))
    expect(await createLab('Naslov', student)).toEqual({ kind: 'forbidden', requestId: 'abc-123' })
  })

  it('400 sa poznatim kodom daje našu poruku', async () => {
    vi.stubGlobal('fetch', reply(400, { code: 'INVALID_TITLE' }))
    const outcome = await createLab('x'.repeat(201), teacher)
    expect(outcome.kind).toBe('invalid')
    expect(outcome.kind === 'invalid' && outcome.messages[0]).toContain('najviše 200 znakova')
  })

  it('400 ne prikazuje proizvoljan tekst sa servera', async () => {
    vi.stubGlobal('fetch', reply(400, { code: 'SQL error near users table', detail: 'stack trace' }))
    const outcome = await createLab('Naslov', teacher)
    expect(outcome).toEqual({ kind: 'invalid', messages: ['Unos nije ispravan.'], requestId: undefined })
  })

  it('501 iz LAB-01 kostura i greška mreže imaju svoje ishode', async () => {
    vi.stubGlobal('fetch', reply(501))
    expect((await createLab('Naslov', teacher)).kind).toBe('notImplemented')

    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('offline')))
    expect((await createLab('Naslov', teacher)).kind).toBe('error')
  })
})

describe('LAB-07 getLabContent (fake dok endpoint ne postoji)', () => {
  it('sr i en imaju istu reviziju i isti evaluator, a različit tekst', async () => {
    const sr = await getLabContent(DEMO_LAB_ID, 'sr', student)
    const en = await getLabContent(DEMO_LAB_ID, 'en', student)
    if (sr.kind !== 'ok' || en.kind !== 'ok') throw new Error('očekivan uspeh')

    expect(en.content.revisionId).toBe(sr.content.revisionId)
    expect(en.content.evaluatorVersion).toBe(sr.content.evaluatorVersion)
    expect(en.content.title).not.toBe(sr.content.title)
    expect(sr.usedFallback).toBe(false)
    expect(en.usedFallback).toBe(false)
  })

  it('nepodržan jezik daje 400 sa porukom o jeziku', async () => {
    const outcome = await getLabContent(DEMO_LAB_ID, 'xx', student)
    expect(outcome.kind).toBe('invalid')
    expect(outcome.kind === 'invalid' && outcome.messages[0]).toContain('Jezik nije podržan')
  })

  it('ako prevod ne postoji, vraća se srpski i to je označeno', async () => {
    const outcome = await getLabContent(DEMO_XSS_LAB_ID, 'en', student)
    if (outcome.kind !== 'ok') throw new Error('očekivan uspeh')
    expect(outcome.content.language).toBe('sr')
    expect(outcome.usedFallback).toBe(true)
  })

  it('jezik iz LabContentView radi i kao broj (C# enum) i kao tekst', () => {
    expect(parseLanguage(0)).toBe('sr')
    expect(parseLanguage(1)).toBe('en')
    expect(parseLanguage('Sr')).toBe('sr')
    expect(parseLanguage('en')).toBe('en')
    expect(parseLanguage(2)).toBeNull()
    expect(parseLanguage('xx')).toBeNull()
  })

  it('nepostojeća laboratorija daje 404, a anonimni korisnik 401', async () => {
    expect((await getLabContent('00000000-0000-0000-0000-000000000000', 'sr', student)).kind).toBe('notFound')
    expect((await getLabContent(DEMO_LAB_ID, 'sr', null)).kind).toBe('unauthenticated')
  })
})

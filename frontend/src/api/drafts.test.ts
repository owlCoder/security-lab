import { afterEach, describe, expect, it, vi } from 'vitest'
import { apiFetch } from '../auth/apiClient'
import { createHttpDraftsGateway, DRAFTS_PATH, DraftsApiError, isDraftsApiEnabled } from './drafts'

const serverDraft = {
  id: '5d2f3f0e-7b1c-4a52-9e0b-1c2d3e4f5a6b',
  sourceTemplateId: null,
  ownerId: '11111111-1111-1111-1111-111111111111',
  status: 'Draft',
  version: 1,
  title: 'Pristup nastavnika',
  goal: 'Provera prava pristupa',
  type: 'AccessControl',
  correlationId: 'abc',
}

const expectedDraft = {
  id: serverDraft.id,
  title: serverDraft.title,
  goal: serverDraft.goal,
  type: 'AccessControl',
  status: 'Draft',
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.unstubAllEnvs()
})

describe('AUT-01 drafts API sloj', () => {
  it('učitava listu nacrta kroz zajednički zahtev sesije i prenosi samo prikazana polja', async () => {
    const signal = new AbortController().signal
    const request = vi.fn().mockResolvedValue(jsonResponse({ drafts: [serverDraft], correlationId: 'abc' }))

    const drafts = await createHttpDraftsGateway(request).list(signal)

    expect(drafts).toEqual([expectedDraft])
    expect(drafts[0]).not.toHaveProperty('ownerId')
    expect(request).toHaveBeenCalledExactlyOnceWith(DRAFTS_PATH, { method: 'GET', cache: 'no-store', signal })
  })

  it('pri kreiranju šalje samo naziv, cilj i tip, bez ID-a, statusa i vlasnika', async () => {
    const request = vi.fn().mockResolvedValue(jsonResponse(serverDraft, 201))

    const created = await createHttpDraftsGateway(request).create({
      title: 'Pristup nastavnika', goal: 'Provera prava pristupa', type: 'AccessControl',
    })

    expect(created).toEqual(expectedDraft)
    const [path, init] = request.mock.calls[0]
    expect(path).toBe(DRAFTS_PATH)
    expect(init.method).toBe('POST')
    expect(init.headers).toEqual({ 'Content-Type': 'application/json' })
    expect(JSON.parse(init.body)).toEqual({
      title: 'Pristup nastavnika', goal: 'Provera prava pristupa', type: 'AccessControl',
    })
  })

  it('prosleđuje kolačić sesije i odjavljuje korisnika na 401 preko zajedničkog zahteva', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ error: 'unauthenticated' }, 401))
    vi.stubGlobal('fetch', fetchMock)
    const onUnauthorized = vi.fn()
    const request = vi.fn((input: RequestInfo | URL, init?: RequestInit) => apiFetch(input, init, onUnauthorized))

    await expect(createHttpDraftsGateway(request).list()).rejects.toMatchObject({
      name: 'DraftsApiError', status: 401, message: 'Sesija je istekla. Prijavite se ponovo.',
    })
    expect(onUnauthorized).toHaveBeenCalledOnce()
    expect(fetchMock.mock.calls[0][1].credentials).toBe('same-origin')
  })

  it.each([
    [400, 'Podaci nacrta nisu prošli proveru. Proverite naziv, cilj i tip laboratorije.'],
    [422, 'Podaci nacrta nisu prošli proveru. Proverite naziv, cilj i tip laboratorije.'],
    [401, 'Sesija je istekla. Prijavite se ponovo.'],
    [403, 'Nemate pravo da radite sa nacrtima zadataka.'],
    [409, 'Podaci su u međuvremenu izmenjeni. Osvežite prikaz i pokušajte ponovo.'],
    [503, 'Nacrti trenutno nisu dostupni. Pokušajte ponovo.'],
    [500, 'Zahtev za nacrte nije uspeo. Pokušajte ponovo.'],
  ])('HTTP %s se prevodi u našu poruku, bez čitanja teksta sa servera', async (status, message) => {
    const response = jsonResponse({ error: 'private_database_secret', detail: 'private exception' }, status)
    const readJson = vi.spyOn(response, 'json')
    const request = vi.fn().mockResolvedValue(response)
    const gateway = createHttpDraftsGateway(request)

    const listError = await gateway.list().catch(caught => caught)
    expect(listError).toBeInstanceOf(DraftsApiError)
    expect(listError.status).toBe(status)
    expect(listError.message).toBe(message)
    expect(listError.message).not.toMatch(/private|secret|exception/)
    expect(readJson).not.toHaveBeenCalled()

    const createError = await gateway.create({ title: 'N', goal: '', type: 'AccessControl' }).catch(caught => caught)
    expect(createError.status).toBe(status)
  })

  it('mrežni izuzetak se zamenjuje bezbednom porukom', async () => {
    const request = vi.fn().mockRejectedValue(new TypeError('private network exception'))

    await expect(createHttpDraftsGateway(request).list()).rejects.toMatchObject({
      name: 'DraftsApiError', status: 0, message: 'Server nije dostupan. Proverite vezu i pokušajte ponovo.',
    })
  })

  it.each([
    null,
    {},
    { drafts: 'nije lista' },
    { drafts: [{ ...serverDraft, type: 'Nepoznat' }] },
    { drafts: [{ ...serverDraft, status: 'Nepoznat' }] },
    { drafts: [{ ...serverDraft, id: '' }] },
    { drafts: [{ ...serverDraft, title: 5 }] },
    { drafts: [null] },
  ])('odbacuje neispravnu listu bez korišćenja njenog sadržaja', async body => {
    const request = vi.fn().mockResolvedValue(jsonResponse(body))

    await expect(createHttpDraftsGateway(request).list()).rejects.toMatchObject({
      name: 'DraftsApiError', message: 'Server je vratio neočekivan odgovor za nacrte.',
    })
  })

  it('odbacuje kreiranje bez statusa 201 ili sa neispravnim nacrtom', async () => {
    const wrongStatus = vi.fn().mockResolvedValue(jsonResponse(serverDraft, 200))
    const wrongBody = vi.fn().mockResolvedValue(jsonResponse({ ...serverDraft, type: 'Nepoznat' }, 201))
    const data = { title: 'N', goal: '', type: 'AccessControl' as const }

    await expect(createHttpDraftsGateway(wrongStatus).create(data)).rejects.toMatchObject({
      message: 'Server je vratio neočekivan odgovor za nacrte.',
    })
    await expect(createHttpDraftsGateway(wrongBody).create(data)).rejects.toMatchObject({
      message: 'Server je vratio neočekivan odgovor za nacrte.',
    })
  })

  it('odbacuje JSON koji se ne može pročitati', async () => {
    const request = vi.fn().mockResolvedValue(new Response('private invalid response', { status: 200 }))

    await expect(createHttpDraftsGateway(request).list()).rejects.toMatchObject({
      message: 'Server je vratio neočekivan odgovor za nacrte.',
    })
  })

  it('ne poziva zahtev kada je signal već prekinut', async () => {
    const controller = new AbortController()
    controller.abort()
    const request = vi.fn()

    await expect(createHttpDraftsGateway(request).list(controller.signal)).rejects.toMatchObject({ name: 'AbortError' })
    expect(request).not.toHaveBeenCalled()
  })

  it('prekid zahteva se ne prikazuje kao greška servera', async () => {
    const aborted = new DOMException('Aborted', 'AbortError')
    const request = vi.fn().mockRejectedValue(aborted)

    await expect(createHttpDraftsGateway(request).list()).rejects.toBe(aborted)
  })

  it('serverski režim se uključuje samo sa VITE_DRAFTS_API=true', () => {
    expect(isDraftsApiEnabled()).toBe(false)

    vi.stubEnv('VITE_DRAFTS_API', 'false')
    expect(isDraftsApiEnabled()).toBe(false)

    vi.stubEnv('VITE_DRAFTS_API', 'true')
    expect(isDraftsApiEnabled()).toBe(true)
  })
})

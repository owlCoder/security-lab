import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { CSRF_COOKIE, CSRF_HEADER, CSRF_TOKEN_URL, csrfFetch, isStateChanging, readCsrfCookie } from '../auth/csrf'

// US-15 / SEC-03: the frontend sends the CSRF token by itself on every state-changing request.

type Call = { url: string; init?: RequestInit }

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const noContent = () => new Response(null, { status: 204 })

let calls: Call[]

// jsdom runs on plain http and, like a browser, refuses __Host- cookies there, so the cookie string is stubbed.
function setCookie(value: string | null) {
  const cookie = value === null ? 'other=1' : `other=1; ${CSRF_COOKIE}=${encodeURIComponent(value)}`
  Object.defineProperty(document, 'cookie', { configurable: true, get: () => cookie })
}

// The global fetch is the "server": GET /api/auth/csrf answers a fresh token, the rest goes to the handler.
function serve(handler: (call: Call) => Response, freshToken = 'fresh-token') {
  vi.stubGlobal('fetch', vi.fn(async (url: RequestInfo | URL, init?: RequestInit) => {
    const call = { url: String(url), init }
    calls.push(call)
    if (call.url === CSRF_TOKEN_URL) return json(200, { token: freshToken })
    return handler(call)
  }))
}

const headerOf = (call: Call) => new Headers(call.init?.headers).get(CSRF_HEADER)
const apiCalls = () => calls.filter(c => c.url !== CSRF_TOKEN_URL)
const tokenCalls = () => calls.filter(c => c.url === CSRF_TOKEN_URL)

beforeEach(() => {
  calls = []
  setCookie('cookie-token')
})

afterEach(() => {
  // Removes the stub, so document.cookie is the real jsdom property again.
  delete (document as { cookie?: string }).cookie
  vi.unstubAllGlobals()
})

describe('isStateChanging', () => {
  it.each(['POST', 'put', 'PATCH', 'DELETE'])('%s menja stanje', method => {
    expect(isStateChanging(method)).toBe(true)
  })
  it.each([undefined, 'GET', 'head', 'OPTIONS', 'TRACE'])('%s ne menja stanje', method => {
    expect(isStateChanging(method)).toBe(false)
  })
})

describe('readCsrfCookie', () => {
  it('cita token iz citljivog kolacica', () => {
    setCookie('abc_DEF-123')
    expect(readCsrfCookie()).toBe('abc_DEF-123')
  })
  it('bez kolacica vraca null', () => {
    setCookie(null)
    expect(readCsrfCookie()).toBeNull()
  })
})

describe('csrfFetch (US-15)', () => {
  it('POST dobija token iz kolacica u headeru, bez dodatnog poziva', async () => {
    serve(() => json(200, {}))

    await csrfFetch('/api/auth/login', { method: 'POST', body: '{}' })

    expect(tokenCalls()).toHaveLength(0)
    expect(apiCalls()).toHaveLength(1)
    expect(headerOf(apiCalls()[0])).toBe('cookie-token')
  })

  it('zahtev odlazi odmah (sinhrono), pa redosled i dvostruki klik ostaju isti', () => {
    serve(() => json(200, {}))

    void csrfFetch('/a', { method: 'POST' })

    expect(apiCalls()).toHaveLength(1)
  })

  it('GET ide bez tokena', async () => {
    serve(() => json(200, {}))

    await csrfFetch('/api/auth/me')

    expect(headerOf(apiCalls()[0])).toBeNull()
  })

  it('cuva postojece headere i njihov oblik', async () => {
    serve(() => json(200, {}))

    await csrfFetch('/a', { method: 'POST', headers: { 'Content-Type': 'application/json', 'Idempotency-Key': 'k1' } })

    const headers = apiCalls()[0].init?.headers as Record<string, string>
    expect(headers['Content-Type']).toBe('application/json')
    expect(headers['Idempotency-Key']).toBe('k1')
    expect(headers[CSRF_HEADER]).toBe('cookie-token')
  })

  it('posle 403 csrf_invalid uzima nov token i ponavlja zahtev tacno jednom', async () => {
    let attempts = 0
    serve(() => (++attempts === 1 ? json(403, { code: 'csrf_invalid' }) : noContent()))

    const response = await csrfFetch('/a', { method: 'POST', body: '{"x":1}' })

    expect(response.status).toBe(204)
    expect(tokenCalls()).toHaveLength(1)
    expect(apiCalls().map(headerOf)).toEqual(['cookie-token', 'fresh-token'])
    expect(apiCalls()[1].init?.body).toBe('{"x":1}')
  })

  it('bez kolacica prvi zahtev ide bez tokena, a ponovljeni sa novim', async () => {
    setCookie(null)
    serve(call => (headerOf(call) ? noContent() : json(403, { code: 'csrf_invalid' })))

    const response = await csrfFetch('/a', { method: 'DELETE' })

    expect(response.status).toBe(204)
    expect(apiCalls().map(headerOf)).toEqual([null, 'fresh-token'])
  })

  it('ne ponavlja zahtev u beskonacnost ako server i dalje odbija', async () => {
    serve(() => json(403, { code: 'csrf_invalid' }))

    const response = await csrfFetch('/a', { method: 'POST' })

    expect(response.status).toBe(403)
    expect(apiCalls()).toHaveLength(2)
  })

  it('drugi 403 (nema dozvolu) se ne ponavlja i telo ostaje citljivo', async () => {
    serve(() => json(403, { code: 'forbidden' }))

    const response = await csrfFetch('/a', { method: 'POST' })

    expect(apiCalls()).toHaveLength(1)
    expect(await response.json()).toEqual({ code: 'forbidden' })
  })

  it('kada server za token nije dostupan, vraca prvi odgovor bez pada', async () => {
    vi.stubGlobal('fetch', vi.fn(async (url: RequestInfo | URL, init?: RequestInit) => {
      calls.push({ url: String(url), init })
      if (String(url) === CSRF_TOKEN_URL) throw new TypeError('network down')
      return json(403, { code: 'csrf_invalid' })
    }))

    const response = await csrfFetch('/a', { method: 'POST' })

    expect(response.status).toBe(403)
    expect(apiCalls()).toHaveLength(2)
  })

  it('zahtev salje kroz zadatu funkciju (mock ili test), a novi token trazi od pravog servera', async () => {
    serve(() => json(200, {}))
    let attempts = 0
    const send = vi.fn(async () => (++attempts === 1 ? json(403, { code: 'csrf_invalid' }) : json(200, {})))

    await csrfFetch('/api/revisions/r1/approve', { method: 'POST' }, send)

    expect(send).toHaveBeenCalledTimes(2)
    expect(tokenCalls()).toHaveLength(1)
    const secondInit = (send.mock.calls[1] as unknown as [string, RequestInit])[1]
    expect(new Headers(secondInit.headers).get(CSRF_HEADER)).toBe('fresh-token')
  })
})

import { afterEach, describe, expect, it, vi } from 'vitest'
import { getLoginHistory, localDayEndIso, localDayStartIso } from './loginHistory'
import type { LoginHistoryFilter } from '../types/loginHistory'
// IDN-22 TIM 09

function json(status: number, body: unknown) {
  return new Response(JSON.stringify(body), { status })
}

function serverReturns(status: number, body: unknown) {
  const fetchMock = vi.fn(async (_url: string, _init?: RequestInit) => json(status, body))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function requestedUrl(fetchMock: ReturnType<typeof serverReturns>): URL {
  return new URL(String(fetchMock.mock.calls[0][0]), 'http://localhost')
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('IDN-22 login history API client', () => {
  it('getLoginHistory_WhenAllFiltersSet_SendsExactQuery', async () => {
    const fetchMock = serverReturns(200, { items: [], page: 2, pageSize: 10 })

    await getLoginHistory({
      from: '2026-11-01T00:00:00.000Z', to: '2026-11-10T23:59:59.999Z', outcome: 'failed', page: 2, pageSize: 10,
    })

    const url = requestedUrl(fetchMock)
    expect(url.pathname).toBe('/api/auth/login-history')
    expect(Object.fromEntries(url.searchParams)).toEqual({
      from: '2026-11-01T00:00:00.000Z', to: '2026-11-10T23:59:59.999Z', outcome: 'failed', page: '2', pageSize: '10',
    })
    expect(fetchMock.mock.calls[0][1]?.method).toBe('GET')
  })

  it('getLoginHistory_WhenOptionalFiltersEmpty_OmitsThem', async () => {
    const fetchMock = serverReturns(200, { items: [], page: 1, pageSize: 20 })

    await getLoginHistory({ from: '', to: undefined, page: 1, pageSize: 20 })

    const url = requestedUrl(fetchMock)
    expect([...url.searchParams.keys()]).toEqual(['page', 'pageSize'])
  })

  it('getLoginHistory_WhenCallerSmugglesUserId_NeverSendsIt', async () => {
    const fetchMock = serverReturns(200, { items: [], page: 1, pageSize: 20 })
    const smuggled = { page: 1, pageSize: 20, userId: 'u-other', subjectId: 'u-other' } as LoginHistoryFilter

    await getLoginHistory(smuggled)

    const raw = String(fetchMock.mock.calls[0][0])
    expect(raw).not.toContain('userId')
    expect(raw).not.toContain('subjectId')
    expect(raw).not.toContain('u-other')
  })

  it('getLoginHistory_WhenServerSucceeds_ReturnsPage', async () => {
    const page = { items: [{ occurredAt: '2026-11-10T09:00:00Z', succeeded: true, maskedSourceAddress: '203.0.113.*' }], page: 1, pageSize: 20 }
    serverReturns(200, page)

    const result = await getLoginHistory({ page: 1, pageSize: 20 })

    expect(result).toEqual({ ok: true, data: page })
  })

  it('getLoginHistory_WhenServerRejects_ReturnsErrorCode', async () => {
    serverReturns(400, { code: 'range_too_large', message: 'internal detail' })

    const result = await getLoginHistory({ page: 1, pageSize: 20 })

    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.error).toMatchObject({ status: 400, code: 'range_too_large' })
  })

  it('getLoginHistory_WhenAuthRequestGiven_SendsThroughItSoA401SignsOut', async () => {
    const globalFetch = serverReturns(200, { items: [], page: 1, pageSize: 20 })
    const request = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => json(401, { code: 'session_invalid' }))

    const result = await getLoginHistory({ page: 1, pageSize: 20 }, request)

    expect(request).toHaveBeenCalledTimes(1)
    expect(String(request.mock.calls[0][0])).toBe('/api/auth/login-history?page=1&pageSize=20')
    expect(globalFetch).not.toHaveBeenCalled()
    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.error).toMatchObject({ status: 401, code: 'session_invalid' })
  })

  it('getLoginHistory_WhenNetworkFails_ReturnsNetworkErrorWithoutDetails', async () => {
    const request = vi.fn(async () => { throw new TypeError('connect ECONNREFUSED 192.0.2.10:5000') })

    const result = await getLoginHistory({ page: 1, pageSize: 20 }, request)

    expect(result).toEqual({ ok: false, error: { status: 0, code: 'network_error', message: '' } })
  })

  it('localDayBounds_WhenValidDate_ReturnStartAndEndOfLocalDay', () => {
    expect(localDayStartIso('2026-11-01')).toBe(new Date(2026, 10, 1, 0, 0, 0, 0).toISOString())
    expect(localDayEndIso('2026-11-01')).toBe(new Date(2026, 10, 1, 23, 59, 59, 999).toISOString())
  })

  it('localDayBounds_WhenEmptyOrInvalid_ReturnUndefined', () => {
    expect(localDayStartIso('')).toBeUndefined()
    expect(localDayEndIso('not-a-date')).toBeUndefined()
    expect(localDayStartIso('2026-02-30')).toBeUndefined()
  })
})

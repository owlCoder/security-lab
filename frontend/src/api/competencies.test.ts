import { afterEach, describe, expect, it, vi } from 'vitest'
import { apiFetch } from '../auth/apiClient'
import { NetworkError } from '../models/ApiErrors'
import {
  addLaboratoryCompetency,
  CompetencyApiError,
  loadCompetencyCatalog,
  loadLaboratoryCompetencies,
  removeLaboratoryCompetency,
  type Competency,
  type LaboratoryCompetencies,
} from './competencies'

const laboratoryId = '30000000-0000-0000-0000-000000000001'
const competencyId = '4b677e63-204f-4688-a0eb-a339c343f101'
const competency: Competency = { id: competencyId, name: 'Kontrola pristupa', description: null }
const laboratory: LaboratoryCompetencies = {
  laboratoryId,
  laboratoryTitle: 'Provera kontrole pristupa',
  canManage: true,
  competencies: [competency],
}

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('PRG-04 competency API', () => {
  it('loads the laboratory title, server permission and links through the supplied session request', async () => {
    const signal = new AbortController().signal
    const request = vi.fn().mockResolvedValue(jsonResponse(laboratory))

    expect(await loadLaboratoryCompetencies(request, laboratoryId, signal)).toEqual(laboratory)
    expect(request).toHaveBeenCalledExactlyOnceWith(`/api/laboratories/${laboratoryId}/competencies`, {
      method: 'GET', cache: 'no-store', signal,
    })
    expect(request.mock.calls[0][1].headers).toBeUndefined()
    expect(request.mock.calls[0][1].body).toBeUndefined()
    expect(request.mock.calls[0][1].credentials).toBeUndefined()
  })

  it('loads the existing competency catalog without caching', async () => {
    const signal = new AbortController().signal
    const request = vi.fn().mockResolvedValue(jsonResponse({ competencies: [competency] }))

    expect(await loadCompetencyCatalog(request, signal)).toEqual([competency])
    expect(request).toHaveBeenCalledExactlyOnceWith('/api/competencies', {
      method: 'GET', cache: 'no-store', signal,
    })
  })

  it.each([201, 204])('adds an encoded link with PUT and handles bodyless %s without parsing JSON', async status => {
    const response = new Response(null, { status })
    const readJson = vi.spyOn(response, 'json')
    const request = vi.fn().mockResolvedValue(response)
    const signal = new AbortController().signal

    await expect(addLaboratoryCompetency(request, 'lab/a?x=1#part', 'skill/b?x=2#part', signal)).resolves.toBeUndefined()
    expect(request).toHaveBeenCalledExactlyOnceWith(
      '/api/laboratories/lab%2Fa%3Fx%3D1%23part/competencies/skill%2Fb%3Fx%3D2%23part',
      { method: 'PUT', cache: 'no-store', signal },
    )
    expect(readJson).not.toHaveBeenCalled()
  })

  it('removes an encoded link with DELETE and handles 204 without parsing JSON', async () => {
    const response = new Response(null, { status: 204 })
    const readJson = vi.spyOn(response, 'json')
    const request = vi.fn().mockResolvedValue(response)

    await expect(removeLaboratoryCompetency(request, 'lab/a', 'skill/b')).resolves.toBeUndefined()
    expect(request).toHaveBeenCalledExactlyOnceWith('/api/laboratories/lab%2Fa/competencies/skill%2Fb', {
      method: 'DELETE', cache: 'no-store', signal: undefined,
    })
    expect(readJson).not.toHaveBeenCalled()
  })

  it('delegates same-origin cookies and session invalidation to the authenticated request wrapper', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ code: 'session_invalid' }, 401))
    vi.stubGlobal('fetch', fetchMock)
    const onUnauthorized = vi.fn()
    const request = vi.fn((input: RequestInfo | URL, init?: RequestInit) => apiFetch(input, init, onUnauthorized))

    await expect(loadLaboratoryCompetencies(request, laboratoryId)).rejects.toMatchObject({
      name: 'CompetencyApiError', status: 401, message: 'Sesija je istekla. Prijavite se ponovo.',
    })
    expect(onUnauthorized).toHaveBeenCalledOnce()
    expect(fetchMock.mock.calls[0][1].credentials).toBe('same-origin')
    expect(fetchMock.mock.calls[0][1].headers).toBeUndefined()
  })

  it.each([401, 403, 404, 503])('maps HTTP %s to a safe local error without reading server details', async status => {
    const response = jsonResponse({ error: 'private_database_secret', detail: 'private exception' }, status)
    const readJson = vi.spyOn(response, 'json')
    const request = vi.fn().mockResolvedValue(response)

    const error = await loadLaboratoryCompetencies(request, laboratoryId).catch(caught => caught)
    expect(error).toBeInstanceOf(CompetencyApiError)
    expect(error.status).toBe(status)
    expect(error.message).not.toMatch(/private|secret|exception/)
    expect(readJson).not.toHaveBeenCalled()
  })

  it('does not distinguish missing laboratories from denied access in its displayed message', async () => {
    const denied = vi.fn().mockResolvedValue(jsonResponse({}, 403))
    const missing = vi.fn().mockResolvedValue(jsonResponse({}, 404))
    const deniedError = await loadLaboratoryCompetencies(denied, laboratoryId).catch(error => error)
    const missingError = await loadLaboratoryCompetencies(missing, laboratoryId).catch(error => error)

    expect(deniedError.message).toBe(missingError.message)
  })

  it('replaces network exception text with a safe message', async () => {
    const request = vi.fn().mockRejectedValue(new TypeError('private network exception'))

    await expect(loadCompetencyCatalog(request)).rejects.toMatchObject({
      name: 'CompetencyApiError', status: 0, message: 'Server nije dostupan. Proverite vezu i pokušajte ponovo.',
    })
  })

  it.each([
    { ...laboratory, laboratoryId: 'another-laboratory' },
    { ...laboratory, laboratoryTitle: '' },
    { ...laboratory, canManage: 'true' },
    { ...laboratory, competencies: [{ ...competency, description: 123 }] },
  ])('rejects malformed laboratory data without using its contents', async body => {
    const request = vi.fn().mockResolvedValue(jsonResponse(body))

    await expect(loadLaboratoryCompetencies(request, laboratoryId)).rejects.toMatchObject({
      name: 'CompetencyApiError', message: 'Server je vratio neočekivan odgovor za kompetencije.',
    })
  })

  it('rejects malformed catalog JSON safely', async () => {
    const request = vi.fn().mockResolvedValue(new Response('private invalid response', { status: 200 }))

    await expect(loadCompetencyCatalog(request)).rejects.toMatchObject({
      name: 'CompetencyApiError', message: 'Server je vratio neočekivan odgovor za kompetencije.',
    })
  })

  it('does not call the request after its signal has already been aborted', async () => {
    const controller = new AbortController()
    controller.abort()
    const request = vi.fn()

    await expect(loadCompetencyCatalog(request, controller.signal)).rejects.toMatchObject({ name: 'AbortError' })
    expect(request).not.toHaveBeenCalled()
  })

  it('preserves cancellation when AuthContext wraps a fetch abort as NetworkError', async () => {
    const controller = new AbortController()
    const request = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => {
      controller.abort()
      throw new NetworkError(controller.signal.reason)
    })

    await expect(loadCompetencyCatalog(request, controller.signal)).rejects.toMatchObject({ name: 'AbortError' })
    expect(request).toHaveBeenCalledWith('/api/competencies', expect.objectContaining({ signal: controller.signal }))
  })

  it('preserves a direct AbortError from the request', async () => {
    const aborted = new DOMException('Aborted', 'AbortError')
    const request = vi.fn().mockRejectedValue(aborted)

    await expect(loadCompetencyCatalog(request)).rejects.toBe(aborted)
  })
})

import { afterEach, describe, expect, it, vi } from 'vitest'
import { createLocalDraftsGateway } from './localDraftsGateway'

afterEach(() => {
  vi.unstubAllGlobals()
})

function stubBrowserApis() {
  let counter = 0
  const fetchSpy = vi.fn()
  vi.stubGlobal('fetch', fetchSpy)
  vi.stubGlobal('crypto', { randomUUID: () => `local-${++counter}` })
  return fetchSpy
}

describe('AUT-01 demo gateway', () => {
  it('kaže da se nacrti ne čuvaju na serveru', () => {
    expect(createLocalDraftsGateway().persistsOnServer).toBe(false)
  })

  it('kreira nacrt sa novim ID-jem i statusom Draft i vraća ga u listi', async () => {
    stubBrowserApis()
    const gateway = createLocalDraftsGateway()

    const created = await gateway.create({ title: 'Prvi', goal: 'Cilj', type: 'LogAnalysis' })

    expect(created).toEqual({ id: 'local-1', title: 'Prvi', goal: 'Cilj', type: 'LogAnalysis', status: 'Draft' })
    expect(await gateway.list()).toEqual([created])
  })

  it('izmena vraćenih objekata ne menja stanje gateway-a', async () => {
    stubBrowserApis()
    const gateway = createLocalDraftsGateway()
    const created = await gateway.create({ title: 'Prvi', goal: '', type: 'AccessControl' })

    created.title = 'Izmenjeno'
    ;(await gateway.list())[0].title = 'Izmenjeno'

    expect((await gateway.list())[0].title).toBe('Prvi')
  })

  it('ne šalje nijedan zahtev serveru', async () => {
    const fetchSpy = stubBrowserApis()
    const gateway = createLocalDraftsGateway()

    await gateway.create({ title: 'Prvi', goal: '', type: 'AccessControl' })
    await gateway.list()

    expect(fetchSpy).not.toHaveBeenCalled()
  })
})

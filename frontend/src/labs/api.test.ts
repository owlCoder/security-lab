import { afterEach, describe, expect, it, vi } from 'vitest'
import { session, setFetcher } from '../api/http'
import { getLabPreview, getStudentLab } from './api'

const LAB_ID = '44444444-4444-4444-4444-444444444444'

const view = {
  labId: LAB_ID,
  title: 'Test laboratorija',
  goal: 'Test cilj',
  tasks: [],
}

afterEach(() => {
  vi.unstubAllEnvs()
  session.set(null)
  setFetcher((input, init) => fetch(input, init))
})

const endpoints = [
  { name: 'Studentski API', load: getStudentLab },
  { name: 'Preview API', load: getLabPreview },
]

describe.each(endpoints)('$name', ({ load }) => {
  it('šalje izabrani razvojni identitet u developmentu', async () => {
    vi.stubEnv('DEV', true)

    const receivedHeaders: Headers[] = []

    setFetcher(async (input, init) => {
      receivedHeaders.push(new Headers(init?.headers))

      const body = input.endsWith('/preview')
        ? { isPreview: true, state: 'Draft', view }
        : view

      return new Response(JSON.stringify(body), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    })

    const result = await load(LAB_ID, 'teacher')

    expect(result.ok).toBe(true)
    expect(receivedHeaders).toHaveLength(1)
    expect(receivedHeaders[0].get('X-Stub-User')).toBe(
      '11111111-1111-1111-1111-111111111111',
    )
  })

  it('izostavlja razvojni identitet u produkciji', async () => {
    vi.stubEnv('DEV', false)

    const receivedHeaders: Headers[] = []

    setFetcher(async (input, init) => {
      receivedHeaders.push(new Headers(init?.headers))

      const body = input.endsWith('/preview')
        ? { isPreview: true, state: 'Draft', view }
        : view

      return new Response(JSON.stringify(body), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    })

    const result = await load(LAB_ID, 'teacher')

    expect(result.ok).toBe(true)
    expect(receivedHeaders).toHaveLength(1)
    expect(receivedHeaders[0].has('X-Stub-User')).toBe(false)
  })
})
import type { StudentLabView } from './types'

export const PUBLISHED_LAB_ID = '44444444-4444-4444-4444-444444444444'
export const DRAFT_LAB_ID = '55555555-5555-5555-5555-555555555555'

const TEACHER_ID = '11111111-1111-1111-1111-111111111111'
const STUDENT_ID = '22222222-2222-2222-2222-222222222222'

function json(status: number, body: object): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function unavailable(): Response {
  return json(404, {
    code: 'not_found',
    message: 'Laboratorija nije dostupna',
  })
}

export function handleLabMock(
  input: string,
  init?: RequestInit,
): Response | undefined {
  if (!import.meta.env.DEV) {
    return undefined
  }

  const path = new URL(input, 'http://mock').pathname
  const match = path.match(/^\/api\/labs\/([^/]+)\/(student-view|preview)$/)

  if (!match || (init?.method ?? 'GET') !== 'GET') {
    return undefined
  }

  const userId = new Headers(init?.headers).get('X-Stub-User')

  if (!userId) {
    return json(401, {
      code: 'unauthenticated',
      message: 'Laboratorija nije dostupna',
    })
  }

  const labId = match[1]
  const isPreview = match[2] === 'preview'
  const isPublished = labId === PUBLISHED_LAB_ID
  const exists = isPublished || labId === DRAFT_LAB_ID

  if (!exists) {
    return unavailable()
  }

  if (isPreview) {
    if (userId !== TEACHER_ID) {
      return unavailable()
    }
  } else if (
    !isPublished ||
    (userId !== STUDENT_ID && userId !== TEACHER_ID)
  ) {
    return unavailable()
  }

  const view: StudentLabView = {
    labId,
    title: 'Osnove kontrole pristupa',
    goal: 'Razlikovanje autentikacije i autorizacije.',
    tasks: [
      {
        taskId: 'task-1',
        order: 1,
        prompt: 'Objasni razliku između autentikacije i autorizacije.',
        options: [],
      },
      {
        taskId: 'task-2',
        order: 2,
        prompt: 'Navedi primer provere pristupa laboratoriji.',
        options: [],
      },
    ],
  }

  if (isPreview) {
    return json(200, {
      isPreview: true,
      state: isPublished ? 'Published' : 'Draft',
      view,
    })
  }

  return json(200, view)
}
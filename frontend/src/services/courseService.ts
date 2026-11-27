
import type { Course, CreateCourseRequest } from '../types/course'

const API_URL = '/api/courses'

const devHeaders: Record<string, string> =
  import.meta.env.DEV &&
  import.meta.env.VITE_ACC01_DEV_AUTH === 'true'
    ? {
        'X-Test-User':
          '00000000-0000-0000-0000-000000000001',
      }
    : {}

async function checkResponse(
  response: Response,
): Promise<void> {
  if (response.ok) {
    return
  }

  let message: string

  switch (response.status) {
    case 400:
    case 422:
      message = 'Podaci kursa nisu ispravni.'
      break

    case 401:
      message = 'Potrebno je da se prijavite.'
      break

    case 403:
      message = 'Nemate dozvolu za ovu akciju.'
      break

    case 404:
      message = 'Kurs nije pronađen.'
      break

    case 409:
      message =
        'Došlo je do konflikta. Proverite podatke i pokušajte ponovo.'
      break

    default:
      message = `Greška servera (${response.status}).`
  }

  if (
    response.status === 400 ||
    response.status === 422 ||
    response.status === 409
  ) {
    try {
      const error = await response.json()

      if (typeof error?.detail === 'string') {
        message = error.detail
      } else if (typeof error?.message === 'string') {
        message = error.message
      }
    } catch {
      // Zadržavamo podrazumevanu poruku.
    }
  }

  throw new Error(message)
}

// GET /api/courses
export async function listCourses(
  signal?: AbortSignal,
): Promise<Course[]> {
  const response = await fetch(API_URL, {
    method: 'GET',
    credentials: 'same-origin',
    cache: 'no-store',
    headers: devHeaders,
    signal,
  })

  await checkResponse(response)

  return (await response.json()) as Course[]
}

// GET /api/courses/{id}
export async function getCourse(
  id: string,
  signal?: AbortSignal,
): Promise<Course> {
  const response = await fetch(
    `${API_URL}/${encodeURIComponent(id)}`,
    {
      method: 'GET',
      credentials: 'same-origin',
      cache: 'no-store',
      headers: devHeaders,
      signal,
    },
  )

  await checkResponse(response)

  return (await response.json()) as Course
}

// POST /api/courses
export async function createCourse(
  request: CreateCourseRequest,
): Promise<Course> {
  const response = await fetch(API_URL, {
    method: 'POST',
    credentials: 'same-origin',
    cache: 'no-store',
    headers: {
      'Content-Type': 'application/json',
      ...devHeaders,
    },
    body: JSON.stringify(request),
  })

  await checkResponse(response)

  return (await response.json()) as Course
}

// POST /api/courses/{id}/archive
export async function archiveCourse(
  id: string,
): Promise<void> {
  const response = await fetch(
    `${API_URL}/${encodeURIComponent(id)}/archive`,
    {
      method: 'POST',
      credentials: 'same-origin',
      cache: 'no-store',
      headers: devHeaders,
    },
  )

  await checkResponse(response)
}

export const courseService = {
  listCourses,
  getCourse,
  createCourse,
  archiveCourse,
}

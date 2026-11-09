
import type { Course, CreateCourseRequest } from '../types/course'

const API_URL = '/api/courses'

// Testni identitet koristi se iskljucivo u Development okruzenju.
const devHeaders: Record<string, string> = import.meta.env.DEV
  ? {
      'X-Test-User': '00000000-0000-0000-0000-000000000001',
    }
  : {}

async function checkResponse(response: Response): Promise<void> {
  if (response.ok) {
    return
  }

  let message = `HTTP greška ${response.status}`

  try {
    const error = await response.json()

    if (typeof error?.detail === 'string') {
      message = error.detail
    } else if (typeof error?.title === 'string') {
      message = error.title
    } else if (typeof error?.message === 'string') {
      message = error.message
    }
  } catch {
    // Ako odgovor nije JSON, zadrzavamo HTTP status.
  }

  throw new Error(message)
}

// GET /api/courses
export async function listCourses(): Promise<Course[]> {
  const response = await fetch(API_URL, {
    method: 'GET',
    headers: devHeaders,
  })

  await checkResponse(response)

  return (await response.json()) as Course[]
}

// GET /api/courses/{id}
export async function getCourse(id: string): Promise<Course> {
  const response = await fetch(
    `${API_URL}/${encodeURIComponent(id)}`,
    {
      method: 'GET',
      headers: devHeaders,
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
export async function archiveCourse(id: string): Promise<void> {
  const response = await fetch(
    `${API_URL}/${encodeURIComponent(id)}/archive`,
    {
      method: 'POST',
      headers: devHeaders,
    },
  )

  await checkResponse(response)

  // HTTP 204 No Content nema JSON telo.
}

export const courseService = {
  listCourses,
  getCourse,
  createCourse,
  archiveCourse,
}
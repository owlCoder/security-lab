
import type { Course, CreateCourseRequest } from '../types/course'

export function validateCourse(input: CreateCourseRequest): string | null {
  if (!input.code.trim()) {
    return 'Šifra kursa je obavezna.'
  }

  if (input.code.trim().length > 32) {
    return 'Šifra kursa može imati najviše 32 karaktera.'
  }

  if (!input.name.trim()) {
    return 'Naziv kursa je obavezan.'
  }

  if (input.name.trim().length > 200) {
    return 'Naziv kursa može imati najviše 200 karaktera.'
  }

  if ((input.description ?? '').trim().length > 4000) {
    return 'Opis može imati najviše 4000 karaktera.'
  }

  return null
}

export function normalizeCourse(
  input: CreateCourseRequest
): CreateCourseRequest {
  return {
    code: input.code.trim().toUpperCase(),
    name: input.name.trim(),
    description: input.description?.trim() || null,
  }
}

export function markArchived(
  courses: Course[],
  courseId: string
): Course[] {
  return courses.map((course) =>
    course.id === courseId
      ? { ...course, status: 'Archived' }
      : course
  )
}

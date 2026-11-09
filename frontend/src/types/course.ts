
export type CourseStatus = 'Active' | 'Archived'

export type CourseFilter = 'all' | CourseStatus

export interface Course {
  id: string
  code: string
  name: string
  description: string | null
  status: CourseStatus
  createdAt: string
  archivedAt: string | null
}

export interface CreateCourseRequest {
  code: string
  name: string
  description?: string | null
}

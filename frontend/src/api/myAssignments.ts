import { apiRequest, type ApiResult } from './http'

// ASN-02 (team 16): the logged-in student's assignments.
// The server picks the student from the session cookie and filters by active enrollment.
// The client never sends a student ID and makes no access decisions.

export const MY_ASSIGNMENTS_PATH = '/api/me/assignments'

export type MyAssignment = {
  assignmentId: string
  courseId: string
  publishedLabRevisionId: string
  availableFromUtc: string | null
  dueAtUtc: string
  maxAttempts: number
}

type MyAssignmentsResponse = {
  assignments: MyAssignment[]
  correlationId: string
}

function isMyAssignment(value: unknown): value is MyAssignment {
  if (!value || typeof value !== 'object') return false
  const a = value as Record<string, unknown>
  return typeof a.assignmentId === 'string'
    && typeof a.courseId === 'string'
    && typeof a.publishedLabRevisionId === 'string'
    && (a.availableFromUtc === null || typeof a.availableFromUtc === 'string')
    && typeof a.dueAtUtc === 'string'
    && typeof a.maxAttempts === 'number'
}

// Only the documented fields are kept, so anything extra the server might add is never rendered.
function toMyAssignment(a: MyAssignment): MyAssignment {
  return {
    assignmentId: a.assignmentId,
    courseId: a.courseId,
    publishedLabRevisionId: a.publishedLabRevisionId,
    availableFromUtc: a.availableFromUtc,
    dueAtUtc: a.dueAtUtc,
    maxAttempts: a.maxAttempts,
  }
}

export async function getMyAssignments(): Promise<ApiResult<MyAssignment[]>> {
  const result = await apiRequest<MyAssignmentsResponse>('GET', MY_ASSIGNMENTS_PATH)
  if (!result.ok) return result

  const list = result.data?.assignments
  if (!Array.isArray(list) || !list.every(isMyAssignment)) {
    return { ok: false, error: { status: 200, code: 'invalid_response', message: 'Server je vratio neočekivan odgovor.' } }
  }
  return { ok: true, data: list.map(toMyAssignment) }
}

/**
 * Formats a UTC timestamp in the viewer's local time and names the time zone. Display only.
 * ASN-15 asks for the course time zone; the API does not return it yet.
 */
export function formatUtcForDisplay(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  // dateStyle/timeStyle cannot be combined with timeZoneName, so the parts are listed explicitly.
  return d.toLocaleString('sr-Latn-RS', {
    day: 'numeric', month: 'short', year: 'numeric',
    hour: '2-digit', minute: '2-digit',
    timeZoneName: 'short',
  })
}

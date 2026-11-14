import { apiRequest } from '../api/http'
import type { ApiResult } from '../api/http'
import type { StudentLabView } from './types'

export type LabDevUser = 'teacher' | 'student' | 'outsider'

const DEV_USER_IDS: Record<LabDevUser, string> = {
  teacher: '11111111-1111-1111-1111-111111111111',
  student: '22222222-2222-2222-2222-222222222222',
  outsider: '33333333-3333-3333-3333-333333333333',
}

type LabPreviewResponse = {
  isPreview: boolean
  view: StudentLabView
}

export const LAB_UNAVAILABLE_MESSAGE = 'Laboratorija nije dostupna'

function getDevHeaders(user: LabDevUser): Record<string, string> {
  if (import.meta.env.DEV) {
    return { 'X-Stub-User': DEV_USER_IDS[user] }
  }

  return {}
}

function normalizeLabResult<T>(result: ApiResult<T>): ApiResult<T> {
  if (result.ok) {
    return result
  }

  const status = result.error.status

  if (status === 401 || status === 403 || status === 404) {
    return {
      ok: false,
      error: {
        status: 404,
        code: 'lab_unavailable',
        message: LAB_UNAVAILABLE_MESSAGE,
      },
    }
  }

  return result
}

export async function getStudentLab(
  labId: string,
  user: LabDevUser,
): Promise<ApiResult<StudentLabView>> {
  const result = await apiRequest<StudentLabView>(
    'GET',
    `/api/labs/${encodeURIComponent(labId)}/student-view`,
    undefined,
    getDevHeaders(user),
  )

  return normalizeLabResult(result)
}

export async function getLabPreview(
  labId: string,
  user: LabDevUser,
): Promise<ApiResult<StudentLabView>> {
  const result = await apiRequest<LabPreviewResponse>(
    'GET',
    `/api/labs/${encodeURIComponent(labId)}/preview`,
    undefined,
    getDevHeaders(user),
  )

  const normalized = normalizeLabResult(result)

  if (!normalized.ok) {
    return normalized
  }

  return { ok: true, data: normalized.data.view }
}
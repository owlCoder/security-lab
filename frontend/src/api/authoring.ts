import { apiRequest, type ApiError, type ApiResult } from './http'
import type { LabDraft, LabTemplate } from '../types/authoring'

const base = '/api/authoring'
const enc = encodeURIComponent

type TemplateListResponse = { templates: LabTemplate[]; correlationId: string }

export async function listTemplates(): Promise<ApiResult<LabTemplate[]>> {
  const result = await apiRequest<TemplateListResponse>('GET', `${base}/templates`)
  return result.ok ? { ok: true, data: result.data.templates } : result
}

export function createDraftFromTemplate(templateId: string): Promise<ApiResult<LabDraft>> {
  return apiRequest('POST', `${base}/templates/${enc(templateId)}/drafts`)
}

export function authoringErrorMessage(error: ApiError): string {
  switch (error.status) {
    case 401: return 'Niste prijavljeni.'
    case 403: return 'Samo autori mogu da kreiraju zadatke iz šablona.'
    case 404: return 'Šablon ne postoji.'
    case 409: return 'Došlo je do konflikta. Osvežite prikaz i pokušajte ponovo.'
    default: return error.message
  }
}
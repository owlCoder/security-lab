import { apiRequest, type ApiResult } from './http'

export type RevisionStatus = 'Draft' | 'InReview' | 'Published' | 'Archived'
export type RevisionAction = 'submit' | 'approve' | 'return'
export type DecisionType = 'Submitted' | 'Approved' | 'Returned'

export type Person = { id: string; displayName: string }

export type RevisionSummary = {
  id: string
  labId: string
  labTitle: string
  courseId: string
  courseName: string
  status: RevisionStatus
  revisionNumber: number | null
  author: Person
  submittedAt: string | null
  updatedAt: string
}

export type Decision = {
  id: string
  type: DecisionType
  actor: Person
  at: string
  comment: string | null
  contentHash: string
  revisionNumber: number | null
}

export type RevisionDetail = RevisionSummary & {
  contentHash: string
  rowVersion: string
  policy: { requiresSeparateApprover: boolean }
  allowedActions: RevisionAction[]
  decisions: Decision[]
}

export const COMMENT_MAX_LENGTH = 1000

const base = '/api/revisions'
const enc = encodeURIComponent

export function getPendingReviews(courseId?: string): Promise<ApiResult<RevisionSummary[]>> {
  const q = courseId ? `?courseId=${enc(courseId)}` : ''
  return apiRequest('GET', `${base}/pending-reviews${q}`)
}

export function getMyRevisions(): Promise<ApiResult<RevisionSummary[]>> {
  return apiRequest('GET', `${base}/mine`)
}

export function getRevision(id: string): Promise<ApiResult<RevisionDetail>> {
  return apiRequest('GET', `${base}/${enc(id)}`)
}


export function runRevisionAction(
  revision: Pick<RevisionDetail, 'id' | 'rowVersion' | 'contentHash'>,
  action: RevisionAction,
  idempotencyKey: string,
  comment?: string,
): Promise<ApiResult<RevisionDetail>> {
  const body: Record<string, string> = {
    rowVersion: revision.rowVersion,
    contentHash: revision.contentHash,
  }
  const trimmed = comment?.trim()
  if (trimmed) body.comment = trimmed
  return apiRequest('POST', `${base}/${enc(revision.id)}/${action}`, body, { 'Idempotency-Key': idempotencyKey })
}

export function newIdempotencyKey(): string {
  return crypto.randomUUID()
}

export const STATUS_LABELS: Record<RevisionStatus, string> = {
  Draft: 'Nacrt',
  InReview: 'Na pregledu',
  Published: 'Objavljena',
  Archived: 'Arhivirana',
}

export const DECISION_LABELS: Record<DecisionType, string> = {
  Submitted: 'Predata na pregled',
  Approved: 'Odobrena i objavljena',
  Returned: 'Vraćena na doradu',
}

export function actionErrorMessage(code: string, fallback: string): string {
  switch (code) {
    case 'self_approval_forbidden':
      return 'Ne možete odobriti sopstvenu reviziju. Politika kursa zahteva drugog odobravaoca.'
    case 'revision_changed':
      return 'Revizija je u međuvremenu izmenjena. Osvežite prikaz i pregledajte novu verziju pre odluke.'
    case 'invalid_state':
      return 'Ova radnja više nije dozvoljena u trenutnom stanju revizije. Osvežite prikaz.'
    case 'validation_failed':
      return 'Revizija nije prošla proveru pre objave (REV-02).'
    case 'not_found':
      return 'Revizija ne postoji ili nemate pristup.'
    case 'forbidden':
      return 'Nemate dozvolu za ovu radnju.'
    default:
      return fallback
  }
}

export function formatDateTime(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  return d.toLocaleString('sr-RS', { dateStyle: 'medium', timeStyle: 'short' })
}

export function shortHash(hash: string): string {
  const [algo, value] = hash.includes(':') ? hash.split(':', 2) : ['', hash]
  return (algo ? algo + ':' : '') + value.slice(0, 10)
}
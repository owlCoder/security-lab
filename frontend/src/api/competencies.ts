import type { AuthContextValue } from '../types/auth'

export type Competency = {
  id: string
  name: string
  description: string | null
}

export type LaboratoryCompetencies = {
  laboratoryId: string
  laboratoryTitle: string
  canManage: boolean
  competencies: Competency[]
}

type CompetencyRequest = AuthContextValue['request']

const invalidResponseMessage = 'Server je vratio neočekivan odgovor za kompetencije.'

export class CompetencyApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'CompetencyApiError'
    this.status = status
  }
}

function errorMessage(status: number): string {
  switch (status) {
    case 400: return 'Proverite laboratoriju i izabranu kompetenciju.'
    case 401: return 'Sesija je istekla. Prijavite se ponovo.'
    case 403:
    case 404: return 'Laboratorija nije dostupna ili nemate pravo pristupa.'
    case 503: return 'Kompetencije trenutno nisu dostupne. Pokušajte ponovo.'
    default: return 'Zahtev za kompetencije nije uspeo. Pokušajte ponovo.'
  }
}

function rethrowAbort(error: unknown, signal?: AbortSignal): void {
  signal?.throwIfAborted()
  if (isRecord(error) && error.name === 'AbortError') throw error
}

async function send(
  request: CompetencyRequest,
  path: string,
  method: 'GET' | 'PUT' | 'DELETE',
  signal?: AbortSignal,
): Promise<Response> {
  signal?.throwIfAborted()
  let response: Response
  try {
    response = await request(path, { method, cache: 'no-store', signal })
  } catch (error) {
    rethrowAbort(error, signal)
    throw new CompetencyApiError(0, 'Server nije dostupan. Proverite vezu i pokušajte ponovo.')
  }
  signal?.throwIfAborted()
  if (!response.ok) throw new CompetencyApiError(response.status, errorMessage(response.status))
  return response
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

function isCompetency(value: unknown): value is Competency {
  return isRecord(value)
    && typeof value.id === 'string' && value.id.trim() !== ''
    && typeof value.name === 'string' && value.name.trim() !== ''
    && (value.description === null || typeof value.description === 'string')
}

function isCompetencyList(value: unknown): value is Competency[] {
  return Array.isArray(value) && value.every(isCompetency)
}

async function readJson(response: Response, signal?: AbortSignal): Promise<unknown> {
  try {
    const body: unknown = await response.json()
    signal?.throwIfAborted()
    return body
  } catch (error) {
    rethrowAbort(error, signal)
    throw new CompetencyApiError(response.status, invalidResponseMessage)
  }
}

function laboratoryPath(laboratoryId: string): string {
  return `/api/laboratories/${encodeURIComponent(laboratoryId)}/competencies`
}

export async function loadLaboratoryCompetencies(
  request: CompetencyRequest,
  laboratoryId: string,
  signal?: AbortSignal,
): Promise<LaboratoryCompetencies> {
  const response = await send(request, laboratoryPath(laboratoryId), 'GET', signal)
  const body = await readJson(response, signal)
  if (!isRecord(body)
    || typeof body.laboratoryId !== 'string'
    || body.laboratoryId.toLowerCase() !== laboratoryId.toLowerCase()
    || typeof body.laboratoryTitle !== 'string' || body.laboratoryTitle.trim() === ''
    || typeof body.canManage !== 'boolean'
    || !isCompetencyList(body.competencies)) {
    throw new CompetencyApiError(response.status, invalidResponseMessage)
  }
  return {
    laboratoryId: body.laboratoryId,
    laboratoryTitle: body.laboratoryTitle,
    canManage: body.canManage,
    competencies: body.competencies,
  }
}

export async function loadCompetencyCatalog(
  request: CompetencyRequest,
  signal?: AbortSignal,
): Promise<Competency[]> {
  const response = await send(request, '/api/competencies', 'GET', signal)
  const body = await readJson(response, signal)
  if (!isRecord(body) || !isCompetencyList(body.competencies)) {
    throw new CompetencyApiError(response.status, invalidResponseMessage)
  }
  return body.competencies
}

export async function addLaboratoryCompetency(
  request: CompetencyRequest,
  laboratoryId: string,
  competencyId: string,
  signal?: AbortSignal,
): Promise<void> {
  const path = `${laboratoryPath(laboratoryId)}/${encodeURIComponent(competencyId)}`
  const response = await send(request, path, 'PUT', signal)
  if (response.status !== 201 && response.status !== 204) {
    throw new CompetencyApiError(response.status, invalidResponseMessage)
  }
}

export async function removeLaboratoryCompetency(
  request: CompetencyRequest,
  laboratoryId: string,
  competencyId: string,
  signal?: AbortSignal,
): Promise<void> {
  const path = `${laboratoryPath(laboratoryId)}/${encodeURIComponent(competencyId)}`
  const response = await send(request, path, 'DELETE', signal)
  if (response.status !== 204) throw new CompetencyApiError(response.status, invalidResponseMessage)
}

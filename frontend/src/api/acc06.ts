export type Group = {
  id: string
  courseId: string
  name: string
  status: 'Active' | 'Archived'
}

export type GroupMember = {
  id: string
  groupId: string
  userId: string
  joinedAt: string
}

export type Material = {
  type: string
  materialId: string
}

export class Acc06ApiError extends Error {
  constructor(public readonly status: number, message: string) {
    super(message)
  }
}

function coursePath(courseId: string) {
  return `/api/courses/${encodeURIComponent(courseId)}`
}

function groupsPath(courseId: string) {
  return `${coursePath(courseId)}/groups`
}

function membersPath(courseId: string, groupId: string) {
  return `${groupsPath(courseId)}/${encodeURIComponent(groupId)}/members`
}

function materialPath(material: Material) {
  const type = encodeURIComponent(material.type)
  const id = encodeURIComponent(material.materialId)
  return `${type}/${id}`
}

async function request(
  userId: string,
  path: string,
  method = 'GET',
  body?: object,
): Promise<Response> {
  const headers = new Headers()

  if (userId) {
    headers.set('X-Test-User', userId)
  }

  if (body !== undefined) {
    headers.set('Content-Type', 'application/json')
  }

  let response: Response

  try {
    response = await fetch(path, {
      method,
      headers,
      credentials: 'same-origin',
      cache: 'no-store',
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch {
    throw new Error('API nije dostupan. Proveri da li je backend pokrenut.')
  }

  if (response.ok) return response

  if (response.status === 401) {
    throw new Acc06ApiError(401, 'Izaberi demo korisnika.')
  }

  if (response.status === 404) {
    throw new Acc06ApiError(404, 'Resurs nije dostupan.')
  }

  const problem = (await response.json().catch(() => null)) as
    | { detail?: string }
    | null

  throw new Acc06ApiError(
    response.status,
    problem?.detail ?? `Zahtev nije uspeo (HTTP ${response.status}).`,
  )
}

export async function listGroups(
  userId: string,
  courseId: string,
): Promise<Group[]> {
  const response = await request(userId, groupsPath(courseId))
  return response.json()
}

export async function createGroup(
  userId: string,
  courseId: string,
  name: string,
): Promise<Group> {
  const response = await request(userId, groupsPath(courseId), 'POST', { name })
  return response.json()
}

export async function listMembers(
  userId: string,
  courseId: string,
  groupId: string,
): Promise<GroupMember[]> {
  const response = await request(userId, membersPath(courseId, groupId))
  return response.json()
}

export async function addMember(
  userId: string,
  courseId: string,
  groupId: string,
  studentId: string,
): Promise<GroupMember> {
  const response = await request(
    userId,
    membersPath(courseId, groupId),
    'POST',
    { userId: studentId },
  )

  return response.json()
}

export async function removeMember(
  userId: string,
  courseId: string,
  groupId: string,
  studentId: string,
): Promise<void> {
  const path = `${membersPath(courseId, groupId)}/${encodeURIComponent(studentId)}`
  await request(userId, path, 'DELETE')
}

export async function listVisibleMaterials(
  userId: string,
  courseId: string,
): Promise<Material[]> {
  const response = await request(userId, `${coursePath(courseId)}/materials`)
  return response.json()
}

export async function readMaterial(
  userId: string,
  material: Material,
): Promise<Material> {
  const response = await request(userId, `/api/materials/${materialPath(material)}`)
  return response.json()
}
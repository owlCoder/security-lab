import { setFetcher, type Fetcher } from './http'
import type { Decision, Person, RevisionAction, RevisionDetail, RevisionStatus, RevisionSummary } from './revisions'

const MOCK_PASSWORD = 'demo-lozinka'
const MAX_FAILURES = 5
const LOCK_SECONDS = 30

type MockUser = Person & { email: string; teacher: boolean }
const USERS: MockUser[] = [
  { id: 'u-student', email: 'student@example.com', displayName: 'Student Demo', teacher: false },
  { id: 'u-petar', email: 'nastavnik@example.com', displayName: 'Petar Perić', teacher: true },
  { id: 'u-ana', email: 'nastavnik2@example.com', displayName: 'Ana Anić', teacher: true },
]
const person = (u: MockUser): Person => ({ id: u.id, displayName: u.displayName })
const byId = (id: string) => USERS.find(u => u.id === id)!

let failures = 0
let lockedUntil = 0

type MockRevision = {
  id: string; labId: string; labTitle: string; status: RevisionStatus
  authorId: string; requiresSeparateApprover: boolean; contentHash: string
  version: number; revisionNumber: number | null; submittedAt: string | null; updatedAt: string
  decisions: Decision[]; invalid?: boolean
}
const COURSE = { id: 'c-oib', name: 'OIB 2026' }
const labCounters: Record<string, number> = { 'lab-sql': 2, 'lab-xss': 0, 'lab-pwd': 1, 'lab-csrf': 0 }
let seq = 0
const nextId = (p: string) => `${p}-${++seq}`
const hash = () => 'sha256:' + Array.from({ length: 64 }, () => '0123456789abcdef'[Math.floor(Math.random() * 16)]).join('')
const ago = (min: number) => new Date(Date.now() - min * 60_000).toISOString()

function decision(type: Decision['type'], actorId: string, at: string, contentHash: string, comment: string | null = null, revisionNumber: number | null = null): Decision {
  return { id: nextId('d'), type, actor: person(byId(actorId)), at, comment, contentHash, revisionNumber }
}

const h1 = hash(), h2 = hash(), h3 = hash(), h4 = hash()
const revisions: MockRevision[] = [
  {
    id: 'rev-sql', labId: 'lab-sql', labTitle: 'SQL injection – osnove', status: 'InReview', authorId: 'u-ana',
    requiresSeparateApprover: true, contentHash: h1, version: 3, revisionNumber: null, submittedAt: ago(30), updatedAt: ago(30),
    decisions: [
      decision('Submitted', 'u-ana', ago(300), h1),
      decision('Returned', 'u-petar', ago(200), h1, 'Zadatak 2 nema hint, a evaluator ne proverava UNION upit.'),
      decision('Submitted', 'u-ana', ago(30), h1),
    ],
  },
  {
    id: 'rev-xss', labId: 'lab-xss', labTitle: 'XSS – reflektovani', status: 'InReview', authorId: 'u-petar',
    requiresSeparateApprover: true, contentHash: h2, version: 1, revisionNumber: null, submittedAt: ago(60), updatedAt: ago(60),
    decisions: [decision('Submitted', 'u-petar', ago(60), h2)],
  },
  {
    id: 'rev-pwd', labId: 'lab-pwd', labTitle: 'Lozinke i heširanje', status: 'Draft', authorId: 'u-petar',
    requiresSeparateApprover: true, contentHash: h3, version: 1, revisionNumber: null, submittedAt: null, updatedAt: ago(10),
    decisions: [],
  },
  {
    id: 'rev-csrf', labId: 'lab-csrf', labTitle: 'CSRF – nepotpun evaluator', status: 'InReview', authorId: 'u-petar',
    requiresSeparateApprover: true, contentHash: h4, version: 1, revisionNumber: null, submittedAt: ago(5), updatedAt: ago(5),
    decisions: [decision('Submitted', 'u-petar', ago(5), h4)], invalid: true,
  },
]

const rowVersion = (r: MockRevision) => btoa(`v${r.version}`)
const isAuthor = (r: MockRevision, u: MockUser) => r.authorId === u.id

function allowedActions(r: MockRevision, u: MockUser): RevisionAction[] {
  if (!u.teacher) return []
  if (r.status === 'Draft' && isAuthor(r, u)) return ['submit']
  if (r.status === 'InReview' && !(r.requiresSeparateApprover && isAuthor(r, u))) return ['approve', 'return']
  return []
}

function summary(r: MockRevision): RevisionSummary {
  return {
    id: r.id, labId: r.labId, labTitle: r.labTitle, courseId: COURSE.id, courseName: COURSE.name,
    status: r.status, revisionNumber: r.revisionNumber, author: person(byId(r.authorId)),
    submittedAt: r.submittedAt, updatedAt: r.updatedAt,
  }
}

function detail(r: MockRevision, u: MockUser): RevisionDetail {
  return {
    ...summary(r), contentHash: r.contentHash, rowVersion: rowVersion(r),
    policy: { requiresSeparateApprover: r.requiresSeparateApprover },
    allowedActions: allowedActions(r, u), decisions: [...r.decisions],
  }
}

const idempotency = new Map<string, Response>()

function command(r: MockRevision, u: MockUser, action: RevisionAction, body: Record<string, unknown>, key: string | null): Response {
  if (key) {
    const prev = idempotency.get(`${u.id}:${key}`)
    if (prev) return prev.clone()
  }
  const respond = (res: Response) => { if (key) idempotency.set(`${u.id}:${key}`, res.clone()); return res }

  if (typeof body.rowVersion !== 'string' || typeof body.contentHash !== 'string')
    return json(400, { code: 'invalid_request', message: 'Nedostaju rowVersion ili contentHash.' })
  const comment = typeof body.comment === 'string' ? body.comment.trim() : ''
  if (comment.length > 1000) return json(400, { code: 'invalid_request', message: 'Komentar je predugačak.' })
  if (action === 'return' && !comment) return json(400, { code: 'invalid_request', message: 'Komentar je obavezan.' })

  if (body.rowVersion !== rowVersion(r) || body.contentHash !== r.contentHash)
    return json(409, { code: 'revision_changed', message: 'Revizija je izmenjena.' })

  if (!u.teacher) return json(403, { code: 'forbidden', message: 'Nemate dozvolu.' })
  if (action === 'approve' && r.status === 'InReview' && r.requiresSeparateApprover && isAuthor(r, u))
    return json(403, { code: 'self_approval_forbidden', message: 'Autor ne može da odobri sopstvenu reviziju.' })
  if (!allowedActions(r, u).includes(action)) return json(409, { code: 'invalid_state', message: 'Prelaz nije dozvoljen.' })

  const now = new Date().toISOString()
  if (action === 'approve' && r.invalid) {
    return respond(json(422, {
      code: 'validation_failed', message: 'Validacija nije prošla.',
      errors: [{ field: 'evaluator', message: 'Evaluator za tip „csrf-form“ nije registrovan.' }],
    }))
  }

  if (action === 'submit') {
    r.status = 'InReview'; r.submittedAt = now
    r.decisions.push(decision('Submitted', u.id, now, r.contentHash))
  } else if (action === 'return') {
    r.status = 'Draft'
    r.decisions.push(decision('Returned', u.id, now, r.contentHash, comment))
  } else {
    r.status = 'Published'
    r.revisionNumber = ++labCounters[r.labId]
    r.decisions.push(decision('Approved', u.id, now, r.contentHash, comment || null, r.revisionNumber))
  }
  r.version++; r.updatedAt = now
  return respond(json(200, detail(r, u)))
}

// ---------- HTTP ----------
function json(status: number, body: unknown, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })
}

function handleLogin(body: { email?: string; password?: string }): Response {
  const now = Date.now()
  if (now < lockedUntil) {
    const seconds = Math.ceil((lockedUntil - now) / 1000)
    return json(429, { code: 'too_many_attempts', message: 'Previše pokušaja. Pokušajte kasnije.' }, { 'Retry-After': String(seconds) })
  }
  const user = USERS.find(u => u.email === body.email?.trim().toLowerCase())
  if (user && body.password === MOCK_PASSWORD) {
    failures = 0
    return json(200, { tokenType: 'Bearer', accessToken: `mock-token:${user.id}` })
  }
  failures++
  if (failures >= MAX_FAILURES) {
    failures = 0
    lockedUntil = now + LOCK_SECONDS * 1000
    return json(429, { code: 'too_many_attempts', message: 'Previše pokušaja. Pokušajte kasnije.' }, { 'Retry-After': String(LOCK_SECONDS) })
  }
  return json(401, { code: 'invalid_credentials', message: 'Pogrešan imejl ili lozinka.' })
}

export const mockFetcher: Fetcher = async (input, init) => {
  await new Promise(r => setTimeout(r, 250)) // simulacija mreze
  const method = init?.method ?? 'GET'
  const body = init?.body ? JSON.parse(String(init.body)) : {}
  const headers = (init?.headers ?? {}) as Record<string, string>
  const url = new URL(input, 'http://mock')
  const path = url.pathname

  if (method === 'POST' && path === '/api/auth/login') return handleLogin(body)
  if (method === 'GET' && path === '/api/health') return json(200, { status: 'ok' })

  const token = headers.Authorization?.replace(/^Bearer /, '')
  const user = USERS.find(u => token === `mock-token:${u.id}`)
  if (!user) return json(401, { code: 'unauthenticated', message: 'Niste prijavljeni.' })

  if (method === 'GET' && path === '/api/revisions/pending-reviews') {
    const list = user.teacher
      ? revisions.filter(r => r.status === 'InReview' && !(r.requiresSeparateApprover && isAuthor(r, user)))
      : []
    return json(200, list.map(summary))
  }
  if (method === 'GET' && path === '/api/revisions/mine') {
    return json(200, revisions.filter(r => isAuthor(r, user) && (r.status === 'Draft' || r.status === 'InReview')).map(summary))
  }

  const m = path.match(/^\/api\/revisions\/([^/]+)(?:\/(submit|approve|return))?$/)
  if (m) {
    const r = revisions.find(x => x.id === decodeURIComponent(m[1]))
    if (!r || !user.teacher) return json(404, { code: 'not_found', message: 'Nije pronađeno.' })
    if (method === 'GET' && !m[2]) return json(200, detail(r, user))
    if (method === 'POST' && m[2]) return command(r, user, m[2] as RevisionAction, body, headers['Idempotency-Key'] ?? null)
  }

  return json(404, { code: 'not_found', message: 'Mock: ruta nije podrzana.' })
}

function editRevision(id: string) {
  const r = revisions.find(x => x.id === id)
  if (!r) return false
  r.contentHash = hash(); r.version++; r.updatedAt = new Date().toISOString()
  return true
}

export function installMock() {
  setFetcher(mockFetcher)
  ;(window as unknown as { __mock: unknown }).__mock = { editRevision }
  console.info(
    `[mock] API mock je ukljucen. Nalozi (lozinka "${MOCK_PASSWORD}"): ` +
    USERS.map(u => u.email).join(', ') +
    '. Za probu 409: __mock.editRevision("<id revizije>")',
  )
}
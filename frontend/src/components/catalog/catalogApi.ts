import { apiRequest } from '../../api/http'
import { FAKE_CATALOG } from './fakeCatalog'
import {
  DIFFICULTIES,
  LAB_TYPES,
  type CatalogLab,
  type LabAccess,
  type MissingPrerequisite,
} from './catalogTypes'

export const CATALOG_PATH = '/api/catalog'

export function accessPath(labId: string): string {
  return `/api/laboratories/${encodeURIComponent(labId)}/access`
}

export type CatalogEnv = { DEV: boolean; MODE: string }

export function shouldUseFakeCatalog(env: CatalogEnv): boolean {
  return env.DEV && env.MODE === 'mock'
}

export const UNKNOWN_PREREQUISITE_TITLE = 'Laboratorija koja vam trenutno nije dostupna'

export type CatalogOutcome =
  | { ok: true; labs: CatalogLab[] }
  | { ok: false; message: string }

function messageFor(status: number): string {
  if (status === 0) return 'Server nije dostupan. Proverite vezu.'
  if (status === 401) return 'Niste prijavljeni.'
  if (status === 403) return 'Nemate pristup katalogu.'
  return 'Katalog trenutno nije moguće učitati.'
}

function lockedAccess(): LabAccess {
  return { isUnlocked: false, missingPrerequisites: [] }
}

function parseEnum<T extends string>(value: unknown, allowed: readonly T[]): T | null {
  if (typeof value === 'number' && Number.isInteger(value)) return allowed[value] ?? null
  if (typeof value === 'string') return allowed.find(a => a.toLowerCase() === value.toLowerCase()) ?? null
  return null
}

function parseMissing(raw: unknown): MissingPrerequisite | null {
  if (typeof raw === 'string') return { id: raw, title: '' }
  if (!raw || typeof raw !== 'object') return null
  const m = raw as Record<string, unknown>
  if (typeof m.id !== 'string') return null
  return { id: m.id, title: typeof m.title === 'string' ? m.title : '' }
}

function parseAccess(raw: unknown): LabAccess {
  if (!raw || typeof raw !== 'object') return lockedAccess()
  const a = raw as Record<string, unknown>
  const missing = Array.isArray(a.missingPrerequisites)
    ? a.missingPrerequisites.map(parseMissing).filter((m): m is MissingPrerequisite => m !== null)
    : []
  return { isUnlocked: a.isUnlocked === true, missingPrerequisites: missing }
}

function parseLab(raw: unknown): CatalogLab | null {
  if (!raw || typeof raw !== 'object') return null
  const r = raw as Record<string, unknown>
  const type = parseEnum(r.type, LAB_TYPES)
  const difficulty = parseEnum(r.difficulty, DIFFICULTIES)
  if (typeof r.id !== 'string' || typeof r.title !== 'string' || typeof r.objective !== 'string') return null
  if (!type || !difficulty) return null
  const tags = Array.isArray(r.tags) ? r.tags.filter((t): t is string => typeof t === 'string') : []
  return { id: r.id, title: r.title, objective: r.objective, type, difficulty, tags, access: parseAccess(r.access) }
}

export function resolveTitles(labs: CatalogLab[]): CatalogLab[] {
  const titles = new Map(labs.map(lab => [lab.id, lab.title]))
  return labs.map(lab => ({
    ...lab,
    access: {
      ...lab.access,
      missingPrerequisites: lab.access.missingPrerequisites.map(m => ({
        id: m.id,
        title: m.title || titles.get(m.id) || UNKNOWN_PREREQUISITE_TITLE,
      })),
    },
  }))
}

export function parseCatalog(data: unknown): CatalogLab[] | null {
  if (!Array.isArray(data)) return null
  return resolveTitles(data.map(parseLab).filter((lab): lab is CatalogLab => lab !== null))
}

async function loadAccess(labId: string): Promise<LabAccess> {
  const result = await apiRequest<unknown>('GET', accessPath(labId))
  return result.ok ? parseAccess(result.data) : lockedAccess()
}

export async function fetchCatalogFromServer(): Promise<CatalogOutcome> {
  const result = await apiRequest<unknown>('GET', CATALOG_PATH)
  if (!result.ok) return { ok: false, message: messageFor(result.error.status) }

  const labs = parseCatalog(result.data)
  if (!labs) return { ok: false, message: messageFor(500) }

  const withAccess = await Promise.all(
    labs.map(async lab => ({ ...lab, access: await loadAccess(lab.id) })),
  )
  return { ok: true, labs: resolveTitles(withAccess) }
}

export async function getCatalog(env: CatalogEnv = import.meta.env): Promise<CatalogOutcome> {
  if (shouldUseFakeCatalog(env)) {
    const labs = parseCatalog(FAKE_CATALOG)
    return labs ? { ok: true, labs } : { ok: false, message: messageFor(500) }
  }
  return fetchCatalogFromServer()
}
// AUT-01: zajednički tipovi i konstante za nacrte zadataka.
// Forma, lista i (kasnije) ekran za uređivanje uvoze odavde,
// a ne jedna iz druge, pa novi tip laboratorije znači izmenu na jednom mestu.

export const MAX_TITLE_LENGTH = 120
export const MAX_GOAL_LENGTH = 2000

export const LAB_TYPES = [
  { value: 'AccessControl', label: 'Kontrola pristupa' },
  { value: 'LogAnalysis', label: 'Analiza logova' },
  { value: 'SecureConfiguration', label: 'Bezbedna konfiguracija' },
  { value: 'ThreatModel', label: 'Modelovanje pretnji' },
  { value: 'MfaAndSessions', label: 'MFA i sesije' },
  { value: 'ContextualAuthorization', label: 'Kontekstualna autorizacija' },
  { value: 'IncidentResponse', label: 'Odgovor na bezbednosne incidente' },
  { value: 'IsolatedEnvironment', label: 'Izolovano okruženje' },
] as const

export type LabType = (typeof LAB_TYPES)[number]['value']

export const DEFAULT_LAB_TYPE: LabType = LAB_TYPES[0].value

export function labTypeLabel(type: LabType): string {
  return LAB_TYPES.find(labType => labType.value === type)?.label ?? type
}

// LAB-04: nacrt, objavljena, arhivirana.
// Uskladiti nazive sa backendom (članovi 1 i 2) pre povezivanja.
export type DraftStatus = 'Draft' | 'Published' | 'Archived'

const DRAFT_STATUS_LABELS: Record<DraftStatus, string> = {
  Draft: 'Nacrt',
  Published: 'Objavljena',
  Archived: 'Arhivirana',
}

export function draftStatusLabel(status: DraftStatus): string {
  return DRAFT_STATUS_LABELS[status]
}

export type CreateDraftData = {
  title: string
  goal: string
  type: LabType
}

export type Draft = CreateDraftData & {
  id: string
  status: DraftStatus
}
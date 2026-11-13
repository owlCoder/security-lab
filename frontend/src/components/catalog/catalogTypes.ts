export const LAB_TYPES = [
  'AccessControl',
  'LogAnalysis',
  'SecurityConfiguration',
  'ThreatModeling',
  'MfaAndSessions',
  'ContextualAuthorization',
  'IncidentResponse',
  'IsolatedService',
] as const
export type LabType = typeof LAB_TYPES[number]

export const DIFFICULTIES = ['Easy', 'Medium', 'Hard'] as const
export type Difficulty = typeof DIFFICULTIES[number]

export const TYPE_LABELS: Record<LabType, string> = {
  AccessControl: 'Kontrola pristupa',
  LogAnalysis: 'Analiza dnevnika',
  SecurityConfiguration: 'Bezbedna konfiguracija',
  ThreatModeling: 'Modelovanje pretnji',
  MfaAndSessions: 'MFA i sesije',
  ContextualAuthorization: 'Kontekstualna autorizacija',
  IncidentResponse: 'Odgovor na incident',
  IsolatedService: 'Izolovano okruženje',
}

export const DIFFICULTY_LABELS: Record<Difficulty, string> = {
  Easy: 'Lako',
  Medium: 'Srednje',
  Hard: 'Teško',
}

export interface MissingPrerequisite {
  id: string
  title: string
}

export interface LabAccess {
  isUnlocked: boolean
  missingPrerequisites: MissingPrerequisite[]
}

export interface CatalogLab {
  id: string
  title: string
  objective: string
  difficulty: Difficulty
  type: LabType
  tags: string[]
  access: LabAccess
}
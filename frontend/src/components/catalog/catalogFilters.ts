import type { CatalogLab, Difficulty, LabType } from './catalogTypes'

export interface CatalogFilters {
  search: string
  type: LabType | ''
  difficulty: Difficulty | ''
  tag: string
}

export const EMPTY_FILTERS: CatalogFilters = { search: '', type: '', difficulty: '', tag: '' }

export function filterLabs(labs: CatalogLab[], f: CatalogFilters): CatalogLab[] {
  const q = f.search.trim().toLowerCase()
  return labs.filter(lab =>
    (!q || lab.title.toLowerCase().includes(q) || lab.objective.toLowerCase().includes(q)) &&
    (!f.type || lab.type === f.type) &&
    (!f.difficulty || lab.difficulty === f.difficulty) &&
    (!f.tag || lab.tags.includes(f.tag)))
}

export function allTags(labs: CatalogLab[]): string[] {
  return [...new Set(labs.flatMap(lab => lab.tags))].sort()
}
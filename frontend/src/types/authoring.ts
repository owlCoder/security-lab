export type LabTemplate = {
  id: string
  name: string
  title: string
  goal: string
  type: string
}

export type LabDraft = {
  id: string
  sourceTemplateId: string | null
  ownerId: string
  status: string
  version: number
  title: string
  goal: string
  type: string
}
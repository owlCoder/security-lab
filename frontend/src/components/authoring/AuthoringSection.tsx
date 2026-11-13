import { useState } from 'react'
import TemplateListPage from '../../pages/TemplateListPage'
import TemplateDetailPage from '../../pages/TemplateDetailPage'
import type { LabTemplate } from '../../types/authoring'

export default function AuthoringSection() {
  const [selected, setSelected] = useState<LabTemplate | null>(null)

  return selected
    ? <TemplateDetailPage key={selected.id} template={selected} onBack={() => setSelected(null)} />
    : <TemplateListPage onOpen={setSelected} />
}
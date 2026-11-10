import { getLabPreview } from './api'
import type { LabDevUser } from './api'
import LabView from './LabView'

type LabPreviewPageProps = {
  labId: string
  user: LabDevUser
}

export default function LabPreviewPage({
  labId,
  user,
}: LabPreviewPageProps) {
  return (
    <section className="card" aria-label="Pregled laboratorije pre objave">
      <div className="lab-preview-banner">
        <strong>Pregled kao student</strong>
      </div>

      <LabView
        key={`${labId}:${user}`}
        labId={labId}
        user={user}
        load={getLabPreview}
      />
    </section>
  )
}
import { getStudentLab } from './api'
import type { LabDevUser } from './api'
import LabView from './LabView'

type StudentLabPageProps = {
  labId: string
  user: LabDevUser
}

export default function StudentLabPage({
  labId,
  user,
}: StudentLabPageProps) {
  return (
    <section className="card" aria-label="Studentski prikaz laboratorije">
      <LabView
        key={`${labId}:${user}`}
        labId={labId}
        user={user}
        load={getStudentLab}
      />
    </section>
  )
}
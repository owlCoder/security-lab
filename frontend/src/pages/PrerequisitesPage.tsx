
import { useParams } from 'react-router-dom'
import PrerequisitesEditor from '../components/prerequisites/PrerequisitesEditor'
import PrerequisitesList from '../components/prerequisites/PrerequisitesList'

interface PrerequisitesPageProps {
  mode: 'teacher' | 'student'
  laboratoryNames?: Record<string, string>
}

export default function PrerequisitesPage({
  mode,
  laboratoryNames = {},
}: PrerequisitesPageProps) {
  const { laboratoryId } = useParams<{ laboratoryId: string }>()

  if (!laboratoryId) {
    return <p role="alert">Laboratorija nije izabrana.</p>
  }

  return (
    <main>
      <h1>Preduslovi laboratorije</h1>

      {mode === 'teacher' ? (
        <PrerequisitesEditor laboratoryId={laboratoryId} />
      ) : (
        <PrerequisitesList
          laboratoryId={laboratoryId}
          laboratoryNames={laboratoryNames}
        />
      )}
    </main>
  )
}

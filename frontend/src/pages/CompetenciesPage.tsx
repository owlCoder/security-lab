import { useParams } from 'react-router-dom'
import LaboratoryCompetencies from '../components/competencies/LaboratoryCompetencies'

export default function CompetenciesPage() {
  const { laboratoryId = '' } = useParams<{ laboratoryId: string }>()
  return <main className="competencies-page"><LaboratoryCompetencies laboratoryId={laboratoryId} /></main>
}

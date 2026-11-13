import type { TestUser } from '../api/labs'
import TeacherMetadataForm from '../components/labs/TeacherMetadataForm'
import { useParams } from 'react-router-dom'

const developmentTeacher: TestUser = import.meta.env.DEV
    ? { id: 'marko', role: 'Teacher' }
    : null

export default function TeacherMetadataPage() {
    const { labId } = useParams<{ labId: string }>()
    return (
        <main className="teacher-metadata-page">
            <h1>Unos metapodataka laboratorije</h1>

            {import.meta.env.DEV && (
                <p role="note">
                    Razvojni test korisnik: marko (nastavnik). Prava pristupa
                    proverava server.
                </p>
            )}

            <TeacherMetadataForm user={developmentTeacher} labId={labId} />
        </main>
    )
}
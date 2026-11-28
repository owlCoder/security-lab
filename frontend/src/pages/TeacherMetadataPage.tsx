import type { TestUser } from '../api/labs'
import TeacherMetadataForm from '../components/labs/TeacherMetadataForm'
import { useParams } from 'react-router-dom'
import type { Course } from '../types/course'

const developmentTeacher: TestUser = import.meta.env.DEV
    ? { id: '11111111-1111-1111-1111-111111111111', role: 'Teacher' }
    : null

const developmentCourse: Course | undefined = import.meta.env.DEV
    ? {
        id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
        code: 'OIB-DEMO',
        name: 'Lokalni demo kurs',
        description: 'Kurs sa unapred podešenim nastavničkim članstvom za lokalni razvoj.',
        status: 'Active',
        createdAt: '',
        archivedAt: null,
    }
    : undefined

export default function TeacherMetadataPage() {
    const { labId } = useParams<{ labId: string }>()
    return (
        <main className="teacher-metadata-page">
            <h1>Unos metapodataka laboratorije</h1>

            {import.meta.env.DEV && (
                <p role="note">
                    Razvojni test korisnik: marko (nastavnik) na kursu OIB-DEMO.
                    Ovo je lokalni demo identitet; prava pristupa i dalje proverava server.
                </p>
            )}

            <TeacherMetadataForm user={developmentTeacher} labId={labId}
                developmentCourses={developmentCourse ? [developmentCourse] : undefined} />
        </main>
    )
}
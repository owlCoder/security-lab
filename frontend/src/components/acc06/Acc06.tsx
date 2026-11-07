/// <reference types="vite/client" />

import { useState } from 'react'
import GroupManagement from './GroupManagement'
import StudentMaterials from './StudentMaterials'

const teacherId = '00000000-0000-0000-0000-000000000001'

const students = [
  {
    id: '00000000-0000-0000-0000-00000000000a',
    name: 'Student A',
  },
  {
    id: '00000000-0000-0000-0000-00000000000b',
    name: 'Student B',
  },
] as const

const activeCourseId = '21000000-0000-0000-0000-000000000001'
const archivedCourseId = '21000000-0000-0000-0000-000000000003'

export default function Acc06() {
  const [userId, setUserId] = useState<string>(students[0].id)
  const [courseId, setCourseId] = useState(activeCourseId)

  if (!import.meta.env.DEV) return null

  const contextKey = `${userId}:${courseId}`

  return (
    <section className="kt1" aria-labelledby="acc06-title">
      <div className="kt1-heading">
        <div>
          <p className="eyebrow">ACC-06</p>
          <h2 id="acc06-title">Grupe i vidljivost materijala</h2>
        </div>

        <span className="badge">Demo</span>
      </div>

      <p className="notice">
        Izaberi demo korisnika i kurs za pregled grupa ili vidljivih materijala.
      </p>

      <fieldset className="session-row">
        <legend>Demo kontekst</legend>

        <label>
          Korisnik
          <select
            value={userId}
            onChange={event => setUserId(event.target.value)}
          >
            <option value={teacherId}>Nastavnik</option>

            {students.map(student => (
              <option key={student.id} value={student.id}>
                {student.name}
              </option>
            ))}
          </select>
        </label>

        <label>
          Kurs
          <select
            value={courseId}
            onChange={event => setCourseId(event.target.value)}
          >
            <option value={activeCourseId}>OIB-2026</option>
            <option value={archivedCourseId}>OIB-2025 · arhiviran</option>
          </select>
        </label>
      </fieldset>

      {userId === teacherId ? (
        <GroupManagement
          key={contextKey}
          userId={userId}
          courseId={courseId}
          archived={courseId === archivedCourseId}
          students={students}
        />
      ) : (
        <StudentMaterials
          key={contextKey}
          userId={userId}
          courseId={courseId}
        />
      )}
    </section>
  )
}
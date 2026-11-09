
import type { Course } from '../../types/course'
import CourseCard from './CourseCard'

interface Props {
  courses: Course[]
  busy: boolean
  onArchive: (id: string) => void
}

export default function CourseList({
  courses,
  busy,
  onArchive,
}: Props) {
  if (courses.length === 0) {
    return (
      <div className="courses-empty">
        <h3>Nema kurseva za prikaz</h3>
        <p>
          Kreirajte novi kurs ili promenite kriterijume pretrage.
        </p>
      </div>
    )
  }

  return (
    <div className="course-list">
      {courses.map((course) => (
        <CourseCard
          key={course.id}
          course={course}
          busy={busy}
          onArchive={onArchive}
        />
      ))}
    </div>
  )
}

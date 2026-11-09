
import type { Course } from '../../types/course'

interface Props {
  course: Course
  busy: boolean
  onArchive: (id: string) => void
}

export default function CourseCard({
  course,
  busy,
  onArchive,
}: Props) {
  const isArchived = course.status === 'Archived'

  return (
    <article className="course-card">
      <div className="course-card-top">
        <div>
          <h3>{course.name}</h3>
          <span className="course-id">{course.code}</span>
        </div>

        <span
          className={`course-status ${
            isArchived ? 'status-archived' : 'status-active'
          }`}
        >
          {isArchived ? 'Arhiviran' : 'Aktivan'}
        </span>
      </div>

      <p className="course-card-description">
        {course.description || 'Opis nije unet.'}
      </p>

      <div className="course-card-footer">
        <span className="course-id">
          Kreiran:{' '}
          {new Date(course.createdAt).toLocaleDateString('sr-RS')}
        </span>

        {!isArchived && (
          <button
            type="button"
            className="archive-button"
            disabled={busy}
            onClick={() => onArchive(course.id)}
          >
            Arhiviraj
          </button>
        )}
      </div>
    </article>
  )
}

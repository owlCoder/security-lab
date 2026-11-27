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
      <div className="card-top">
        <div>
          <h3>{course.name}</h3>

          <p className="course-code">
            {course.code}
          </p>
        </div>

        <span
          className={`course-status ${
            isArchived
              ? 'status-archived'
              : 'status-active'
          }`}
        >
          {isArchived ? 'Arhiviran' : 'Aktivan'}
        </span>
      </div>

      <p className="course-description">
        {course.description || 'Opis nije unet.'}
      </p>

      {isArchived && (
        <p className="archived-note">
          Novi upisi nisu dozvoljeni.
          Postojeća istorija kursa je sačuvana.
        </p>
      )}

      <div className="card-footer">
        <div className="course-dates">
          <span className="course-id">
            Kreiran:{' '}
            {new Date(
              course.createdAt,
            ).toLocaleDateString('sr-RS')}
          </span>

          {isArchived && course.archivedAt && (
            <span className="course-id">
              Arhiviran:{' '}
              {new Date(
                course.archivedAt,
              ).toLocaleDateString('sr-RS')}
            </span>
          )}
        </div>

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
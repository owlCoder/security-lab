import './CoursesPage.css'
import { useMemo, useState } from 'react'
import CourseForm from '../components/courses/CourseForm'
import CourseList from '../components/courses/CourseList'
import { markArchived } from '../lib/courseRules'
import { courseService } from '../services/courseService'
import type {
  Course,
  CourseFilter,
  CreateCourseRequest,
} from '../types/course'

export default function CoursesPage() {
  // GET /api/courses jos nije implementiran.
  // Lista sadrzi kurseve kreirane tokom ove sesije.
  const [courses, setCourses] = useState<Course[]>([])
  const [busy, setBusy] = useState(false)
  const [formOpen, setFormOpen] = useState(false)
  const [archiveId, setArchiveId] = useState<string | null>(null)

  const [notice, setNotice] = useState<{
    text: string
    kind: 'success' | 'error'
  } | null>(null)

  const [filter, setFilter] = useState<CourseFilter>('all')
  const [search, setSearch] = useState('')

  const selected =
    courses.find(
      (course) =>
        course.id === archiveId &&
        course.status === 'Active'
    ) ?? null

  const visible = useMemo(() => {
    return courses.filter((course) => {
      const matchesStatus =
        filter === 'all' || course.status === filter

      const searchText =
        `${course.code} ${course.name} ${course.description ?? ''}`
          .toLocaleLowerCase()

      const matchesSearch = searchText.includes(
        search.trim().toLocaleLowerCase()
      )

      return matchesStatus && matchesSearch
    })
  }, [courses, filter, search])

  async function create(input: CreateCourseRequest) {
    if (busy) return

    setBusy(true)
    setNotice(null)

    try {
      const created = await courseService.createCourse(input)

      setCourses((previous) => [...previous, created])
      setFormOpen(false)

      setNotice({
        text: `Kurs "${created.name}" je uspešno kreiran.`,
        kind: 'success',
      })
    } catch (error) {
      setNotice({
        text:
          error instanceof Error
            ? error.message
            : 'Kreiranje kursa nije uspelo.',
        kind: 'error',
      })
    } finally {
      setBusy(false)
    }
  }

  function requestArchive(courseId: string) {
    setArchiveId(courseId)
    setNotice(null)
  }

  async function confirmArchive() {
    if (!selected || busy) return

    setBusy(true)
    setNotice(null)

    try {
      await courseService.archiveCourse(selected.id)

      setCourses((previous) =>
        markArchived(previous, selected.id)
      )

      setNotice({
        text: `Kurs "${selected.name}" je uspešno arhiviran.`,
        kind: 'success',
      })

      setArchiveId(null)
    } catch (error) {
      setNotice({
        text:
          error instanceof Error
            ? error.message
            : 'Arhiviranje kursa nije uspelo.',
        kind: 'error',
      })
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="courses-page">
      <header className="courses-header">
        <p className="eyebrow">
          SCUTUM / SECURITY LAB / ACC-01
        </p>

        <h1>Upravljanje kursevima</h1>

        <p className="courses-description">
          Kreiranje, pregled i arhiviranje kurseva.
        </p>
      </header>

      <section className="courses-section">
        <div className="courses-section-header">
          <div>
            <h2>Kursevi kreirani tokom ove sesije</h2>
            <p>
              Kursevi uspešno kreirani preko ACC-01 API-ja.
            </p>
          </div>

          <button
            type="button"
            className="primary-button"
            disabled={formOpen || busy}
            onClick={() => {
              setFormOpen(true)
              setNotice(null)
            }}
          >
            + Novi kurs
          </button>
        </div>

        <p className="demo-note">
          Backend trenutno nema GET endpoint za listu kurseva.
          Kursevi su trajno sačuvani na serveru, ali se ovde
          prikazuju samo tokom trenutne sesije.
        </p>

        {notice && (
          <p
            role={notice.kind === 'error' ? 'alert' : 'status'}
            className={
              notice.kind === 'error'
                ? 'form-error'
                : 'success-message'
            }
          >
            {notice.text}
          </p>
        )}

        {formOpen && (
          <CourseForm
            busy={busy}
            onCancel={() => setFormOpen(false)}
            onSave={create}
          />
        )}

        <div className="toolbar">
          <label htmlFor="course-search">
            Pretraga
            <input
              id="course-search"
              type="search"
              value={search}
              onChange={(event) =>
                setSearch(event.target.value)
              }
              placeholder="Šifra, naziv ili opis"
            />
          </label>

          <label htmlFor="course-filter">
            Status
            <select
              id="course-filter"
              value={filter}
              onChange={(event) =>
                setFilter(
                  event.target.value as CourseFilter
                )
              }
            >
              <option value="all">Svi</option>
              <option value="Active">Aktivni</option>
              <option value="Archived">Arhivirani</option>
            </select>
          </label>
        </div>

        <p className="count">
          Prikazano: {visible.length} / {courses.length}
        </p>

        <CourseList
          courses={visible}
          busy={busy}
          onArchive={requestArchive}
        />
      </section>

      {selected && (
        <div className="modal-overlay">
          <div
            className="archive-modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="archive-title"
          >
            <h2 id="archive-title">
              Arhiviranje kursa
            </h2>

            <p>
              Da li želite da arhivirate kurs{' '}
              <strong>{selected.name}</strong>?
            </p>

            <p className="archive-warning">
              Kurs ostaje sačuvan u bazi.
              Novi upisi neće biti dozvoljeni.
            </p>

            <div className="form-actions">
              <button
                type="button"
                className="secondary-button"
                disabled={busy}
                onClick={() => setArchiveId(null)}
              >
                Otkaži
              </button>

              <button
                type="button"
                className="confirm-archive-button"
                disabled={busy}
                onClick={confirmArchive}
              >
                {busy
                  ? 'Arhiviranje…'
                  : 'Potvrdi arhiviranje'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

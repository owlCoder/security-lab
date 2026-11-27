import './CoursesPage.css'
import { useEffect, useMemo, useState } from 'react'
import CourseForm from '../components/courses/CourseForm'
import CourseList from '../components/courses/CourseList'
import { courseService } from '../services/courseService'

import type {
  Course,
  CourseFilter,
  CreateCourseRequest,
} from '../types/course'

export default function CoursesPage() {
  const [courses, setCourses] = useState<Course[]>([])

  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [formOpen, setFormOpen] = useState(false)
  const [archiveId, setArchiveId] = useState<string | null>(null)

  const [notice, setNotice] = useState<{
    text: string
    kind: 'success' | 'error'
  } | null>(null)

  const [filter, setFilter] = useState<CourseFilter>('all')
  const [search, setSearch] = useState('')

  const [reloadVersion, setReloadVersion] = useState(0)

  // Ucitavanje kurseva sa servera.
  useEffect(() => {
    const controller = new AbortController()

    async function loadCourses() {
      setLoading(true)
      setLoadError(null)

      try {
        const result = await courseService.listCourses(
          controller.signal,
        )

        if (!controller.signal.aborted) {
          setCourses(result)
        }
      } catch (error) {
        if (controller.signal.aborted) {
          return
        }

        setCourses([])

        setLoadError(
          error instanceof Error
            ? error.message
            : 'Učitavanje kurseva nije uspelo.',
        )
      } finally {
        if (!controller.signal.aborted) {
          setLoading(false)
        }
      }
    }

    void loadCourses()

    return () => controller.abort()
  }, [reloadVersion])

  const selected =
    courses.find(
      (course) =>
        course.id === archiveId &&
        course.status === 'Active',
    ) ?? null

  // Pretraga i filtriranje kurseva.
  const visible = useMemo(() => {
    return courses.filter((course) => {
      const matchesStatus =
        filter === 'all' || course.status === filter

      const searchText =
        `${course.code} ${course.name} ${course.description}`
          .toLocaleLowerCase()

      const matchesSearch = searchText.includes(
        search.trim().toLocaleLowerCase(),
      )

      return matchesStatus && matchesSearch
    })
  }, [courses, filter, search])

  // Osvezavanje liste sa servera.
  function refreshCourses() {
    setLoading(true)
    setReloadVersion((previous) => previous + 1)
  }

  // Kreiranje kursa.
  async function create(input: CreateCourseRequest) {
    if (busy || loading || loadError) {
      return
    }

    setBusy(true)
    setNotice(null)

    try {
      const created = await courseService.createCourse(input)

      setFormOpen(false)

      setNotice({
        text: `Kurs "${created.name}" je uspešno kreiran.`,
        kind: 'success',
      })

      refreshCourses()
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

  // Otvaranje potvrde arhiviranja.
  function requestArchive(courseId: string) {
    if (busy || loading || loadError) {
      return
    }

    setArchiveId(courseId)
    setNotice(null)
  }

  // Arhiviranje kursa.
  async function confirmArchive() {
    if (!selected || busy || loading) {
      return
    }

    setBusy(true)
    setNotice(null)

    try {
      await courseService.archiveCourse(selected.id)

      setArchiveId(null)

      setNotice({
        text: `Kurs "${selected.name}" je uspešno arhiviran.`,
        kind: 'success',
      })

      refreshCourses()
    } catch (error) {
      setArchiveId(null)

      setNotice({
        text:
          error instanceof Error
            ? error.message
            : 'Arhiviranje kursa nije uspelo.',
        kind: 'error',
      })

      refreshCourses()
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

      <section
        className="courses-section"
        aria-busy={loading || busy}
      >
        <div className="courses-section-header">
          <div>
            <h2>Pregled kurseva</h2>

            <p>
              Aktivni i arhivirani kursevi dostupni
              trenutnom korisniku.
            </p>
          </div>

          <div className="form-actions">
            <button
              type="button"
              className="secondary-button"
              disabled={loading || busy || archiveId !== null}
              onClick={refreshCourses}
            >
              Osveži
            </button>

            <button
              type="button"
              className="primary-button"
              disabled={
                formOpen ||
                busy ||
                loading ||
                loadError !== null ||
                archiveId !== null
              }
              onClick={() => {
                setFormOpen(true)
                setNotice(null)
              }}
            >
              + Novi kurs
            </button>
          </div>
        </div>

        <p className="demo-note">
          Arhivirani kursevi ostaju dostupni za pregled.
          Arhiviranje sprečava nove upise, ali ne briše
          postojeće podatke i istoriju kursa.
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

        {loading && (
          <div className="courses-empty" role="status">
            <h3>Učitavanje kurseva...</h3>
            <p>Preuzimanje podataka sa servera.</p>
          </div>
        )}

        {!loading && loadError && (
          <div className="courses-empty">
            <h3>Kursevi nisu učitani</h3>

            <p className="form-error" role="alert">
              {loadError}
            </p>

            <button
              type="button"
              className="secondary-button"
              onClick={refreshCourses}
            >
              Pokušaj ponovo
            </button>
          </div>
        )}

        {!loading && !loadError && (
          <>
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
                      event.target.value as CourseFilter,
                    )
                  }
                >
                  <option value="all">Svi</option>
                  <option value="Active">Aktivni</option>
                  <option value="Archived">
                    Arhivirani
                  </option>
                </select>
              </label>
            </div>

            <p className="count">
              Prikazano: {visible.length} / {courses.length}
            </p>

            <CourseList
              courses={visible}
              busy={busy || formOpen || archiveId !== null}
              onArchive={requestArchive}
            />
          </>
        )}
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
              Postojeća istorija neće biti obrisana.
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
                disabled={busy || loading}
                onClick={confirmArchive}
              >
                {busy
                  ? 'Arhiviranje...'
                  : 'Potvrdi arhiviranje'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
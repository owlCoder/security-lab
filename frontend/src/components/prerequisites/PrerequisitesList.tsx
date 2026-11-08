
import { useEffect, useState } from 'react'
import { getPrerequisites } from '../../api/prerequisites'

interface PrerequisitesListProps {
  laboratoryId: string
  laboratoryNames?: Record<string, string>
}

export default function PrerequisitesList({
  laboratoryId,
  laboratoryNames = {},
}: PrerequisitesListProps) {
  const [prerequisites, setPrerequisites] = useState<string[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    let active = true

    async function loadPrerequisites() {
      const result = await getPrerequisites(laboratoryId)

      if (!active) return

      if (result.ok) {
        setPrerequisites(result.data)
        setError('')
      } else {
        setPrerequisites([])
        setError(result.error.message)
      }

      setLoading(false)
    }

    setLoading(true)
    setError('')
    setPrerequisites([])
    void loadPrerequisites()

    return () => {
      active = false
    }
  }, [laboratoryId])

  return (
    <section aria-label="Preduslovi laboratorije">
      <h2>Preduslovi laboratorije</h2>

      {loading ? (
        <p>Učitavanje preduslova...</p>
      ) : error ? (
        <p role="alert">{error}</p>
      ) : prerequisites.length === 0 ? (
        <p>Za ovu laboratoriju nisu definisani preduslovi.</p>
      ) : (
        <>
          <p>
            Pre početka ove laboratorije potrebno je
            završiti sledeće laboratorije:
          </p>

          <ul>
            {prerequisites.map(id => (
              <li key={id}>
                {laboratoryNames[id] ?? `Laboratorija: ${id}`}
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  )
}

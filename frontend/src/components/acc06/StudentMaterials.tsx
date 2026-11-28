import { useState } from 'react'
import {
  Acc06ApiError,
  listVisibleMaterials,
  readMaterial,
} from '../../api/acc06'
import type { Material } from '../../api/acc06'

type Props = {
  userId: string
  courseId: string
}

export default function StudentMaterials({ userId, courseId }: Props) {
  const [materials, setMaterials] = useState<Material[] | null>(null)
  const [opened, setOpened] = useState<Material | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  async function run(action: () => Promise<void>) {
    setBusy(true)
    setOpened(null)
    setError('')

    try {
      await action()
    } catch (error) {
      setError(error instanceof Error ? error.message : 'Zahtev nije uspeo.')
    } finally {
      setBusy(false)
    }
  }

  async function loadMaterials() {
    setMaterials(null)

    await run(async () => {
      setMaterials(await listVisibleMaterials(userId, courseId))
    })
  }

  async function openMaterial(material: Material) {
    await run(async () => {
      try {
        setOpened(await readMaterial(userId, material))
      } catch (error) {
        if (error instanceof Acc06ApiError && error.status === 404) {
          setMaterials(current =>
            current?.filter(item =>
              item.type !== material.type ||
              item.materialId !== material.materialId,
            ) ?? null,
          )

          throw new Error('Materijal nije dostupan.')
        }

        throw error
      }
    })
  }

  return (
    <>
      <fieldset className="work-panel" disabled={busy}>
        <legend>Vidljivi materijali</legend>

        <button type="button" onClick={() => void loadMaterials()}>
          Učitaj vidljive materijale
        </button>

        {materials?.length === 0 && (
          <p className="hint">Nema vidljivih materijala.</p>
        )}

        {materials && materials.length > 0 && (
          <ul>
            {materials.map(material => (
              <li key={`${material.type}:${material.materialId}`}>
                <div className="action-row">
                  <span className="hint">
                    {material.type} · <code>{material.materialId}</code>
                  </span>

                  <button
                    type="button"
                    className="secondary"
                    aria-label={`Otvori materijal ${material.materialId}`}
                    onClick={() => void openMaterial(material)}
                  >
                    Otvori
                  </button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </fieldset>

      <div
        className={`result${error ? ' error' : ''}`}
        role="status"
        aria-live="polite"
      >
        {busy && 'Zahtev je u toku…'}
        {error && <p>{error}</p>}

        {opened && (
          <p>
            Pristup je dozvoljen. {opened.type} · <code>{opened.materialId}</code>
          </p>
        )}
      </div>
    </>
  )
}
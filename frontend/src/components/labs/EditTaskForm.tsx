import { useEffect, useState, type FormEvent } from 'react'

interface TaskData {
  id: string
  title: string
  description: string
}

export interface EditTaskFormProps {
  /** Identifier selected by the calling task list; never editable by the user. */
  taskId: string
}

export function EditTaskForm({ taskId }: EditTaskFormProps) {
  const [task, setTask] = useState<TaskData | null>(null)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [success, setSuccess] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    setTask(null)
    setLoading(true)
    setError('')
    setSuccess('')

    if (!taskId.trim()) {
      setError('Nije odabran zadatak.')
      setLoading(false)
      return () => controller.abort()
    }

    void fetch(`/api/authoring/tasks/${encodeURIComponent(taskId)}`, {
      signal: controller.signal,
      credentials: 'same-origin',
    }).then(async response => {
      if (!response.ok) throw new Error(`Učitavanje zadatka nije uspelo (HTTP ${response.status}).`)
      const data: TaskData = await response.json()
      if (data.id !== taskId || typeof data.title !== 'string' || typeof data.description !== 'string')
        throw new Error('Server je vratio neispravne podatke o zadatku.')
      if (!controller.signal.aborted) setTask(data)
    }).catch((cause: unknown) => {
      if (!controller.signal.aborted)
        setError(cause instanceof Error ? cause.message : 'Učitavanje zadatka nije uspelo.')
    }).finally(() => {
      if (!controller.signal.aborted) setLoading(false)
    })

    return () => controller.abort()
  }, [taskId])

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!task || saving) return

    setError('')
    setSuccess('')
    if (!task.title.trim() || !task.description.trim()) {
      setError('Naslov i opis zadatka su obavezni.')
      return
    }

    setSaving(true)
    try {
      const response = await fetch(`/api/authoring/tasks/${encodeURIComponent(taskId)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify({ title: task.title, description: task.description }),
      })
      if (!response.ok)
        throw new Error(`Čuvanje nije uspelo (HTTP ${response.status}). Izmene nisu potvrđene.`)
      setSuccess('Izmene su uspešno sačuvane.')
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Čuvanje nije uspelo. Izmene nisu potvrđene.')
    } finally {
      setSaving(false)
    }
  }

  if (loading) return <p role="status">Učitavanje zadatka...</p>
  if (!task) return <p role="alert">{error || 'Zadatak nije dostupan.'}</p>

  return (
    <section aria-labelledby="edit-task-heading">
      <h3 id="edit-task-heading">Uređivanje zadatka (AUT-01)</h3>
      <p>ID zadatka: <output>{task.id}</output></p>
      {error && <p role="alert">{error}</p>}
      {success && <p role="status">{success}</p>}
      <form onSubmit={handleSubmit}>
        <label htmlFor="edit-task-title">Naslov zadatka</label>
        <input
          id="edit-task-title"
          value={task.title}
          onChange={event => setTask({ ...task, title: event.target.value })}
          required
          maxLength={200}
        />
        <label htmlFor="edit-task-description">Opis zadatka</label>
        <textarea
          id="edit-task-description"
          value={task.description}
          onChange={event => setTask({ ...task, description: event.target.value })}
          required
        />
        <button type="submit" disabled={saving}>{saving ? 'Čuvanje...' : 'Sačuvaj izmene'}</button>
      </form>
    </section>
  )
}

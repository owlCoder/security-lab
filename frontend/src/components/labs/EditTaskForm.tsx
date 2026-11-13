import { useEffect, useState, type FormEvent } from 'react'

interface TaskData {
  id: string
  title: string
  description: string
  goal?: string
  type?: string
}

const LAB_TYPES = [
  'AccessControl',
  'LogAnalysis',
  'SecureConfiguration',
  'ThreatModel',
  'MfaAndSessions',
  'ContextualAuthorization',
  'IncidentResponse',
  'IsolatedEnvironment',
] as const

const DEFAULT_TYPE = 'AccessControl'

function emptyTask(taskId: string): TaskData {
  return {
    id: taskId,
    title: '',
    description: '',
    goal: '',
    type: DEFAULT_TYPE,
  }
}

export interface EditTaskFormProps {
  /** Identifier selected by the calling task list; never editable by the user. */
  taskId: string
}

function normalizeTask(taskId: string, data: Partial<TaskData>): TaskData {
  const description = typeof data.description === 'string' ? data.description : typeof data.goal === 'string' ? data.goal : ''
  const type = typeof data.type === 'string' ? data.type : DEFAULT_TYPE

  return {
    id: typeof data.id === 'string' ? data.id : taskId,
    title: typeof data.title === 'string' ? data.title : '',
    description,
    goal: typeof data.goal === 'string' ? data.goal : description,
    type,
  }
}

export function EditTaskForm({ taskId }: EditTaskFormProps) {
  const [task, setTask] = useState<TaskData>(() => emptyTask(taskId))
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const [success, setSuccess] = useState('')
  const [loaded, setLoaded] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    setTask(emptyTask(taskId))
    setLoading(true)
    setError('')
    setSuccess('')
    setLoaded(false)

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
      const data: Partial<TaskData> = await response.json()
      const normalized = normalizeTask(taskId, data)
      if (normalized.id !== taskId || typeof normalized.title !== 'string')
        throw new Error('Server je vratio neispravne podatke o zadatku.')
      if (!controller.signal.aborted) {
        setTask(normalized)
        setLoaded(true)
      }
    }).catch((cause: unknown) => {
      if (!controller.signal.aborted) {
        setError(cause instanceof Error ? cause.message : 'Učitavanje zadatka nije uspelo.')
        setTask(emptyTask(taskId))
        setLoaded(false)
      }
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

    if (!loaded) {
      setError('Zadatak nije uspešno učitan. Sačuvanje je blokirano dok se validan odgovor ne vrati.')
      return
    }

    const title = task.title.trim()
    const goal = (task.goal ?? task.description ?? '').trim()
    if (!title || !goal) {
      setError('Naslov i opis zadatka su obavezni.')
      return
    }

    if (task.type && !LAB_TYPES.includes(task.type as typeof LAB_TYPES[number])) {
      setError('Nepoznat tip zadatka. Odaberite jedan od ponuđenih tipova.')
      return
    }

    setSaving(true)
    try {
      const body = {
        title,
        description: goal,
        goal,
        type: task.type ?? DEFAULT_TYPE,
      }

      const response = await fetch(`/api/authoring/tasks/${encodeURIComponent(taskId)}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'same-origin',
        body: JSON.stringify(body),
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

  return (
    <section className="labs-form" aria-labelledby="edit-task-heading">
      <h3 id="edit-task-heading">Uređivanje zadatka (AUT-01)</h3>
      <p>ID zadatka: <output>{task.id}</output></p>
      {error && <p role="alert">{error}</p>}
      {success && <p role="status">{success}</p>}

      <form onSubmit={handleSubmit} noValidate>
        <div className="labs-field">
          <label htmlFor="edit-task-title">Naslov zadatka</label>
          <input
            id="edit-task-title"
            value={task.title}
            onChange={event => setTask({ ...task, title: event.target.value })}
            required
            maxLength={200}
          />
        </div>

        <div className="labs-field">
          <label htmlFor="edit-task-goal">Opis zadatka</label>
          <textarea
            id="edit-task-goal"
            value={task.goal ?? task.description}
            onChange={event => {
              const nextValue = event.target.value
              setTask(current => current ? { ...current, goal: nextValue, description: nextValue } : current)
            }}
            required
            rows={5}
          />
        </div>

        <div className="labs-field">
          <label htmlFor="edit-task-type">Tip zadatka</label>
          <select
            id="edit-task-type"
            value={task.type ?? DEFAULT_TYPE}
            onChange={event => setTask({ ...task, type: event.target.value })}
          >
            {LAB_TYPES.map(type => (
              <option key={type} value={type}>{type}</option>
            ))}
          </select>
        </div>

        <button type="submit" disabled={saving}>{saving ? 'Čuvanje...' : 'Sačuvaj izmene'}</button>
      </form>
    </section>
  )
}

import { useEffect, useState } from 'react'
import { useAuth } from '../auth/AuthContext'

export default function SessionNotificationSettings() {
  const { request } = useAuth()
  const [enabled, setEnabled] = useState(false)
  const [busy, setBusy] = useState(true)
  const [message, setMessage] = useState('')

  useEffect(() => {
    let active = true
    void request('/api/auth/session-notifications')
      .then(async response => {
        if (!response.ok) throw new Error('Podešavanje obaveštenja nije dostupno.')
        const preference = await response.json() as { enabled: boolean }
        if (active) setEnabled(preference.enabled)
      })
      .catch(() => {
        if (active) setMessage('Podešavanje obaveštenja nije dostupno.')
      })
      .finally(() => {
        if (active) setBusy(false)
      })

    return () => { active = false }
  }, [request])

  async function update(enabledValue: boolean) {
    setBusy(true)
    setMessage('')
    try {
      const response = await request('/api/auth/session-notifications', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ enabled: enabledValue }),
      })
      if (!response.ok) throw new Error('Podešavanje nije sačuvano.')
      setEnabled(enabledValue)
      setMessage(enabledValue ? 'Obaveštenja su uključena.' : 'Obaveštenja su isključena.')
    } catch {
      setMessage('Podešavanje nije sačuvano. Pokušajte ponovo.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="card">
      <h2>Obaveštenja o prijavi</h2>
      <label>
        <input
          type="checkbox"
          checked={enabled}
          disabled={busy}
          onChange={event => void update(event.target.checked)}
        />
        Pošalji mi email kada se neko prijavi na moj nalog
      </label>
      {message && <p role="status">{message}</p>}
    </section>
  )
}

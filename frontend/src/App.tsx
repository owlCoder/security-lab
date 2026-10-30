import { useEffect, useState } from 'react'
import AccessDemo from './components/AccessDemo'

export default function App() {
  const [health, setHealth] = useState('Checking API…')
  useEffect(() => {
    fetch('/api/health').then(r => r.ok ? r.json() : Promise.reject(r.status))
      .then(data => setHealth(data.status === 'ok' ? 'API available' : 'API is not ready'))
      .catch(() => setHealth('API unavailable (start the backend).'))
  }, [])
  return <main>
    <p className="eyebrow">Security Lab / OIB</p>
    <h1>Security Lab</h1>
    <p>An interactive laboratory platform for the Fundamentals of Information Security course. Develop features through the assigned project components.</p>
    <section><h2>Development status</h2><p>{health}</p><p>KT1: initial implementation, component architecture, and data flow diagram.</p></section>
    <AccessDemo />
  </main>
}

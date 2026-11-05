import { useState } from 'react'
import TestUserPicker, { TEST_USERS } from './TestUserPicker'
import CreateLabForm from './CreateLabForm'
import LabView from './LabView'
import './labs.css'

// LAB-07 frontend (tim 14): kreiranje laboratorije, prikaz po jeziku i prikaz grešaka.
// Frontend samo pomaže korisniku; sva prava proverava server.
export default function LabsSection() {
  const [userKey, setUserKey] = useState('none')
  const user = TEST_USERS.find(u => u.value === userKey)?.user ?? null

  return (
    <section className="labs" aria-labelledby="labs-heading">
      <div className="labs-heading">
        <div>
          <p className="eyebrow">LAB-01 + LAB-07 · tim 14</p>
          <h2 id="labs-heading">Laboratorije i jezičke verzije</h2>
        </div>
        <span className="labs-badge">KT1</span>
      </div>
      <p className="labs-notice">
        Frontend samo pomaže korisniku: sva prava i validaciju proverava server. Delovi označeni
        oznakom <span className="badge-fake">FAKE</span> su privremeni i zameniće se pravim modulima.
      </p>
      {import.meta.env.DEV && <TestUserPicker value={userKey} onChange={setUserKey} />}
      <div className="labs-grid">
        <div className="labs-panel"><CreateLabForm user={user} /></div>
        <div className="labs-panel"><LabView user={user} /></div>
      </div>
    </section>
  )
}

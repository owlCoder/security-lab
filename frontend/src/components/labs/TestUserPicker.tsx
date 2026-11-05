import type { TestUser } from '../../api/labs'

// FAKE – zameniće se pravom prijavom (IDN modul). Bira identitet koji se šalje
// u zaglavlju X-Test-User; server ga prihvata samo u Development režimu.
export const TEST_USERS: { value: string; label: string; user: TestUser }[] = [
  { value: 'none', label: 'Bez korisnika (anonimno)', user: null },
  { value: 'student', label: 'ana – Student', user: { id: 'ana', role: 'Student' } },
  { value: 'teacher', label: 'marko – Nastavnik', user: { id: 'marko', role: 'Teacher' } },
]

const ROLE_NAMES = { Student: 'student', Teacher: 'nastavnik' } as const

interface Props {
  value: string
  onChange: (value: string) => void
}

export default function TestUserPicker({ value, onChange }: Props) {
  const current = TEST_USERS.find(u => u.value === value)?.user ?? null
  return (
    <fieldset className="labs-session">
      <legend>Razvojna sesija <span className="badge-fake">FAKE</span></legend>
      <div className="labs-session-row">
        <div className="labs-field">
          <label htmlFor="labs-test-user">Test korisnik</label>
          <select
            id="labs-test-user"
            value={value}
            onChange={e => onChange(e.target.value)}
            aria-describedby="labs-test-user-hint"
          >
            {TEST_USERS.map(u => <option key={u.value} value={u.value}>{u.label}</option>)}
          </select>
        </div>
        <p className="labs-session-current">
          Trenutno: <strong>{current ? `${current.id} (${ROLE_NAMES[current.role]})` : 'anonimno'}</strong>
        </p>
      </div>
      <p id="labs-test-user-hint" className="hint">
        Šalje zaglavlje X-Test-User, koje server prihvata samo u razvojnom režimu. Zameniće se pravom prijavom (IDN modul).
      </p>
    </fieldset>
  )
}

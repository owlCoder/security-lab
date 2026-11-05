import CourseworkKt1 from './components/CourseworkKt1'
import { useEffect, useState } from 'react'
import { logout } from './api/auth'
import { apiRequest, session } from './api/http'
import { getMyRevisions, getPendingReviews } from './api/revisions'
import CreateAccountForm from './components/CreateAccountForm'
import LabsSection from './components/labs/LabsSection'
import LoginPage from './pages/LoginPage'
import RevisionDetailPage from './pages/RevisionDetailPage'
import RevisionListPage from './pages/RevisionListPage'

type View = { tab: 'pending' | 'mine'; revisionId?: string }

export default function App() {
  const [health, setHealth] = useState('Provera API-ja…')
  const [loggedIn, setLoggedIn] = useState(session.token != null)
  const [view, setView] = useState<View>({ tab: 'pending' })

  useEffect(() => {
    apiRequest<{ status: string }>('GET', '/api/health').then(r =>
      setHealth(!r.ok ? 'API nije dostupan (pokrenite backend).' : r.data.status === 'ok' ? 'API dostupan' : 'API nije spreman'))
  }, [])

  function handleLogout() {
    logout()
    setLoggedIn(false)
    setView({ tab: 'pending' })
  }

  return <main>
    <header className="topbar">
      <div>
        <p className="eyebrow">Security Lab / OIB</p>
        <h1>Security Lab</h1>
      </div>
      {loggedIn && <button className="secondary" onClick={handleLogout}>Odjava</button>}
    </header>

    {!loggedIn && <LoginPage onLoggedIn={() => setLoggedIn(true)} />}

    {loggedIn && <>
      <nav className="tabs" aria-label="Revizije">
        <button className={view.tab === 'pending' ? 'tab active' : 'tab'} onClick={() => setView({ tab: 'pending' })}>Na čekanju</button>
        <button className={view.tab === 'mine' ? 'tab active' : 'tab'} onClick={() => setView({ tab: 'mine' })}>Moje revizije</button>
      </nav>

      {view.revisionId
        ? <RevisionDetailPage
            key={view.revisionId}
            revisionId={view.revisionId}
            onBack={() => setView({ tab: view.tab })}
          />
        : view.tab === 'pending'
          ? <RevisionListPage
              key="pending"
              title="Revizije na čekanju"
              description="Revizije koje čekaju vaše odobrenje. Sopstvene revizije se ovde ne prikazuju kada politika zahteva drugog odobravaoca."
              emptyText="Nema revizija koje čekaju vaše odobrenje."
              load={getPendingReviews}
              onOpen={id => setView({ tab: 'pending', revisionId: id })}
            />
          : <RevisionListPage
              key="mine"
              title="Moje revizije"
              description="Nacrti i revizije koje ste predali na pregled."
              emptyText="Nemate nacrta ni revizija na pregledu."
              load={getMyRevisions}
              onOpen={id => setView({ tab: 'mine', revisionId: id })}
            />}
    </>}

    <section className="card"><h2>Status razvoja</h2><p>{health}</p></section>
    <section className="card"><h2>Kreiranje naloga (administrator)</h2><CreateAccountForm /></section>
    {/* Komponente drugih timova sa main grane (ACC-04/ADM-08 i LAB-07) */}
    <CourseworkKt1 />
    <LabsSection />


  </main>
}

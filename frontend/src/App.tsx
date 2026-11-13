import { useNavigate, Routes, Route } from 'react-router-dom'
import { useEffect, useState } from 'react'
import { AuthProvider, useAuth } from './auth/AuthContext'
import { apiRequest } from './api/http'
import { getMyRevisions, getPendingReviews } from './api/revisions'
import AppShell from './components/AppShell'
import LandingPage from './components/LandingPage'
import CourseworkKt1 from './components/CourseworkKt1'
import LabsSection from './components/labs/LabsSection'
import ProtectedRoute from './components/ProtectedRoute'
import ProtectedApp from './components/ProtectedApp'
import LoginPage from './login/page'
import CoursesPage from './pages/CoursesPage'
import RevisionDetailPage from './pages/RevisionDetailPage'
import RevisionListPage from './pages/RevisionListPage'
import TeacherMetadataPage from './pages/TeacherMetadataPage'
import LabVisibilitySection from './labs/LabVisibilitySection'

function LoginRoute() {
  const navigate = useNavigate()
  const { refreshUser } = useAuth()
  const [error, setError] = useState('')

  return <>
    <LoginPage onSuccess={() => {
      setError('')
      void refreshUser()
        .then(() => navigate('/access-demo'))
        .catch(() => setError('Login succeeded, but the session could not be verified. Please try again.'))
      return true
    }} />
    {error && <p className="login-error" role="alert">{error}</p>}
  </>
}

function HealthStatus() {
  const [health, setHealth] = useState('Provera API-ja…')

  useEffect(() => {
    apiRequest<{ status: string }>('GET', '/api/health').then(r =>
      setHealth(!r.ok ? 'API nije dostupan (pokrenite backend).' : r.data.status === 'ok' ? 'API dostupan' : 'API nije spreman'))
  }, [])

  return <section className="card"><h2>Status razvoja</h2><p>{health}</p></section>
}

type RevisionView = { tab: 'pending' | 'mine'; revisionId?: string }

// REV-05 revision review screens; the session cookie authenticates their API calls.
function RevisionsSection() {
  const [view, setView] = useState<RevisionView>({ tab: 'pending' })

  return <>
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
  </>
}

export default function App() {
  return <AuthProvider>
    <Routes>
      <Route element={<AppShell />}>
        <Route index element={<>
          <LandingPage />
          <HealthStatus />
          {/* Komponente drugih timova sa main grane (ACC-04/ADM-08 i LAB-07) */}
          <CourseworkKt1 />
          <LabsSection />
          <CoursesPage />
          <LabVisibilitySection />
        </>} />
        <Route path="teacher/labs/new" element={<TeacherMetadataPage />} />
        <Route path="teacher/labs/:labId/edit" element={<TeacherMetadataPage />} />
        <Route path="login" element={<LoginRoute />} />
        <Route element={<ProtectedRoute />}>
          <Route path="access-demo" element={<><ProtectedApp /><RevisionsSection /></>} />
        </Route>
      </Route>
    </Routes>
  </AuthProvider>
}
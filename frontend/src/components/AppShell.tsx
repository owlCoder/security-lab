import { Link, Outlet } from 'react-router-dom'
import { useState } from 'react'
import { useAuth } from '../auth/AuthContext'

export default function AppShell() {
  const { user, logout } = useAuth()
  const [logoutError, setLogoutError] = useState('')

  async function handleLogout() {
    setLogoutError('')
    try {
      await logout()
    } catch {
      setLogoutError('Log out failed. The session is still active.')
    }
  }

  return <>
    <nav className="site-nav" aria-label="Main navigation">
      <Link className="brand-link" to="/">Security Lab</Link>
      {user && <div className="site-nav-links">
        <Link to="/access-demo">Laboratory</Link>
        <Link to="/my-assignments">Moji zadaci</Link>
        <button type="button" className="secondary" onClick={() => void handleLogout()}>
          Log out
        </button>
      </div>}
    </nav>
    {logoutError && <p className="login-error" role="alert">{logoutError}</p>}
    <Outlet />
  </>
}

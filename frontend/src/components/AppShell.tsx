import { ReactNode } from 'react'
import { NavLink, useNavigate } from 'react-router-dom'
import { useAuth } from '../hooks/useAuth'

interface AppShellProps {
  children: ReactNode
}

export default function AppShell({ children }: AppShellProps) {
  const { username, logout } = useAuth()
  const navigate = useNavigate()

  async function signOut() {
    await logout()
    navigate('/login', { replace: true })
  }

  return (
    <div className="app-shell">
      <aside className="shell-rail">
        <button
          type="button"
          onClick={() => navigate('/notebooks')}
          className="shell-brand"
          aria-label="Knowledge Desk"
        >
          <span className="shell-brand-mark">KD</span>
          <span className="shell-brand-text">Knowledge Desk</span>
        </button>

        <nav className="shell-nav" aria-label="Primary navigation">
          <NavLink
            to="/dashboard"
            className={({ isActive }) => `shell-link ${isActive ? 'shell-link-active' : ''}`}
          >
              Home
          </NavLink>
          <NavLink
            to="/notebooks"
            className={({ isActive }) => `shell-link ${isActive ? 'shell-link-active' : ''}`}
          >
              Notebooks
          </NavLink>
        </nav>

        <div className="shell-account">
          <span title={username ?? undefined}>{username}</span>
          <button type="button" onClick={signOut} className="ui-button ui-button-ghost text-xs">
            Sign out
          </button>
        </div>
      </aside>

      <main className="app-main">
        {children}
      </main>
    </div>
  )
}

import { ReactNode, useState } from 'react'
import { NavLink, useNavigate } from 'react-router-dom'
import { useAuth } from '../hooks/useAuth'

interface AppShellProps {
  children: ReactNode
}

export default function AppShell({ children }: AppShellProps) {
  const { username, isDevAdmin, logout } = useAuth()
  const navigate = useNavigate()
  const [collapsed, setCollapsed] = useState(false)

  async function signOut() {
    await logout()
    navigate('/login', { replace: true })
  }

  return (
    <div className="app-shell">
      <aside className={`shell-rail${collapsed ? ' shell-rail-collapsed' : ''}`}>
        {!collapsed && (
          <>
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
              <div className="shell-nav-group">
                <span className="shell-nav-label">Workspace</span>
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
              </div>
              {isDevAdmin && (
                <div className="shell-nav-group shell-nav-group-lab">
                  <span className="shell-nav-label">Lab</span>
                  <NavLink
                    to="/lab/retrieval-versions"
                    className={({ isActive }) => `shell-link ${isActive ? 'shell-link-active' : ''}`}
                  >
                    Versions
                  </NavLink>
                  <NavLink
                    to="/lab/reindex"
                    className={({ isActive }) => `shell-link ${isActive ? 'shell-link-active' : ''}`}
                  >
                    Re-indexing
                  </NavLink>
                  <NavLink
                    to="/lab/retrieval-bench"
                    className={({ isActive }) => `shell-link ${isActive ? 'shell-link-active' : ''}`}
                  >
                    Bench
                  </NavLink>
                </div>
              )}
            </nav>

            <div className="shell-account">
              <span title={username ?? undefined}>{username}</span>
              <button type="button" onClick={signOut} className="ui-button ui-button-ghost text-xs">
                Sign out
              </button>
            </div>
          </>
        )}

        <button
          type="button"
          className="shell-rail-toggle"
          onClick={() => setCollapsed(c => !c)}
          aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
          title={collapsed ? 'Expand' : 'Collapse'}
        >
          {collapsed ? '›' : '‹'}
        </button>
      </aside>

      <main
        className="app-main"
        style={{ marginLeft: collapsed ? '2rem' : undefined }}
      >
        {children}
      </main>
    </div>
  )
}

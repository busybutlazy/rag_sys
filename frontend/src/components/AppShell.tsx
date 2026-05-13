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
    <div className="min-h-screen bg-stone-50 text-stone-900">
      <header className="sticky top-0 z-20 border-b border-stone-200 bg-stone-50/90 px-4 backdrop-blur sm:px-6">
        <div className="mx-auto flex h-16 w-full max-w-[1280px] items-center justify-between gap-4">
          <div className="flex min-w-0 items-center gap-5">
            <button
              type="button"
              onClick={() => navigate('/notebooks')}
              className="shrink-0 text-left"
            >
              <p className="text-[11px] font-medium uppercase tracking-[0.18em] text-stone-400">RAG</p>
              <h1 className="text-lg font-semibold tracking-[-0.01em] text-stone-900">Knowledge Desk</h1>
            </button>

            <nav className="hidden gap-1 sm:flex">
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
          </div>

          <div className="flex min-w-0 items-center gap-2">
            <span className="hidden max-w-[12rem] truncate text-sm text-stone-500 md:inline">{username}</span>
            <button type="button" onClick={signOut} className="ui-button ui-button-ghost text-xs">
              Sign out
            </button>
          </div>
        </div>
        <nav className="mx-auto flex w-full max-w-[1280px] gap-1 overflow-x-auto pb-2 sm:hidden">
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
      </header>

      <main className="mx-auto min-w-0 max-w-[1280px] px-4 py-5 sm:px-6 md:px-8 md:py-8">
        {children}
      </main>
    </div>
  )
}

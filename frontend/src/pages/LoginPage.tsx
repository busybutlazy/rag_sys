import { useState, FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../hooks/useAuth'

export default function LoginPage() {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const { login } = useAuth()
  const navigate = useNavigate()

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      const res = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        credentials: 'include',
        body: JSON.stringify({ username, password }),
      })
      if (!res.ok) {
        setError('Invalid username or password')
        return
      }
      const data = await res.json()
      login(data.accessToken, username)
      navigate('/dashboard', { replace: true })
    } catch {
      setError('Network error — please try again')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-stone-50 px-4">
      <div className="w-full max-w-sm rounded-xl border border-stone-200 bg-white p-7 shadow-sm">
        <p className="eyebrow text-center">RAG</p>
        <h1 className="mb-6 mt-1 text-center text-2xl font-semibold tracking-[-0.01em] text-stone-900">Knowledge Desk</h1>
        {error && (
          <p className="mb-4 rounded-md border border-red-100 bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>
        )}
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="mb-1 block text-sm font-medium text-stone-700">Username</label>
            <input
              type="text"
              value={username}
              onChange={e => setUsername(e.target.value)}
              required
              className="ui-input"
              placeholder="admin"
            />
          </div>
          <div>
            <label className="mb-1 block text-sm font-medium text-stone-700">Password</label>
            <input
              type="password"
              value={password}
              onChange={e => setPassword(e.target.value)}
              required
              className="ui-input"
            />
          </div>
          <button
            type="submit"
            disabled={loading}
            className="ui-button ui-button-primary w-full"
          >
            {loading ? 'Signing in...' : 'Sign in'}
          </button>
        </form>
      </div>
    </div>
  )
}

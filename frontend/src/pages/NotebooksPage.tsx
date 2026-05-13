import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { apiGet, apiPost } from '../lib/api'

interface Notebook { id: string; name: string; description?: string; updatedAt: string }

export default function NotebooksPage() {
  const [notebooks, setNotebooks] = useState<Notebook[]>([])
  const [name, setName] = useState('')
  const [error, setError] = useState<string | null>(null)
  const navigate = useNavigate()

  useEffect(() => {
    apiGet<Notebook[]>('/api/notebooks').then(setNotebooks).catch(() => setError('Failed to load'))
  }, [])

  async function create(e: FormEvent) {
    e.preventDefault()
    try {
      const nb = await apiPost<Notebook>('/api/notebooks', { name })
      navigate(`/notebooks/${nb.id}`)
    } catch {
      setError('Failed to create notebook')
    }
  }

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <header className="border-b border-stone-200 pb-6">
        <p className="eyebrow">Library</p>
        <h1 className="page-title">Notebooks</h1>
      </header>

      {error && <p className="rounded-md border border-red-100 bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}

      <section className="workspace-panel">
        <form onSubmit={create} className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_auto]">
          <input
            value={name}
            onChange={e => setName(e.target.value)}
            placeholder="New notebook name"
            required
            className="ui-input"
          />
          <button type="submit" className="ui-button ui-button-primary">
            Create
          </button>
        </form>
      </section>

      {notebooks.length === 0 ? (
        <div className="empty-state">No notebooks yet.</div>
      ) : (
        <ul className="grid gap-3 md:grid-cols-2">
          {notebooks.map(nb => (
            <li key={nb.id}>
              <Link
                to={`/notebooks/${nb.id}`}
                className="block rounded-lg border border-stone-200 bg-white p-4 shadow-sm transition hover:border-stone-300 hover:bg-stone-50"
              >
                <p className="truncate text-base font-semibold text-stone-900">{nb.name}</p>
                {nb.description && <p className="mt-2 line-clamp-2 text-sm leading-6 text-stone-500">{nb.description}</p>}
                <p className="mt-4 text-xs text-stone-400">Updated {new Date(nb.updatedAt).toLocaleDateString()}</p>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

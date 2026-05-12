import { useEffect, useState } from 'react'
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

  async function create(e: React.FormEvent) {
    e.preventDefault()
    try {
      const nb = await apiPost<Notebook>('/api/notebooks', { name })
      navigate(`/notebooks/${nb.id}`)
    } catch {
      setError('Failed to create notebook')
    }
  }

  return (
    <div className="max-w-2xl mx-auto p-6">
      <h1 className="text-2xl font-bold mb-6">Notebooks</h1>
      {error && <p className="mb-4 text-sm text-red-600 bg-red-50 px-3 py-2 rounded">{error}</p>}
      <form onSubmit={create} className="flex gap-2 mb-6">
        <input
          value={name}
          onChange={e => setName(e.target.value)}
          placeholder="New notebook name…"
          required
          className="flex-1 border rounded-lg px-3 py-2 focus:outline-none focus:ring-2 focus:ring-blue-500"
        />
        <button type="submit" className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700">
          Create
        </button>
      </form>
      <ul className="space-y-2">
        {notebooks.map(nb => (
          <li key={nb.id}>
            <Link
              to={`/notebooks/${nb.id}`}
              className="block p-4 border rounded-lg hover:bg-gray-50 transition"
            >
              <p className="font-semibold">{nb.name}</p>
              {nb.description && <p className="text-sm text-gray-500">{nb.description}</p>}
            </Link>
          </li>
        ))}
        {notebooks.length === 0 && (
          <li className="text-gray-400 text-sm">No notebooks yet. Create one above.</li>
        )}
      </ul>
    </div>
  )
}

import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { apiGet, apiPost } from '../lib/api'

interface Notebook { id: string; name: string; description?: string; updatedAt: string }

function formatDate(iso: string) {
  const d = new Date(iso)
  return `${d.getFullYear()}.${String(d.getMonth() + 1).padStart(2, '0')}.${String(d.getDate()).padStart(2, '0')}`
}

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
    <div className="mx-auto max-w-5xl">

      {/* ── Header — Re Loop section header style ── */}
      <header style={{ marginBottom: '4rem' }}>
        <p className="eyebrow" style={{ marginBottom: '1rem' }}>Library</p>
        <h1 className="page-title">Notebooks</h1>
      </header>

      {error && (
        <p style={{ marginBottom: '2rem', padding: '0.75rem 1rem', border: '1px solid rgba(178,72,64,0.2)', borderRadius: '0.4rem', fontSize: '0.875rem', color: '#B24840', background: 'rgba(178,72,64,0.05)' }}>
          {error}
        </p>
      )}

      {/* ── Create form ── */}
      <section style={{ marginBottom: '4rem' }}>
        <p className="eyebrow" style={{ marginBottom: '1.4rem' }}>New Notebook</p>
        <form onSubmit={create} style={{ display: 'grid', gap: '0.75rem', gridTemplateColumns: 'minmax(0,1fr) auto' }}>
          <input
            value={name}
            onChange={e => setName(e.target.value)}
            placeholder="Notebook name"
            required
            className="ui-input"
          />
          <button type="submit" className="ui-button ui-button-primary">
            Create
          </button>
        </form>
      </section>

      {/* ── Notebook list — Re Loop .information row style ── */}
      {notebooks.length === 0 ? (
        <div className="empty-state">No notebooks yet.</div>
      ) : (
        <>
          {/* Column header */}
          <div style={{
            display: 'flex',
            alignItems: 'center',
            gap: '2.4rem',
            paddingBottom: '0.8rem',
            marginBottom: '0',
          }}>
            <span className="eyebrow" style={{ minWidth: '2.8rem' }}>#</span>
            <span className="eyebrow" style={{ minWidth: '7rem' }}>Updated</span>
            <span className="eyebrow" style={{ flex: 1 }}>Title</span>
          </div>

          <ul className="nb-list">
            {notebooks.map((nb, i) => (
              <li key={nb.id} className="nb-row">
                <Link to={`/notebooks/${nb.id}`} className="nb-row-link">
                  <span className="nb-row-num en exl">{String(i + 1).padStart(2, '0')}</span>
                  <span className="nb-row-date en">{formatDate(nb.updatedAt)}</span>
                  <span className="nb-row-title">{nb.name}</span>
                  {nb.description && (
                    <span style={{ fontSize: '0.78rem', color: 'var(--ink-soft)', minWidth: '12rem', maxWidth: '16rem', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', flexShrink: 0 }}>
                      {nb.description}
                    </span>
                  )}
                  <span className="nb-row-arrow">→</span>
                </Link>
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  )
}

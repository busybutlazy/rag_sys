import { useState } from 'react'
import type { FormEvent } from 'react'
import { apiGet } from '../lib/api'

interface ChunkResult {
  source_id: string
  chunk_index: number
  text: string
}

interface SearchResponse {
  results: ChunkResult[]
}

interface BenchmarkResponse {
  query: string
  vector: ChunkResult[]
  bm25: ChunkResult[]
  hybrid: ChunkResult[]
}

type SearchMode = 'vector' | 'bm25' | 'hybrid' | 'benchmark'

interface Props {
  notebookId: string
}

function ResultList({ chunks, label }: { chunks: ChunkResult[]; label?: string }) {
  if (chunks.length === 0) return <p className="text-xs text-stone-400">No results.</p>
  return (
    <div>
      {label && <p className="mb-2 text-xs font-semibold uppercase tracking-[0.12em] text-stone-400">{label}</p>}
      <ul className="space-y-2">
        {chunks.map((r, i) => (
          <li key={i} className="rounded-lg border border-stone-200 bg-stone-50 p-3">
            <div className="flex items-center justify-between mb-1">
              <span className="font-mono text-xs text-stone-400">
                {r.source_id.slice(0, 8)}... / chunk {r.chunk_index}
              </span>
              <span className="text-xs text-stone-400">#{i + 1}</span>
            </div>
            <p className="line-clamp-4 text-xs leading-relaxed text-stone-800">{r.text}</p>
          </li>
        ))}
      </ul>
    </div>
  )
}

export default function SearchPanel({ notebookId }: Props) {
  const [query, setQuery] = useState('')
  const [mode, setMode] = useState<SearchMode>('hybrid')
  const [results, setResults] = useState<ChunkResult[] | null>(null)
  const [benchmark, setBenchmark] = useState<BenchmarkResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function search(e: FormEvent) {
    e.preventDefault()
    const q = query.trim()
    if (!q) return

    setLoading(true)
    setError(null)
    setResults(null)
    setBenchmark(null)

    try {
      if (mode === 'benchmark') {
        const data = await apiGet<BenchmarkResponse>(
          `/api/notebooks/${notebookId}/search/benchmark?q=${encodeURIComponent(q)}&topK=5`
        )
        setBenchmark(data)
      } else {
        const data = await apiGet<SearchResponse>(
          `/api/notebooks/${notebookId}/search?q=${encodeURIComponent(q)}&mode=${mode}&topK=5`
        )
        setResults(data.results)
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Search failed')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="space-y-3">
      <form onSubmit={search} className="grid gap-2 lg:grid-cols-[minmax(0,1fr)_10rem_auto]">
        <input
          value={query}
          onChange={e => setQuery(e.target.value)}
          placeholder="Search chunks"
          className="ui-input"
        />
        <select
          value={mode}
          onChange={e => setMode(e.target.value as SearchMode)}
          className="ui-input"
        >
          <option value="hybrid">Hybrid</option>
          <option value="vector">Vector</option>
          <option value="bm25">BM25</option>
          <option value="benchmark">Benchmark</option>
        </select>
        <button
          type="submit"
          disabled={!query.trim() || loading}
          className="ui-button ui-button-primary"
        >
          {loading ? '...' : 'Search'}
        </button>
      </form>

      {error && <p className="text-xs text-red-500">{error}</p>}

      {results !== null && <ResultList chunks={results} />}

      {benchmark && (
        <div className="space-y-4">
          <ResultList chunks={benchmark.vector} label="Vector" />
          <ResultList chunks={benchmark.bm25} label="BM25" />
          <ResultList chunks={benchmark.hybrid} label="Hybrid (RRF)" />
        </div>
      )}
    </div>
  )
}

import { useEffect, useState } from 'react'
import { apiGet, apiPost } from '../lib/api'

interface ExperimentConfig {
  modes: string[]
  top_k: number
  alpha: number
}

interface ExperimentResult {
  query: string
  mode: string
  latency_ms: number
  result_count: number
  results: { source_id: string; chunk_index: number }[]
}

interface ExperimentRecord {
  id: string
  name: string
  notebook_id: string
  config: ExperimentConfig
  queries: string[]
  results: ExperimentResult[]
  created_at: string
}

interface Props {
  notebookId: string
}

const modes = ['vector', 'bm25', 'hybrid']

export default function ExperimentPanel({ notebookId }: Props) {
  const [name, setName] = useState('')
  const [queryText, setQueryText] = useState('')
  const [selectedModes, setSelectedModes] = useState<string[]>(['vector', 'bm25', 'hybrid'])
  const [topK, setTopK] = useState(5)
  const [alpha, setAlpha] = useState(0.5)
  const [experiments, setExperiments] = useState<ExperimentRecord[]>([])
  const [active, setActive] = useState<ExperimentRecord | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    loadExperiments()
  }, [notebookId])

  async function loadExperiments() {
    const data = await apiGet<ExperimentRecord[]>(`/api/notebooks/${notebookId}/experiments?limit=10`)
    setExperiments(data)
    setActive(current => current ?? data[0] ?? null)
  }

  async function run(e: React.FormEvent) {
    e.preventDefault()
    const queries = queryText.split('\n').map(q => q.trim()).filter(Boolean)
    if (queries.length === 0 || selectedModes.length === 0) return

    setLoading(true)
    setError(null)
    try {
      const record = await apiPost<ExperimentRecord>(`/api/notebooks/${notebookId}/experiments`, {
        name: name || null,
        queries,
        config: { modes: selectedModes, top_k: topK, alpha },
      })
      setActive(record)
      setExperiments(prev => [record, ...prev.filter(e => e.id !== record.id)].slice(0, 10))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Experiment failed')
    } finally {
      setLoading(false)
    }
  }

  function toggleMode(mode: string) {
    setSelectedModes(prev =>
      prev.includes(mode) ? prev.filter(m => m !== mode) : [...prev, mode],
    )
  }

  return (
    <div className="space-y-4">
      <form onSubmit={run} className="space-y-3">
        <div className="grid gap-2 sm:grid-cols-[1fr_8rem_8rem]">
          <input
            value={name}
            onChange={e => setName(e.target.value)}
            placeholder="Experiment name"
            className="border rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
          <input
            type="number"
            min={1}
            max={20}
            value={topK}
            onChange={e => setTopK(Number(e.target.value))}
            className="border rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            aria-label="Top K"
            title="Top K"
          />
          <input
            type="number"
            min={0}
            max={1}
            step={0.1}
            value={alpha}
            onChange={e => setAlpha(Number(e.target.value))}
            className="border rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
            aria-label="Hybrid alpha"
            title="Hybrid alpha"
          />
        </div>

        <textarea
          value={queryText}
          onChange={e => setQueryText(e.target.value)}
          placeholder="One query per line"
          rows={4}
          className="w-full border rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
        />

        <div className="flex flex-wrap items-center gap-2">
          {modes.map(mode => (
            <button
              key={mode}
              type="button"
              onClick={() => toggleMode(mode)}
              className={`px-3 py-1.5 rounded-lg border text-sm ${
                selectedModes.includes(mode) ? 'bg-blue-600 text-white border-blue-600' : 'bg-white text-gray-700'
              }`}
            >
              {mode}
            </button>
          ))}
          <button
            type="submit"
            disabled={loading || !queryText.trim() || selectedModes.length === 0}
            className="ml-auto px-3 py-1.5 bg-green-600 text-white rounded-lg text-sm hover:bg-green-700 disabled:opacity-40"
          >
            {loading ? 'Running...' : 'Run'}
          </button>
        </div>
      </form>

      {error && <p className="text-xs text-red-500">{error}</p>}

      {experiments.length > 0 && (
        <div className="flex flex-wrap gap-2">
          {experiments.map(exp => (
            <button
              key={exp.id}
              onClick={() => setActive(exp)}
              className={`px-2 py-1 rounded border text-xs ${
                active?.id === exp.id ? 'bg-gray-900 text-white' : 'bg-white text-gray-600'
              }`}
            >
              {exp.name}
            </button>
          ))}
        </div>
      )}

      {active && (
        <div className="border rounded-lg overflow-hidden">
          <div className="bg-gray-50 px-3 py-2 border-b">
            <p className="text-sm font-semibold">{active.name}</p>
            <p className="text-xs text-gray-500">
              {active.config.modes.join(', ')} · top {active.config.top_k} · alpha {active.config.alpha}
            </p>
          </div>
          <table className="w-full text-sm">
            <thead className="bg-white text-xs text-gray-500">
              <tr>
                <th className="text-left px-3 py-2">Query</th>
                <th className="text-left px-3 py-2">Mode</th>
                <th className="text-right px-3 py-2">Latency</th>
                <th className="text-right px-3 py-2">Results</th>
              </tr>
            </thead>
            <tbody>
              {active.results.map((r, i) => (
                <tr key={i} className="border-t">
                  <td className="px-3 py-2 max-w-[18rem] truncate">{r.query}</td>
                  <td className="px-3 py-2 font-mono text-xs">{r.mode}</td>
                  <td className="px-3 py-2 text-right">{r.latency_ms} ms</td>
                  <td className="px-3 py-2 text-right">{r.result_count}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

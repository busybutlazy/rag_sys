import { useEffect, useState } from 'react'
import { apiGet, apiPost } from '../lib/api'

type Notebook = { id: string; name: string }
type Version = { id: string; notes?: string; chunkSize: number; chunkOverlap: number; embeddingModel: string; active: boolean }
type Dataset = { id: string; name: string; description?: string }
type Query = { id: string; queryText: string; sortOrder: number }
type DatasetDetail = Dataset & { queries: Query[] }
type Chunk = { sourceId: string; chunkIndex: number; text: string }
type Comparison = {
  mode: string
  versionA: { latencyMs: number; results: Chunk[] }
  versionB: { latencyMs: number; results: Chunk[] }
  metrics: { overlapAtK: number; sourceOverlap: number; resultCountDelta: number; latencyDeltaMs: number }
}
type CompareResponse = { query: string; comparisons: Comparison[] }
type RunSummary = { id: string; datasetId?: string; status: string; createdAt: string }
type RunDetail = { comparisons: Array<{ queryTextSnapshot: string; mode: string; metrics: Comparison['metrics'] }> }

export default function LabRetrievalBenchPage() {
  const [notebooks, setNotebooks] = useState<Notebook[]>([])
  const [notebookId, setNotebookId] = useState('')
  const [versions, setVersions] = useState<Version[]>([])
  const [versionA, setVersionA] = useState('')
  const [versionB, setVersionB] = useState('')
  const [datasets, setDatasets] = useState<Dataset[]>([])
  const [datasetId, setDatasetId] = useState('')
  const [datasetDetail, setDatasetDetail] = useState<DatasetDetail | null>(null)
  const [datasetName, setDatasetName] = useState('')
  const [newQuery, setNewQuery] = useState('')
  const [query, setQuery] = useState('')
  const [comparison, setComparison] = useState<CompareResponse | null>(null)
  const [runs, setRuns] = useState<RunSummary[]>([])
  const [runDetail, setRunDetail] = useState<RunDetail | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    apiGet<Notebook[]>('/api/notebooks').then(list => {
      setNotebooks(list)
      setNotebookId(list[0]?.id ?? '')
    })
  }, [])

  useEffect(() => {
    if (!notebookId) return
    Promise.all([
      apiGet<Version[]>(`/api/lab/notebooks/${notebookId}/retrieval-versions`),
      apiGet<Dataset[]>(`/api/lab/notebooks/${notebookId}/evaluation-datasets`),
      apiGet<RunSummary[]>(`/api/lab/notebooks/${notebookId}/retrieval-bench/runs`),
    ]).then(([nextVersions, nextDatasets, nextRuns]) => {
      setVersions(nextVersions)
      setVersionA(nextVersions.find(v => v.active)?.id ?? nextVersions[0]?.id ?? '')
      setVersionB(nextVersions.find(v => !v.active)?.id ?? nextVersions[1]?.id ?? nextVersions[0]?.id ?? '')
      setDatasets(nextDatasets)
      setDatasetId(nextDatasets[0]?.id ?? '')
      setRuns(nextRuns)
    })
  }, [notebookId])

  useEffect(() => {
    if (!datasetId) { setDatasetDetail(null); return }
    apiGet<DatasetDetail>(`/api/lab/evaluation-datasets/${datasetId}`).then(setDatasetDetail)
  }, [datasetId])

  async function compare() {
    if (!query.trim() || !versionA || !versionB) return
    setBusy(true); setError('')
    try {
      setComparison(await apiPost<CompareResponse>(`/api/lab/notebooks/${notebookId}/retrieval-bench/compare`, {
        query,
        retrievalVersionAId: versionA,
        retrievalVersionBId: versionB,
        modes: ['hybrid'],
        topK: 5,
        alpha: 0.5,
      }))
    } catch (e) { setError(e instanceof Error ? e.message : 'Compare failed') }
    finally { setBusy(false) }
  }

  async function createDataset() {
    if (!datasetName.trim()) return
    const created = await apiPost<Dataset>(`/api/lab/notebooks/${notebookId}/evaluation-datasets`, { name: datasetName })
    const next = [created, ...datasets]
    setDatasets(next); setDatasetId(created.id); setDatasetName('')
  }

  async function addQuery() {
    if (!datasetId || !newQuery.trim()) return
    await apiPost(`/api/lab/evaluation-datasets/${datasetId}/queries`, { queryText: newQuery })
    setDatasetDetail(await apiGet(`/api/lab/evaluation-datasets/${datasetId}`))
    setNewQuery('')
  }

  async function runDataset() {
    if (!datasetId || !versionA || !versionB) return
    setBusy(true); setError('')
    try {
      await apiPost(`/api/lab/notebooks/${notebookId}/retrieval-bench/runs`, {
        datasetId,
        retrievalVersionAId: versionA,
        retrievalVersionBId: versionB,
        modes: ['hybrid'],
        topK: 5,
        alpha: 0.5,
      })
      setRuns(await apiGet(`/api/lab/notebooks/${notebookId}/retrieval-bench/runs`))
    } catch (e) { setError(e instanceof Error ? e.message : 'Run failed') }
    finally { setBusy(false) }
  }

  const activeComparison = comparison?.comparisons[0]
  const versionLabel = (v: Version) =>
    `${v.active ? 'Active · ' : ''}${v.id.slice(0, 8)} · ${v.chunkSize}/${v.chunkOverlap} · ${v.embeddingModel}${v.notes ? ` · ${v.notes}` : ''}`

  return (
    <div className="page-stack">
      <header>
        <p className="eyebrow">Lab</p>
        <h1 className="page-title">Retrieval bench</h1>
        <p className="muted">Compare two retrieval versions against the same notebook questions.</p>
      </header>

      <section className="workspace-panel">
        <div className="grid gap-3 md:grid-cols-3">
          <label><span className="field-label">Notebook</span><select className="ui-input" value={notebookId} onChange={e => setNotebookId(e.target.value)}>{notebooks.map(n => <option key={n.id} value={n.id}>{n.name}</option>)}</select></label>
          <label><span className="field-label">Version A</span><select className="ui-input" value={versionA} onChange={e => setVersionA(e.target.value)}>{versions.map(v => <option key={v.id} value={v.id}>{versionLabel(v)}</option>)}</select></label>
          <label><span className="field-label">Version B</span><select className="ui-input" value={versionB} onChange={e => setVersionB(e.target.value)}>{versions.map(v => <option key={v.id} value={v.id}>{versionLabel(v)}</option>)}</select></label>
        </div>
      </section>

      <section className="workspace-panel">
        <h2 className="section-title">Ad hoc compare</h2>
        <div className="surface-row" style={{ gap: '0.75rem' }}>
          <input className="ui-input" value={query} onChange={e => setQuery(e.target.value)} placeholder="Ask one benchmark query" />
          <button className="ui-button" onClick={compare} disabled={busy}>Compare</button>
        </div>
        {activeComparison && (
          <>
            <p className="muted" style={{ marginTop: '0.75rem' }}>
              overlap@k {activeComparison.metrics.overlapAtK} · source overlap {activeComparison.metrics.sourceOverlap} · latency Δ {activeComparison.metrics.latencyDeltaMs} ms
            </p>
            <div className="grid gap-3 md:grid-cols-2" style={{ marginTop: '0.75rem' }}>
              {[activeComparison.versionA, activeComparison.versionB].map((side, idx) => (
                <div key={idx} className="stack-list">
                  <strong>{idx === 0 ? 'Version A' : 'Version B'} · {side.latencyMs} ms</strong>
                  {side.results.map((r, i) => <div key={`${r.sourceId}-${r.chunkIndex}`} className="surface-row"><span>{i + 1}. {r.sourceId}:{r.chunkIndex}</span></div>)}
                </div>
              ))}
            </div>
          </>
        )}
      </section>

      <section className="workspace-panel">
        <h2 className="section-title">Datasets</h2>
        <div className="surface-row" style={{ gap: '0.75rem' }}>
          <select className="ui-input" value={datasetId} onChange={e => setDatasetId(e.target.value)}>
            <option value="">Select dataset</option>
            {datasets.map(d => <option key={d.id} value={d.id}>{d.name}</option>)}
          </select>
          <input className="ui-input" value={datasetName} onChange={e => setDatasetName(e.target.value)} placeholder="New dataset name" />
          <button className="ui-button ui-button-ghost" onClick={createDataset}>Create</button>
        </div>
        {datasetDetail && (
          <div style={{ marginTop: '0.75rem' }}>
            <div className="surface-row" style={{ gap: '0.75rem' }}>
              <input className="ui-input" value={newQuery} onChange={e => setNewQuery(e.target.value)} placeholder="Add query" />
              <button className="ui-button ui-button-ghost" onClick={addQuery}>Add</button>
              <button className="ui-button" onClick={runDataset} disabled={busy || datasetDetail.queries.length === 0}>Run dataset</button>
            </div>
            <div className="stack-list" style={{ marginTop: '0.75rem' }}>
              {datasetDetail.queries.map(q => <div key={q.id} className="surface-row">{q.sortOrder + 1}. {q.queryText}</div>)}
            </div>
          </div>
        )}
      </section>

      <section className="workspace-panel">
        <h2 className="section-title">Recent runs</h2>
        <div className="stack-list">
          {runs.map(r => (
            <button
              key={r.id}
              type="button"
              className="surface-row"
              onClick={async () => setRunDetail(await apiGet(`/api/lab/retrieval-bench/runs/${r.id}`))}
            >
              <span>{r.id.slice(0, 8)} · {r.status}</span>
              <span className="muted">{new Date(r.createdAt).toLocaleString()}</span>
            </button>
          ))}
        </div>
        {runDetail && (
          <div className="stack-list" style={{ marginTop: '0.75rem' }}>
            {runDetail.comparisons.map((c, i) => (
              <div key={`${c.queryTextSnapshot}-${i}`} className="surface-row">
                <span>{c.queryTextSnapshot} · {c.mode}</span>
                <span className="muted">overlap@k {c.metrics.overlapAtK} · latency Δ {c.metrics.latencyDeltaMs} ms</span>
              </div>
            ))}
          </div>
        )}
      </section>

      {error && <p className="error-text">{error}</p>}
    </div>
  )
}

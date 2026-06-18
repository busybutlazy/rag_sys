import { useEffect, useState } from 'react'
import { apiGet, apiPost } from '../lib/api'

type Notebook = { id: string; name: string }
type Version = { id: string; notes?: string; chunkSize: number; chunkOverlap: number; embeddingModel: string; active: boolean }
type Dataset = { id: string; name: string; description?: string }
type Query = { id: string; queryText: string; sortOrder: number }
type DatasetDetail = Dataset & { queries: Query[] }
type Chunk = { sourceId: string; chunkIndex: number; text: string; factId?: string; factText?: string; participants?: string[] }
type Metrics = {
  overlapAtK: number
  sourceOverlap: number
  resultCountDelta: number
  latencyDeltaMs: number
  graphHitRateA: number
  graphHitRateB: number
  factCoverageA: number
  factCoverageB: number
}
type Comparison = {
  mode: string
  versionA: { latencyMs: number; results: Chunk[] }
  versionB: { latencyMs: number; results: Chunk[] }
  metrics: Metrics
}
type CompareResponse = { query: string; comparisons: Comparison[] }
type RunSummary = { id: string; datasetId?: string; status: string; createdAt: string }
type RunDetail = { comparisons: Array<{ queryTextSnapshot: string; mode: string; metrics: Metrics }> }

const ALL_MODES = ['vector', 'bm25', 'hybrid', 'graph_hybrid']

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
  const [modes, setModes] = useState<string[]>(['hybrid'])
  const [comparison, setComparison] = useState<CompareResponse | null>(null)
  const [runs, setRuns] = useState<RunSummary[]>([])
  const [runDetail, setRunDetail] = useState<RunDetail | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [loadingMessage, setLoadingMessage] = useState('Loading notebooks...')

  useEffect(() => {
    apiGet<Notebook[]>('/api/notebooks')
      .then(list => {
        setNotebooks(list)
        setNotebookId(list[0]?.id ?? '')
        setLoadingMessage(list.length === 0 ? 'No notebooks available yet.' : '')
      })
      .catch(e => {
        setError(e instanceof Error ? e.message : 'Failed to load notebooks')
        setLoadingMessage('Unable to load notebooks.')
      })
  }, [])

  useEffect(() => {
    if (!notebookId) return
    setError('')
    apiGet<Version[]>(`/api/lab/notebooks/${notebookId}/retrieval-versions`)
      .then(nextVersions => {
        setVersions(nextVersions)
        setVersionA(nextVersions.find(v => v.active)?.id ?? nextVersions[0]?.id ?? '')
        setVersionB(nextVersions.find(v => !v.active)?.id ?? nextVersions[1]?.id ?? nextVersions[0]?.id ?? '')
      })
      .catch(e => setError(e instanceof Error ? e.message : 'Failed to load retrieval versions'))

    apiGet<Dataset[]>(`/api/lab/notebooks/${notebookId}/evaluation-datasets`)
      .then(nextDatasets => {
        setDatasets(nextDatasets)
        setDatasetId(nextDatasets[0]?.id ?? '')
      })
      .catch(e => setError(e instanceof Error ? e.message : 'Failed to load datasets'))

    apiGet<RunSummary[]>(`/api/lab/notebooks/${notebookId}/retrieval-bench/runs`)
      .then(setRuns)
      .catch(e => setError(e instanceof Error ? e.message : 'Failed to load runs'))
  }, [notebookId])

  useEffect(() => {
    if (!datasetId) { setDatasetDetail(null); return }
    apiGet<DatasetDetail>(`/api/lab/evaluation-datasets/${datasetId}`).then(setDatasetDetail)
  }, [datasetId])

  function toggleMode(mode: string) {
    setModes(prev => prev.includes(mode) ? prev.filter(m => m !== mode) : [...prev, mode])
  }

  async function compare() {
    if (!query.trim() || !versionA || !versionB || modes.length === 0) return
    setBusy(true); setError('')
    try {
      setComparison(await apiPost<CompareResponse>(`/api/lab/notebooks/${notebookId}/retrieval-bench/compare`, {
        query,
        retrievalVersionAId: versionA,
        retrievalVersionBId: versionB,
        modes,
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
    if (!datasetId || !versionA || !versionB || modes.length === 0) return
    setBusy(true); setError('')
    try {
      await apiPost(`/api/lab/notebooks/${notebookId}/retrieval-bench/runs`, {
        datasetId,
        retrievalVersionAId: versionA,
        retrievalVersionBId: versionB,
        modes,
        topK: 5,
        alpha: 0.5,
      })
      setRuns(await apiGet(`/api/lab/notebooks/${notebookId}/retrieval-bench/runs`))
    } catch (e) { setError(e instanceof Error ? e.message : 'Run failed') }
    finally { setBusy(false) }
  }

  const versionLabel = (v: Version) =>
    `${v.active ? 'Active · ' : ''}${v.id.slice(0, 8)} · ${v.chunkSize}/${v.chunkOverlap} · ${v.embeddingModel}${v.notes ? ` · ${v.notes}` : ''}`

  return (
    <div className="lab-page">
      <header className="lab-hero">
        <p className="eyebrow">Lab</p>
        <h1 className="page-title">Retrieval bench</h1>
        <p className="muted">Compare two retrieval versions against the same notebook questions.</p>
      </header>

      <section className="workspace-panel">
        <div className="lab-toolbar lab-toolbar-wide">
          <label><span className="field-label">Notebook</span><select className="ui-input" value={notebookId} onChange={e => setNotebookId(e.target.value)}>{notebooks.map(n => <option key={n.id} value={n.id}>{n.name}</option>)}</select></label>
          <label><span className="field-label">Version A</span><select className="ui-input" value={versionA} onChange={e => setVersionA(e.target.value)}>{versions.length === 0 && <option value="">No versions available</option>}{versions.map(v => <option key={v.id} value={v.id}>{versionLabel(v)}</option>)}</select></label>
          <label><span className="field-label">Version B</span><select className="ui-input" value={versionB} onChange={e => setVersionB(e.target.value)}>{versions.length === 0 && <option value="">No versions available</option>}{versions.map(v => <option key={v.id} value={v.id}>{versionLabel(v)}</option>)}</select></label>
        </div>
        <div className="lab-toolbar" style={{ marginTop: '0.75rem' }}>
          <span className="field-label">Modes</span>
          {ALL_MODES.map(mode => (
            <label key={mode} className="lab-stat" style={{ cursor: 'pointer' }}>
              <input type="checkbox" checked={modes.includes(mode)} onChange={() => toggleMode(mode)} style={{ marginRight: '0.35rem' }} />
              {mode}
            </label>
          ))}
        </div>
        {loadingMessage && <p className="muted" style={{ marginTop: '0.75rem' }}>{loadingMessage}</p>}
      </section>

      <section className="workspace-panel">
        <div className="lab-panel-head">
          <div>
            <h2 className="section-title">Ad hoc compare</h2>
            <p className="muted">One question, two versions, same retrieval path.</p>
          </div>
        </div>
        <div className="lab-inline-form">
          <input className="ui-input" value={query} onChange={e => setQuery(e.target.value)} placeholder="Ask one benchmark query" />
          <button className="ui-button" onClick={compare} disabled={busy}>Compare</button>
        </div>
        {comparison && comparison.comparisons.map(c => (
          <div key={c.mode} style={{ marginTop: '1rem' }}>
            <strong>{c.mode}</strong>
            <div className="lab-stat-strip">
              <span className="lab-stat">overlap@k {c.metrics.overlapAtK}</span>
              <span className="lab-stat">source overlap {c.metrics.sourceOverlap}</span>
              <span className="lab-stat">latency Δ {c.metrics.latencyDeltaMs} ms</span>
              {c.mode === 'graph_hybrid' && (
                <>
                  <span className="lab-stat">graph hit rate A {(c.metrics.graphHitRateA * 100).toFixed(0)}%</span>
                  <span className="lab-stat">graph hit rate B {(c.metrics.graphHitRateB * 100).toFixed(0)}%</span>
                  <span className="lab-stat">fact coverage A {c.metrics.factCoverageA}</span>
                  <span className="lab-stat">fact coverage B {c.metrics.factCoverageB}</span>
                </>
              )}
            </div>
            <div className="lab-result-columns">
              {[c.versionA, c.versionB].map((side, idx) => (
                <div key={idx} className="lab-subpanel">
                  <strong>{idx === 0 ? 'Version A' : 'Version B'} · {side.latencyMs} ms</strong>
                  <div className="lab-card-list" style={{ marginTop: '0.75rem' }}>
                    {side.results.map((r, i) => (
                      <div key={`${r.sourceId}-${r.chunkIndex}`} className="lab-card">
                        <span>{i + 1}. {r.sourceId}:{r.chunkIndex}</span>
                        {r.factId && (
                          <span className="muted" title={r.participants?.join(', ')}>
                            {' '}· fact: {r.factText}{r.participants && r.participants.length > 0 ? ` (${r.participants.join(', ')})` : ''}
                          </span>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </div>
        ))}
      </section>

      <section className="workspace-panel">
        <div className="lab-panel-head">
          <div>
            <h2 className="section-title">Datasets</h2>
            <p className="muted">Reusable query sets for repeated comparisons.</p>
          </div>
        </div>
        <div className="lab-inline-form">
          <select className="ui-input" value={datasetId} onChange={e => setDatasetId(e.target.value)}>
            <option value="">Select dataset</option>
            {datasets.map(d => <option key={d.id} value={d.id}>{d.name}</option>)}
          </select>
          <input className="ui-input" value={datasetName} onChange={e => setDatasetName(e.target.value)} placeholder="New dataset name" />
          <button className="ui-button ui-button-ghost" onClick={createDataset}>Create</button>
        </div>
        {datasetDetail && (
          <div style={{ marginTop: '0.75rem' }}>
            <div className="lab-inline-form">
              <input className="ui-input" value={newQuery} onChange={e => setNewQuery(e.target.value)} placeholder="Add query" />
              <button className="ui-button ui-button-ghost" onClick={addQuery}>Add</button>
              <button className="ui-button" onClick={runDataset} disabled={busy || datasetDetail.queries.length === 0}>Run dataset</button>
            </div>
            <div className="lab-card-list" style={{ marginTop: '0.75rem' }}>
              {datasetDetail.queries.map(q => <div key={q.id} className="lab-card">{q.sortOrder + 1}. {q.queryText}</div>)}
            </div>
          </div>
        )}
      </section>

      <section className="workspace-panel">
        <div className="lab-panel-head">
          <div>
            <h2 className="section-title">Recent runs</h2>
            <p className="muted">Reopen earlier evidence without rerunning the corpus.</p>
          </div>
        </div>
        <div className="lab-card-list">
          {runs.map(r => (
            <button
              key={r.id}
              type="button"
              className="lab-card"
              onClick={async () => setRunDetail(await apiGet(`/api/lab/retrieval-bench/runs/${r.id}`))}
            >
              <span>{r.id.slice(0, 8)} · {r.status}</span>
              <span className="muted">{new Date(r.createdAt).toLocaleString()}</span>
            </button>
          ))}
        </div>
        {runDetail && (
          <div className="lab-card-list" style={{ marginTop: '0.75rem' }}>
            {runDetail.comparisons.map((c, i) => (
              <div key={`${c.queryTextSnapshot}-${i}`} className="lab-card">
                <span>{c.queryTextSnapshot} · {c.mode}</span>
                <span className="muted">
                  overlap@k {c.metrics.overlapAtK} · latency Δ {c.metrics.latencyDeltaMs} ms
                  {c.mode === 'graph_hybrid' && (
                    <> · fact coverage A/B {c.metrics.factCoverageA}/{c.metrics.factCoverageB}</>
                  )}
                </span>
              </div>
            ))}
          </div>
        )}
      </section>

      {error && <p className="error-text">{error}</p>}
    </div>
  )
}

import { useEffect, useState } from 'react'
import { apiGet, apiPost } from '../lib/api'

type Notebook = { id: string; name: string; activeRetrievalVersionId?: string }
type Version = { id: string; chunkSize: number; chunkOverlap: number; embeddingModel: string; defaultSearchMode: string; active: boolean }
type ReindexJob = {
  id: string
  scope: 'source' | 'notebook'
  sourceId?: string
  notebookId: string
  targetRetrievalVersionId: string
  previousRetrievalVersionId?: string
  status: string
  sourcesTotal: number
  sourcesSucceeded: number
  sourcesFailed: number
  lastError?: string
  startedAt?: string
  completedAt?: string
  createdAt: string
}

const STATUS_CLASSES: Record<string, string> = {
  queued: 'status-chip status-queued',
  running: 'status-chip status-running',
  succeeded: 'status-chip status-succeeded',
  failed: 'status-chip status-failed',
  retrying: 'status-chip status-retrying',
  cancelled: 'status-chip status-cancelled',
}

function fmt(iso?: string) {
  if (!iso) return '—'
  return new Date(iso).toLocaleString()
}

export default function LabReindexPage() {
  const [notebooks, setNotebooks] = useState<Notebook[]>([])
  const [notebookId, setNotebookId] = useState('')
  const [versions, setVersions] = useState<Version[]>([])
  const [jobs, setJobs] = useState<ReindexJob[]>([])
  const [targetVersionId, setTargetVersionId] = useState('')
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
      apiGet<ReindexJob[]>(`/api/lab/notebooks/${notebookId}/reindex-jobs`),
    ]).then(([vers, jobList]) => {
      setVersions(vers)
      setJobs(jobList)
      setTargetVersionId(vers.find(v => !v.active)?.id ?? vers[0]?.id ?? '')
    })
  }, [notebookId])

  async function refresh() {
    const [vers, jobList] = await Promise.all([
      apiGet<Version[]>(`/api/lab/notebooks/${notebookId}/retrieval-versions`),
      apiGet<ReindexJob[]>(`/api/lab/notebooks/${notebookId}/reindex-jobs`),
    ])
    setVersions(vers)
    setJobs(jobList)
  }

  async function queueNotebook() {
    if (!targetVersionId) return
    setBusy(true); setError('')
    try {
      await apiPost(`/api/lab/notebooks/${notebookId}/reindex`, { targetVersionId })
      await refresh()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to queue reindex')
    } finally { setBusy(false) }
  }

  async function promote(jobId: string) {
    setBusy(true); setError('')
    try {
      await apiPost(`/api/lab/reindex-jobs/${jobId}/promote`, {})
      await refresh()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to promote')
    } finally { setBusy(false) }
  }

  async function cancel(jobId: string) {
    setBusy(true); setError('')
    try {
      await apiPost(`/api/lab/reindex-jobs/${jobId}/cancel`, {})
      await refresh()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to cancel')
    } finally { setBusy(false) }
  }

  const activeVersion = versions.find(v => v.active)
  const nonActiveVersions = versions.filter(v => !v.active)

  return (
    <div className="lab-page">
      <header className="lab-hero">
        <p className="eyebrow">Lab</p>
        <h1 className="page-title">Re-indexing</h1>
        <p className="muted">Rebuild a notebook's index under a new retrieval version. Old chunks are preserved until you promote.</p>
      </header>

      <section className="workspace-panel">
        <label>
          <span className="field-label">Notebook</span>
          <select value={notebookId} onChange={e => setNotebookId(e.target.value)} className="ui-input">
            {notebooks.map(n => <option key={n.id} value={n.id}>{n.name}</option>)}
          </select>
        </label>
      </section>

      {notebookId && (
        <section className="workspace-panel">
          <div className="lab-panel-head">
            <div>
              <h2 className="section-title">Queue a rebuild</h2>
              <p className="muted">
                Active: <code>{activeVersion ? `${activeVersion.id.slice(0, 8)} · ${activeVersion.chunkSize}/${activeVersion.chunkOverlap}` : 'none'}</code>
              </p>
            </div>
          </div>
          {nonActiveVersions.length === 0 ? (
            <p className="muted">No other retrieval versions available. Create one in Retrieval Versions first.</p>
          ) : (
            <div className="lab-inline-form">
              <label>
                <span className="field-label">Target version</span>
                <select value={targetVersionId} onChange={e => setTargetVersionId(e.target.value)} className="ui-input">
                  {nonActiveVersions.map(v => (
                    <option key={v.id} value={v.id}>
                      {v.id.slice(0, 8)} · {v.chunkSize}/{v.chunkOverlap} · {v.embeddingModel} · {v.defaultSearchMode}
                    </option>
                  ))}
                </select>
              </label>
              <button className="ui-button" onClick={queueNotebook} disabled={busy || !targetVersionId}>
                Re-index notebook
              </button>
            </div>
          )}
          {error && <p className="error-text" style={{ marginTop: '0.5rem' }}>{error}</p>}
        </section>
      )}

      {jobs.length > 0 && (
        <section className="workspace-panel">
          <div className="lab-panel-head">
            <div>
              <h2 className="section-title">Jobs</h2>
              <p className="muted">Promotion happens only after a successful rebuild.</p>
            </div>
            <button className="ui-button ui-button-ghost" onClick={refresh} disabled={busy}>Refresh</button>
          </div>
          <div className="lab-card-list">
            {jobs.map(job => (
              <div key={job.id} className="lab-card lab-job">
                <div className="lab-job-topline">
                  <span className={STATUS_CLASSES[job.status] ?? 'status-chip'}>{job.status}</span>
                  <span className="muted" style={{ fontSize: '0.75rem' }}>{job.scope}</span>
                  <span style={{ flex: 1, fontFamily: 'monospace', fontSize: '0.75rem' }}>
                    {job.targetRetrievalVersionId.slice(0, 8)}
                  </span>
                  {job.scope === 'notebook' && job.sourcesTotal > 0 && (
                    <span className="muted" style={{ fontSize: '0.75rem' }}>
                      {job.sourcesSucceeded}/{job.sourcesTotal} sources
                      {job.sourcesFailed > 0 && <span style={{ color: 'var(--color-danger)' }}> · {job.sourcesFailed} failed</span>}
                    </span>
                  )}
                  <div style={{ display: 'flex', gap: '0.5rem' }}>
                    {job.status === 'succeeded' && (
                      <button className="ui-button" onClick={() => promote(job.id)} disabled={busy}>Promote</button>
                    )}
                    {(job.status === 'queued' || job.status === 'retrying') && (
                      <button className="ui-button ui-button-ghost" onClick={() => cancel(job.id)} disabled={busy}>Cancel</button>
                    )}
                  </div>
                </div>
                <div className="muted" style={{ fontSize: '0.75rem' }}>
                  Created {fmt(job.createdAt)}
                  {job.startedAt && ` · Started ${fmt(job.startedAt)}`}
                  {job.completedAt && ` · Completed ${fmt(job.completedAt)}`}
                </div>
                {job.lastError && (
                  <p style={{ fontSize: '0.75rem', color: 'var(--color-danger)', margin: 0 }}>{job.lastError}</p>
                )}
              </div>
            ))}
          </div>
        </section>
      )}
    </div>
  )
}

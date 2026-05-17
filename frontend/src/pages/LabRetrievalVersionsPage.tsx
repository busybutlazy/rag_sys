import { useEffect, useState } from 'react'
import { apiGet, apiPost } from '../lib/api'

type Notebook = { id: string; name: string }
type Preset = { id: string; key: string; name: string; description?: string }
type Version = {
  id: string
  parentVersionId?: string
  originPresetId?: string
  chunkSize: number
  chunkOverlap: number
  embeddingModel: string
  defaultSearchMode: string
  defaultTopK: number
  active: boolean
}

export default function LabRetrievalVersionsPage() {
  const [notebooks, setNotebooks] = useState<Notebook[]>([])
  const [presets, setPresets] = useState<Preset[]>([])
  const [versions, setVersions] = useState<Version[]>([])
  const [notebookId, setNotebookId] = useState('')

  useEffect(() => {
    Promise.all([
      apiGet<Notebook[]>('/api/notebooks'),
      apiGet<Preset[]>('/api/lab/retrieval-presets'),
    ]).then(([nextNotebooks, nextPresets]) => {
      setNotebooks(nextNotebooks)
      setPresets(nextPresets)
      setNotebookId(nextNotebooks[0]?.id ?? '')
    })
  }, [])

  useEffect(() => {
    if (!notebookId) return
    apiGet<Version[]>(`/api/lab/notebooks/${notebookId}/retrieval-versions`).then(setVersions)
  }, [notebookId])

  async function createFromPreset(presetKey: string) {
    await apiPost(`/api/lab/notebooks/${notebookId}/retrieval-versions`, { presetKey })
    setVersions(await apiGet(`/api/lab/notebooks/${notebookId}/retrieval-versions`))
  }

  async function activate(versionId: string) {
    await apiPost(`/api/lab/notebooks/${notebookId}/retrieval-versions/${versionId}/activate`, {})
    setVersions(await apiGet(`/api/lab/notebooks/${notebookId}/retrieval-versions`))
  }

  return (
    <div className="page-stack">
      <header>
        <p className="eyebrow">Lab</p>
        <h1 className="page-title">Retrieval versions</h1>
        <p className="muted">Active config and indexed payload can diverge until re-indexing arrives in Phase 17.</p>
      </header>

      <section className="workspace-panel">
        <label className="field-label">Notebook</label>
        <select value={notebookId} onChange={e => setNotebookId(e.target.value)} className="ui-input">
          {notebooks.map(n => <option key={n.id} value={n.id}>{n.name}</option>)}
        </select>
      </section>

      <section className="workspace-panel">
        <h2 className="section-title">Starter presets</h2>
        <div className="stack-list">
          {presets.map(p => (
            <div key={p.id} className="surface-row">
              <div><strong>{p.name}</strong><p className="muted">{p.description}</p></div>
              <button className="ui-button" onClick={() => createFromPreset(p.key)}>Create version</button>
            </div>
          ))}
        </div>
      </section>

      <section className="workspace-panel">
        <h2 className="section-title">Notebook history</h2>
        <div className="stack-list">
          {versions.map(v => (
            <div key={v.id} className="surface-row">
              <div>
                <strong>{v.active ? 'Active · ' : ''}{v.id.slice(0, 8)}</strong>
                <p className="muted">{v.chunkSize}/{v.chunkOverlap} · {v.embeddingModel} · {v.defaultSearchMode} top {v.defaultTopK}</p>
              </div>
              {!v.active && <button className="ui-button ui-button-ghost" onClick={() => activate(v.id)}>Activate</button>}
            </div>
          ))}
        </div>
      </section>
    </div>
  )
}

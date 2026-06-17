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
    <div className="lab-page">
      <header className="lab-hero">
        <p className="eyebrow">Lab</p>
        <h1 className="page-title">Retrieval versions</h1>
        <p className="muted">Create immutable retrieval variants, then decide which one becomes the notebook's live configuration.</p>
      </header>

      <section className="workspace-panel">
        <label>
          <span className="field-label">Notebook</span>
          <select value={notebookId} onChange={e => setNotebookId(e.target.value)} className="ui-input">
            {notebooks.map(n => <option key={n.id} value={n.id}>{n.name}</option>)}
          </select>
        </label>
      </section>

      <div className="lab-grid lab-grid-two">
        <section className="workspace-panel">
          <div className="lab-panel-head">
            <div>
              <h2 className="section-title">Starter presets</h2>
              <p className="muted">Create a fresh branch from a known baseline.</p>
            </div>
          </div>
          <div className="lab-card-list">
            {presets.map(p => (
              <div key={p.id} className="lab-card">
                <div className="lab-card-main">
                  <strong>{p.name}</strong>
                  <p className="lab-meta">{p.description}</p>
                </div>
                <button className="ui-button" onClick={() => createFromPreset(p.key)}>Create</button>
              </div>
            ))}
          </div>
        </section>

        <section className="workspace-panel">
          <div className="lab-panel-head">
            <div>
              <h2 className="section-title">Notebook history</h2>
              <p className="muted">Versions remain immutable once created.</p>
            </div>
          </div>
          <div className="lab-card-list">
            {versions.map(v => (
              <div key={v.id} className="lab-card">
                <div className="lab-card-main">
                  <strong>{v.active ? 'Active · ' : ''}{v.id.slice(0, 8)}</strong>
                  <p className="lab-meta">{v.chunkSize}/{v.chunkOverlap} · {v.embeddingModel} · {v.defaultSearchMode} top {v.defaultTopK}</p>
                </div>
                {!v.active && <button className="ui-button ui-button-ghost" onClick={() => activate(v.id)}>Activate</button>}
              </div>
            ))}
          </div>
        </section>
      </div>
    </div>
  )
}

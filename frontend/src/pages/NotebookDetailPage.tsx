import { useEffect, useState, useCallback } from 'react'
import type { FormEvent } from 'react'
import { useParams, Link } from 'react-router-dom'
import { apiGet, apiPost, apiUpload, apiDelete } from '../lib/api'
import { useAuthContext } from '../contexts/AuthContext'
import ChatPanel from '../components/ChatPanel'
import NotebookSourcesPanel from '../components/NotebookSourcesPanel'
import NotebookNotesPanel from '../components/NotebookNotesPanel'
import NotebookRetrievalPanel from '../components/NotebookRetrievalPanel'

interface Source { id: string; title: string; mimeType: string; status: string }
interface Note { id: string; title?: string; noteType: string; createdAt: string }
interface NotebookDetail {
  id: string; name: string; description?: string
  sources: Source[]; notes: Note[]
}

type WorkspaceTab = 'overview' | 'sources' | 'notes' | 'retrieval' | 'chat'

const tabs: { id: WorkspaceTab; label: string; hint: string }[] = [
  { id: 'overview', label: 'Overview', hint: 'Notebook summary' },
  { id: 'sources', label: 'Sources', hint: 'Files and ingest status' },
  { id: 'notes', label: 'Notes', hint: 'Write and review' },
  { id: 'retrieval', label: 'Retrieval', hint: 'Search and experiments' },
  { id: 'chat', label: 'Chat', hint: 'Ask with context' },
]

export default function NotebookDetailPage() {
  const { id } = useParams<{ id: string }>()
  const { accessToken } = useAuthContext()
  const getToken = useCallback(() => accessToken, [accessToken])
  const [nb, setNb] = useState<NotebookDetail | null>(null)
  const [noteContent, setNoteContent] = useState('')
  const [noteTitle, setNoteTitle] = useState('')
  const [activeTab, setActiveTab] = useState<WorkspaceTab>('overview')
  const [error, setError] = useState<string | null>(null)

  const reload = () => apiGet<NotebookDetail>(`/api/notebooks/${id}`).then(setNb)

  useEffect(() => { reload() }, [id])

  async function uploadFile(file: File) {
    try {
      await apiUpload(`/api/notebooks/${id}/sources`, file)
      await reload()
      setError(null)
    } catch (err) {
      console.error('Upload failed', err)
      setError('Upload failed. Check the console for details.')
    }
  }

  async function createNote(e: FormEvent) {
    e.preventDefault()
    try {
      await apiPost(`/api/notebooks/${id}/notes`, { title: noteTitle || null, content: noteContent })
      setNoteContent('')
      setNoteTitle('')
      await reload()
      setError(null)
    } catch (err) {
      console.error('Save note failed', err)
      setError('Failed to save note.')
    }
  }

  async function deleteSource(sourceId: string) {
    try {
      await apiDelete(`/api/notebooks/${id}/sources/${sourceId}`)
      await reload()
      setError(null)
    } catch (err) {
      console.error('Delete failed', err)
      setError('Failed to delete source.')
    }
  }

  if (!nb) return <div className="text-sm text-stone-400">Loading...</div>

  return (
    <div className="space-y-5">
      <header className="flex flex-col gap-3 border-b border-stone-200 pb-5">
        <Link to="/notebooks" className="w-fit text-sm text-stone-500 hover:text-stone-900">
          Back to notebooks
        </Link>
        <div className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
          <div>
            <p className="eyebrow">Notebook</p>
            <h1 className="page-title">{nb.name}</h1>
            {nb.description && <p className="mt-2 max-w-2xl text-sm text-stone-500">{nb.description}</p>}
          </div>
          <div className="flex gap-2 text-xs text-stone-500">
            <span className="rounded-full border border-stone-200 bg-white px-3 py-1">{nb.sources.length} sources</span>
            <span className="rounded-full border border-stone-200 bg-white px-3 py-1">{nb.notes.length} notes</span>
          </div>
        </div>
        {error && <p className="rounded-md border border-red-100 bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      </header>

      <div className="grid gap-5 lg:grid-cols-[16rem_minmax(0,1fr)]">
        <aside className="lg:sticky lg:top-8 lg:self-start">
          <nav className="flex gap-2 overflow-x-auto rounded-lg border border-stone-200 bg-white p-2 shadow-sm lg:flex-col lg:overflow-visible">
            {tabs.map(tab => (
              <button
                key={tab.id}
                type="button"
                onClick={() => setActiveTab(tab.id)}
                className={`workspace-tab ${activeTab === tab.id ? 'workspace-tab-active' : ''}`}
              >
                <span>{tab.label}</span>
                <small>{tab.hint}</small>
              </button>
            ))}
          </nav>
        </aside>

        <div className="min-w-0">
          {activeTab === 'overview' && (
            <section className="workspace-panel">
              <div className="workspace-panel-header">
                <div>
                  <p className="eyebrow">Overview</p>
                  <h2 className="section-title">Workspace at a glance</h2>
                </div>
              </div>
              <div className="grid gap-3 sm:grid-cols-3">
                <div className="metric-tile">
                  <span>Sources</span>
                  <strong>{nb.sources.length}</strong>
                </div>
                <div className="metric-tile">
                  <span>Notes</span>
                  <strong>{nb.notes.length}</strong>
                </div>
                <div className="metric-tile">
                  <span>Ready files</span>
                  <strong>{nb.sources.filter(s => s.status === 'ready').length}</strong>
                </div>
              </div>
              <div className="mt-5 grid gap-3 lg:grid-cols-3">
                <button
                  type="button"
                  onClick={() => setActiveTab('sources')}
                  className="quick-action"
                >
                  <span>Prepare knowledge</span>
                  <strong>Upload and review sources</strong>
                </button>
                <button
                  type="button"
                  onClick={() => setActiveTab('retrieval')}
                  className="quick-action"
                >
                  <span>Inspect retrieval</span>
                  <strong>Search chunks or run experiments</strong>
                </button>
                <button
                  type="button"
                  onClick={() => setActiveTab('chat')}
                  className="quick-action"
                >
                  <span>Ask questions</span>
                  <strong>Start a notebook chat</strong>
                </button>
              </div>
            </section>
          )}

          {activeTab === 'sources' && (
            <NotebookSourcesPanel sources={nb.sources} onUpload={uploadFile} onDelete={deleteSource} />
          )}

          {activeTab === 'notes' && (
            <NotebookNotesPanel
              notes={nb.notes}
              title={noteTitle}
              content={noteContent}
              onTitleChange={setNoteTitle}
              onContentChange={setNoteContent}
              onSubmit={createNote}
            />
          )}

          {activeTab === 'retrieval' && <NotebookRetrievalPanel notebookId={nb.id} />}

          {activeTab === 'chat' && (
            <section className="workspace-panel workspace-panel-flush">
              <ChatPanel notebookId={nb.id} getToken={getToken} />
            </section>
          )}
        </div>
      </div>
    </div>
  )
}

import { useRef } from 'react'
import type { ChangeEvent } from 'react'

interface Source {
  id: string
  title: string
  mimeType: string
  status: string
  ingestionJob?: {
    id: string
    status: string
    attemptCount: number
    maxAttempts: number
    lastError?: string | null
    updatedAt: string
  } | null
}

interface Props {
  sources: Source[]
  onUpload: (file: File) => Promise<void>
  onDelete: (sourceId: string) => Promise<void>
}

export default function NotebookSourcesPanel({ sources, onUpload, onDelete }: Props) {
  const fileRef = useRef<HTMLInputElement>(null)

  async function uploadFile(e: ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return
    await onUpload(file)
    if (fileRef.current) fileRef.current.value = ''
  }

  function confirmDelete(sourceId: string, title: string) {
    if (window.confirm(`Delete "${title}"? This will remove the file and its indexed content.`)) {
      void onDelete(sourceId)
    }
  }

  return (
    <section className="workspace-panel">
      <div className="workspace-panel-header">
        <div>
          <p className="eyebrow">Sources</p>
          <h2 className="section-title">Notebook files</h2>
        </div>
        <input ref={fileRef} type="file" className="hidden" onChange={uploadFile} />
        <button type="button" onClick={() => fileRef.current?.click()} className="ui-button ui-button-primary">
          Upload
        </button>
      </div>

      {sources.length === 0 ? (
        <div className="empty-state">No sources yet.</div>
      ) : (
        <ul className="divide-y divide-stone-100">
          {sources.map(source => (
            <li key={source.id} className="flex items-center justify-between gap-4 py-3">
              <div className="min-w-0">
                <p className="truncate text-sm font-medium text-stone-800">{source.title}</p>
                <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-stone-400">
                  <span>{source.mimeType}</span>
                  <span className={statusClass(source.ingestionJob?.status ?? source.status)}>
                    {statusLabel(source.ingestionJob?.status ?? source.status)}
                  </span>
                  {source.ingestionJob && (
                    <span>
                      attempt {source.ingestionJob.attemptCount}/{source.ingestionJob.maxAttempts}
                    </span>
                  )}
                </div>
                {source.ingestionJob?.lastError && (
                  <p className="mt-1 text-xs text-red-600">{source.ingestionJob.lastError}</p>
                )}
              </div>
              <button
                type="button"
                onClick={() => confirmDelete(source.id, source.title)}
                className="ui-button ui-button-danger shrink-0 text-xs"
              >
                Delete
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function statusLabel(status: string) {
  switch (status) {
    case 'queued':
      return 'Queued'
    case 'running':
      return 'Running'
    case 'retrying':
      return 'Retrying'
    case 'succeeded':
    case 'ingested':
      return 'Ready'
    case 'failed':
      return 'Failed'
    case 'cancelled':
      return 'Cancelled'
    default:
      return status
  }
}

function statusClass(status: string) {
  const base = 'rounded-full border px-2 py-0.5 font-medium'
  switch (status) {
    case 'queued':
      return `${base} border-stone-200 bg-stone-50 text-stone-600`
    case 'running':
    case 'retrying':
      return `${base} border-amber-200 bg-amber-50 text-amber-700`
    case 'succeeded':
    case 'ingested':
      return `${base} border-emerald-200 bg-emerald-50 text-emerald-700`
    case 'failed':
    case 'cancelled':
      return `${base} border-red-200 bg-red-50 text-red-700`
    default:
      return `${base} border-stone-200 bg-white text-stone-500`
  }
}

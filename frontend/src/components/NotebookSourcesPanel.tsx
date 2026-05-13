import { useRef } from 'react'
import type { ChangeEvent } from 'react'

interface Source {
  id: string
  title: string
  mimeType: string
  status: string
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
                <p className="mt-0.5 text-xs text-stone-400">{source.mimeType} / {source.status}</p>
              </div>
              <button
                type="button"
                onClick={() => onDelete(source.id)}
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

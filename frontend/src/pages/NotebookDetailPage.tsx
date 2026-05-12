import { useEffect, useState, useRef } from 'react'
import { useParams, Link } from 'react-router-dom'
import { apiGet, apiPost, apiUpload, apiDelete } from '../lib/api'

interface Source { id: string; title: string; mimeType: string; status: string }
interface Note { id: string; title?: string; noteType: string; createdAt: string }
interface NotebookDetail {
  id: string; name: string; description?: string
  sources: Source[]; notes: Note[]
}

export default function NotebookDetailPage() {
  const { id } = useParams<{ id: string }>()
  const [nb, setNb] = useState<NotebookDetail | null>(null)
  const [noteContent, setNoteContent] = useState('')
  const [noteTitle, setNoteTitle] = useState('')
  const fileRef = useRef<HTMLInputElement>(null)

  const reload = () => apiGet<NotebookDetail>(`/api/notebooks/${id}`).then(setNb)

  useEffect(() => { reload() }, [id])

  async function uploadFile(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return
    await apiUpload(`/api/notebooks/${id}/sources`, file)
    reload()
    if (fileRef.current) fileRef.current.value = ''
  }

  async function createNote(e: React.FormEvent) {
    e.preventDefault()
    await apiPost(`/api/notebooks/${id}/notes`, { title: noteTitle || null, content: noteContent })
    setNoteContent('')
    setNoteTitle('')
    reload()
  }

  async function deleteSource(sourceId: string) {
    await apiDelete(`/api/notebooks/${id}/sources/${sourceId}`)
    reload()
  }

  if (!nb) return <div className="p-6 text-gray-400">Loading…</div>

  return (
    <div className="max-w-3xl mx-auto p-6 space-y-8">
      <div className="flex items-center gap-4">
        <Link to="/notebooks" className="text-blue-600 hover:underline text-sm">← Notebooks</Link>
        <h1 className="text-2xl font-bold">{nb.name}</h1>
      </div>

      <section>
        <h2 className="text-lg font-semibold mb-2">Sources</h2>
        <input ref={fileRef} type="file" className="hidden" onChange={uploadFile} />
        <button
          onClick={() => fileRef.current?.click()}
          className="mb-3 px-3 py-1.5 bg-green-600 text-white rounded-lg text-sm hover:bg-green-700"
        >
          Upload File
        </button>
        {nb.sources.length === 0 && <p className="text-sm text-gray-400">No sources yet.</p>}
        <ul className="space-y-1">
          {nb.sources.map(s => (
            <li key={s.id} className="flex items-center justify-between border rounded px-3 py-2">
              <span className="text-sm truncate">{s.title} <span className="text-gray-400">({s.status})</span></span>
              <button onClick={() => deleteSource(s.id)} className="ml-2 text-red-500 text-xs hover:underline shrink-0">Delete</button>
            </li>
          ))}
        </ul>
      </section>

      <section>
        <h2 className="text-lg font-semibold mb-2">Add Note</h2>
        <form onSubmit={createNote} className="space-y-2">
          <input
            value={noteTitle}
            onChange={e => setNoteTitle(e.target.value)}
            placeholder="Title (optional)"
            className="w-full border rounded px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
          <textarea
            value={noteContent}
            onChange={e => setNoteContent(e.target.value)}
            placeholder="Write a note… (Markdown supported)"
            required
            rows={5}
            className="w-full border rounded px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 font-mono"
          />
          <button type="submit" className="px-4 py-2 bg-blue-600 text-white rounded-lg text-sm hover:bg-blue-700">
            Save Note
          </button>
        </form>
      </section>

      <section>
        <h2 className="text-lg font-semibold mb-2">Notes ({nb.notes.length})</h2>
        {nb.notes.length === 0 && <p className="text-sm text-gray-400">No notes yet.</p>}
        <ul className="space-y-1">
          {nb.notes.map(n => (
            <li key={n.id} className="border rounded px-3 py-2 text-sm">
              {n.title ?? <span className="text-gray-400">(untitled)</span>}
              <span className="ml-2 text-xs text-gray-400">{n.noteType}</span>
            </li>
          ))}
        </ul>
      </section>
    </div>
  )
}

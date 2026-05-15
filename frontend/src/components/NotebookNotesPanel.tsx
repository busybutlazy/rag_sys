import type { FormEvent } from 'react'

interface Note {
  id: string
  title?: string
  noteType: string
  createdAt: string
}

interface Props {
  notes: Note[]
  title: string
  content: string
  onTitleChange: (value: string) => void
  onContentChange: (value: string) => void
  onSubmit: (e: FormEvent) => Promise<void>
  onDelete: (noteId: string) => Promise<void>
}

export default function NotebookNotesPanel({
  notes,
  title,
  content,
  onTitleChange,
  onContentChange,
  onSubmit,
  onDelete,
}: Props) {
  return (
    <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_20rem]">
      <section className="workspace-panel">
        <div className="workspace-panel-header">
          <div>
            <p className="eyebrow">Notes</p>
            <h2 className="section-title">New note</h2>
          </div>
        </div>
        <form onSubmit={onSubmit} className="space-y-3">
          <input
            value={title}
            onChange={e => onTitleChange(e.target.value)}
            placeholder="Title"
            className="ui-input"
          />
          <textarea
            value={content}
            onChange={e => onContentChange(e.target.value)}
            placeholder="Write a note..."
            required
            rows={12}
            className="ui-input resize-y font-mono leading-relaxed"
          />
          <button type="submit" className="ui-button ui-button-primary">
            Save note
          </button>
        </form>
      </section>

      <section className="workspace-panel">
        <div className="workspace-panel-header">
          <div>
            <p className="eyebrow">Library</p>
            <h2 className="section-title">{notes.length} notes</h2>
          </div>
        </div>
        {notes.length === 0 ? (
          <div className="empty-state">No notes yet.</div>
        ) : (
          <ul className="space-y-2">
            {notes.map(note => (
              <li key={note.id} className="rounded-md border border-stone-200 bg-white px-3 py-2">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <p className="truncate text-sm font-medium text-stone-800">{note.title || 'Untitled'}</p>
                    <p className="mt-1 text-xs text-stone-400">{note.noteType}</p>
                  </div>
                  <button
                    type="button"
                    onClick={() => void onDelete(note.id)}
                    className="text-xs text-stone-400 transition hover:text-red-600"
                  >
                    Delete
                  </button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}

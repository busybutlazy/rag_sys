import { useState } from 'react'
import type { FormEvent } from 'react'
import { apiGet, apiPost, apiPut, apiDelete } from '../lib/api'

interface Note {
  id: string
  title?: string
  noteType: string
  createdAt: string
}

interface FullNote {
  id: string
  title?: string | null
  content: string
  noteType: string
}

interface Props {
  notebookId: string
  notes: Note[]
  onNotesChanged: () => void
}

export default function NotebookNotesPanel({ notebookId, notes, onNotesChanged }: Props) {
  const [title, setTitle] = useState('')
  const [content, setContent] = useState('')
  const [editingNoteId, setEditingNoteId] = useState<string | null>(null)
  const [libraryOpen, setLibraryOpen] = useState(true)
  const [loadingNote, setLoadingNote] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const isEditing = editingNoteId !== null

  async function handleSelectNote(noteId: string) {
    if (editingNoteId === noteId) return
    setLoadingNote(true)
    setError(null)
    try {
      const note = await apiGet<FullNote>(`/api/notebooks/${notebookId}/notes/${noteId}`)
      setTitle(note.title ?? '')
      setContent(note.content)
      setEditingNoteId(note.id)
    } catch {
      setError('Failed to load note.')
    } finally {
      setLoadingNote(false)
    }
  }

  function handleNewNote() {
    setEditingNoteId(null)
    setTitle('')
    setContent('')
    setError(null)
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    try {
      if (isEditing) {
        await apiPut(`/api/notebooks/${notebookId}/notes/${editingNoteId}`, {
          title: title.trim() || null,
          content,
        })
      } else {
        await apiPost(`/api/notebooks/${notebookId}/notes`, {
          title: title.trim() || null,
          content,
        })
        setTitle('')
        setContent('')
      }
      onNotesChanged()
    } catch {
      setError(isEditing ? 'Failed to update note.' : 'Failed to save note.')
    }
  }

  async function handleDelete(noteId: string, noteTitle?: string) {
    if (!window.confirm(`Delete "${noteTitle || 'Untitled'}"? This cannot be undone.`)) return
    setError(null)
    try {
      await apiDelete(`/api/notebooks/${notebookId}/notes/${noteId}`)
      if (editingNoteId === noteId) handleNewNote()
      onNotesChanged()
    } catch {
      setError('Failed to delete note.')
    }
  }

  return (
    <div
      className="overflow-hidden rounded-lg bg-paper"
      style={{
        display: 'grid',
        gridTemplateColumns: libraryOpen ? '14rem minmax(0,1fr)' : '2.5rem minmax(0,1fr)',
        transition: 'grid-template-columns 0.24s ease',
        minHeight: '36rem',
        border: '1px solid var(--ink-rule)',
      }}
    >
      {/* ── Library sidebar ─────────────────────────── */}
      <aside
        style={{
          borderRight: '1px solid var(--ink-rule)',
          background: '#F7F9FB',
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column',
        }}
      >
        {libraryOpen ? (
          <>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '0.75rem 0.75rem 0.5rem', flexShrink: 0 }}>
              <div>
                <p className="eyebrow">Notes</p>
                <h2 style={{ fontSize: '0.8rem', fontWeight: 500, color: 'var(--ink)', marginTop: '0.1rem' }}>Library</h2>
              </div>
              <div style={{ display: 'flex', gap: '0.35rem', alignItems: 'center' }}>
                <button
                  type="button"
                  onClick={handleNewNote}
                  className="ui-button ui-button-secondary"
                  style={{ height: '1.8rem', padding: '0 0.6rem', fontSize: '0.72rem' }}
                >
                  New
                </button>
                <button
                  type="button"
                  onClick={() => setLibraryOpen(false)}
                  title="Collapse library"
                  style={{
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                    width: '1.6rem', height: '1.6rem', border: 'none',
                    borderRadius: '0.3rem', background: 'transparent',
                    cursor: 'pointer', color: 'var(--ink-soft)', fontSize: '0.75rem',
                    transition: 'color 0.18s',
                  }}
                >‹</button>
              </div>
            </div>

            <div style={{ flex: 1, overflowY: 'auto', padding: '0 0.5rem 0.75rem' }}>
              {notes.length === 0 ? (
                <div className="empty-state">No notes yet.</div>
              ) : (
                notes.map(note => (
                  <div
                    key={note.id}
                    style={{
                      display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between',
                      gap: '0.25rem', marginBottom: '0.2rem',
                      padding: '0.5rem 0.6rem', borderRadius: '0.4rem',
                      background: editingNoteId === note.id ? 'var(--paper)' : 'transparent',
                      boxShadow: editingNoteId === note.id ? '0 1px 3px rgba(0,0,0,0.07)' : 'none',
                      transition: 'background 0.15s',
                    }}
                  >
                    <button
                      type="button"
                      onClick={() => handleSelectNote(note.id)}
                      style={{
                        flex: 1, minWidth: 0, textAlign: 'left',
                        border: 'none', background: 'transparent', cursor: 'pointer', padding: 0,
                      }}
                    >
                      <p style={{ fontSize: '0.8rem', fontWeight: 500, color: 'var(--ink)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                        {note.title || 'Untitled'}
                      </p>
                      <p style={{ fontSize: '0.68rem', color: 'var(--ink-soft)', marginTop: '0.15rem' }}>{note.noteType}</p>
                    </button>
                    <button
                      type="button"
                      onClick={() => handleDelete(note.id, note.title)}
                      style={{
                        flexShrink: 0, border: 'none', background: 'transparent',
                        cursor: 'pointer', fontSize: '0.68rem', color: 'var(--ink-soft)',
                        padding: '0.1rem 0', transition: 'color 0.18s',
                      }}
                      onMouseEnter={e => { e.currentTarget.style.color = '#dc2626' }}
                      onMouseLeave={e => { e.currentTarget.style.color = 'var(--ink-soft)' }}
                    >
                      Delete
                    </button>
                  </div>
                ))
              )}
            </div>
          </>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', paddingTop: '0.75rem', gap: '0.5rem' }}>
            <button
              type="button"
              onClick={() => setLibraryOpen(true)}
              title="Expand library"
              style={{
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                width: '1.8rem', height: '1.8rem', border: '1px solid var(--ink-rule)',
                borderRadius: '0.3rem', background: 'transparent',
                cursor: 'pointer', color: 'var(--ink-soft)', fontSize: '0.75rem',
                transition: 'color 0.18s',
              }}
            >›</button>
            <div style={{
              writingMode: 'vertical-rl', textOrientation: 'mixed',
              fontSize: '0.6rem', fontWeight: 500, letterSpacing: '0.12em',
              textTransform: 'uppercase', color: 'var(--ink-soft)',
              transform: 'rotate(180deg)', marginTop: '0.25rem',
              whiteSpace: 'nowrap', overflow: 'hidden', maxHeight: '7rem',
              textOverflow: 'ellipsis',
            }}>
              {isEditing
                ? notes.find(n => n.id === editingNoteId)?.title || 'Untitled'
                : 'Library'}
            </div>
          </div>
        )}
      </aside>

      {/* ── Editor area ──────────────────────────────── */}
      <div style={{ display: 'flex', flexDirection: 'column', minWidth: 0 }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '0.75rem', borderBottom: '1px solid var(--ink-rule)', padding: '0.75rem 1.25rem' }}>
          <div>
            <p className="eyebrow">Editor</p>
            <p style={{ fontSize: '0.8rem', fontWeight: 500, color: 'var(--ink)', marginTop: '0.1rem' }}>
              {isEditing
                ? notes.find(n => n.id === editingNoteId)?.title || 'Untitled'
                : 'New note'}
            </p>
          </div>
          {isEditing && (
            <button type="button" onClick={handleNewNote} className="ui-button ui-button-ghost text-xs">
              Cancel edit
            </button>
          )}
        </div>

        <div style={{ flex: 1, padding: '1.25rem', overflowY: 'auto' }}>
          {error && (
            <p style={{ marginBottom: '0.75rem', padding: '0.5rem 0.75rem', borderRadius: '0.4rem', border: '1px solid #fecaca', background: '#fff5f5', fontSize: '0.8rem', color: '#dc2626' }}>
              {error}
            </p>
          )}

          {loadingNote ? (
            <p style={{ fontSize: '0.85rem', color: 'var(--ink-soft)' }}>Loading note…</p>
          ) : (
            <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem', height: '100%' }}>
              <input
                value={title}
                onChange={e => setTitle(e.target.value)}
                placeholder="Title"
                className="ui-input"
              />
              <textarea
                value={content}
                onChange={e => setContent(e.target.value)}
                placeholder="Write a note..."
                required
                className="ui-input font-mono leading-relaxed"
                style={{ resize: 'vertical', flex: 1, minHeight: '22rem' }}
              />
              <div>
                <button type="submit" className="ui-button ui-button-primary">
                  {isEditing ? 'Update note' : 'Save note'}
                </button>
              </div>
            </form>
          )}
        </div>
      </div>
    </div>
  )
}

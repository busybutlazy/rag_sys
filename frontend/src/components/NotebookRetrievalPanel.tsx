import { useState } from 'react'
import SearchPanel from './SearchPanel'
import ExperimentPanel from './ExperimentPanel'

interface Props {
  notebookId: string
}

type RetrievalMode = 'search' | 'experiments'

const modes: { id: RetrievalMode; label: string; hint: string }[] = [
  { id: 'search', label: 'Search', hint: 'Inspect matching chunks' },
  { id: 'experiments', label: 'Experiments', hint: 'Compare retrieval modes' },
]

export default function NotebookRetrievalPanel({ notebookId }: Props) {
  const [activeMode, setActiveMode] = useState<RetrievalMode>('search')

  return (
    <section className="workspace-panel">
      <div className="workspace-panel-header">
        <div>
          <p className="eyebrow">Retrieval</p>
          <h2 className="section-title">Search and experiments</h2>
        </div>
        <div className="inline-flex rounded-md border border-stone-200 bg-stone-50 p-1">
          {modes.map(mode => (
            <button
              key={mode.id}
              type="button"
              onClick={() => setActiveMode(mode.id)}
              className={`rounded px-3 py-1.5 text-sm font-medium transition ${
                activeMode === mode.id ? 'bg-white text-stone-900 shadow-sm' : 'text-stone-500 hover:text-stone-800'
              }`}
              title={mode.hint}
            >
              {mode.label}
            </button>
          ))}
        </div>
      </div>

      {activeMode === 'search' ? (
        <SearchPanel notebookId={notebookId} />
      ) : (
        <ExperimentPanel notebookId={notebookId} />
      )}
    </section>
  )
}

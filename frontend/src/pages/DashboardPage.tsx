import { useNavigate } from 'react-router-dom'
import { useAuth } from '../hooks/useAuth'

export default function DashboardPage() {
  const { username } = useAuth()
  const navigate = useNavigate()
  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <section className="border-b border-stone-200 pb-6">
        <p className="eyebrow">Home</p>
        <h1 className="page-title">Knowledge Desk</h1>
        <p className="mt-3 max-w-2xl text-sm leading-6 text-stone-500">
          Signed in as <span className="font-medium text-stone-800">{username}</span>.
        </p>
      </section>

      <section className="workspace-panel">
        <div className="workspace-panel-header">
          <div>
            <p className="eyebrow">Start</p>
            <h2 className="section-title">Open your notebooks</h2>
          </div>
        </div>
        <p className="max-w-xl text-sm leading-6 text-stone-500">
          Manage sources, notes, retrieval experiments, and chat from a cleaner workspace.
        </p>
        <button
          onClick={() => navigate('/notebooks')}
          className="ui-button ui-button-primary mt-5"
        >
          Open notebooks
        </button>
      </section>
    </div>
  )
}

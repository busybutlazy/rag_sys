import { useNavigate } from 'react-router-dom'
import { useAuth } from '../hooks/useAuth'

export default function DashboardPage() {
  const { username, logout } = useAuth()
  const navigate = useNavigate()
  return (
    <div className="min-h-screen bg-gray-50 flex flex-col items-center justify-center gap-4">
      <h1 className="text-3xl font-bold">RAG System</h1>
      <p className="text-gray-600">
        Logged in as <span className="font-semibold">{username}</span>
      </p>
      <div className="flex gap-3">
        <button
          onClick={() => navigate('/notebooks')}
          className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition"
        >
          My Notebooks
        </button>
        <button
          onClick={logout}
          className="px-4 py-2 bg-red-500 text-white rounded-lg hover:bg-red-600 transition"
        >
          Sign Out
        </button>
      </div>
    </div>
  )
}

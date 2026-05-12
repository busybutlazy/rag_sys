import { useAuth } from '../hooks/useAuth'

export default function DashboardPage() {
  const { username, logout } = useAuth()
  return (
    <div className="min-h-screen bg-gray-50 flex flex-col items-center justify-center gap-4">
      <h1 className="text-3xl font-bold">RAG System</h1>
      <p className="text-gray-600">
        Logged in as <span className="font-semibold">{username}</span>
      </p>
      <button
        onClick={logout}
        className="px-4 py-2 bg-red-500 text-white rounded-lg hover:bg-red-600 transition"
      >
        Sign Out
      </button>
    </div>
  )
}

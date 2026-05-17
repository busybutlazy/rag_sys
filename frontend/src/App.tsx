import { Routes, Route, Navigate } from 'react-router-dom'
import { useEffect, useState } from 'react'
import { useAuth } from './hooks/useAuth'
import { useAuthContext } from './contexts/AuthContext'
import { registerTokenGetter } from './lib/api'
import AppShell from './components/AppShell'
import ProtectedRoute from './components/ProtectedRoute'
import LoginPage from './pages/LoginPage'
import DashboardPage from './pages/DashboardPage'
import NotebooksPage from './pages/NotebooksPage'
import NotebookDetailPage from './pages/NotebookDetailPage'
import LabRetrievalVersionsPage from './pages/LabRetrievalVersionsPage'
import LabReindexPage from './pages/LabReindexPage'
import LabRetrievalBenchPage from './pages/LabRetrievalBenchPage'

function ApiTokenBridge() {
  const { accessToken } = useAuthContext()
  useEffect(() => { registerTokenGetter(() => accessToken) }, [accessToken])
  return null
}

function AppRoutes() {
  const { refresh, accessToken } = useAuth()
  const [checked, setChecked] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    const id = setTimeout(() => controller.abort(), 5000)
    refresh().finally(() => { clearTimeout(id); setChecked(true) })
  }, [refresh])

  if (!checked) return null

  return (
    <Routes>
      <Route path="/login" element={accessToken ? <Navigate to="/dashboard" replace /> : <LoginPage />} />
      <Route path="/dashboard" element={<ProtectedRoute><AppShell><DashboardPage /></AppShell></ProtectedRoute>} />
      <Route path="/notebooks" element={<ProtectedRoute><AppShell><NotebooksPage /></AppShell></ProtectedRoute>} />
      <Route path="/notebooks/:id" element={<ProtectedRoute><AppShell><NotebookDetailPage /></AppShell></ProtectedRoute>} />
      <Route path="/lab/retrieval-versions" element={<ProtectedRoute><AppShell><LabRetrievalVersionsPage /></AppShell></ProtectedRoute>} />
      <Route path="/lab/reindex" element={<ProtectedRoute><AppShell><LabReindexPage /></AppShell></ProtectedRoute>} />
      <Route path="/lab/retrieval-bench" element={<ProtectedRoute><AppShell><LabRetrievalBenchPage /></AppShell></ProtectedRoute>} />
      <Route path="*" element={<Navigate to={accessToken ? '/dashboard' : '/login'} replace />} />
    </Routes>
  )
}

export default function App() {
  return (
    <>
      <ApiTokenBridge />
      <AppRoutes />
    </>
  )
}

import { Routes, Route, Navigate } from 'react-router-dom'
import { useEffect, useState } from 'react'
import { useAuth } from './hooks/useAuth'
import ProtectedRoute from './components/ProtectedRoute'
import LoginPage from './pages/LoginPage'
import DashboardPage from './pages/DashboardPage'

function AppRoutes() {
  const { refresh, accessToken } = useAuth()
  const [checked, setChecked] = useState(false)

  useEffect(() => {
    // Silent refresh — falls through to login if cookie is absent or server returns non-200
    // Note: /api/auth/refresh returns 501 until Phase 2 adds the refresh_tokens table;
    // users must log in manually on each session until then.
    const controller = new AbortController()
    const id = setTimeout(() => controller.abort(), 5000)
    refresh().finally(() => { clearTimeout(id); setChecked(true) })
  }, [refresh])

  if (!checked) return null // prevent flash of login page while checking session

  return (
    <Routes>
      <Route path="/login" element={accessToken ? <Navigate to="/dashboard" replace /> : <LoginPage />} />
      <Route path="/dashboard" element={<ProtectedRoute><DashboardPage /></ProtectedRoute>} />
      <Route path="*" element={<Navigate to={accessToken ? '/dashboard' : '/login'} replace />} />
    </Routes>
  )
}

export default function App() {
  return <AppRoutes />
}

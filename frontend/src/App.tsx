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
    // Attempt silent token refresh on app load using the httpOnly cookie
    refresh().finally(() => setChecked(true))
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

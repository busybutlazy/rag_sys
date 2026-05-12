import { Navigate } from 'react-router-dom'
import { useAuth } from '../hooks/useAuth'

export default function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { accessToken } = useAuth()
  if (!accessToken) return <Navigate to="/login" replace />
  return <>{children}</>
}

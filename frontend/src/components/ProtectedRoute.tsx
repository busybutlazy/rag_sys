import { Navigate } from 'react-router-dom'
import type { ReactNode } from 'react'
import { useAuth } from '../hooks/useAuth'

export default function ProtectedRoute({ children }: { children: ReactNode }) {
  const { accessToken } = useAuth()
  if (!accessToken) return <Navigate to="/login" replace />
  return <>{children}</>
}

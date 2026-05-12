import { createContext, useContext, useState, useCallback, ReactNode } from 'react'

interface AuthState {
  accessToken: string | null
  username: string | null
}

interface AuthContextValue extends AuthState {
  login: (token: string, username: string) => void
  logout: () => Promise<void>
  refresh: () => Promise<boolean>
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [auth, setAuth] = useState<AuthState>({ accessToken: null, username: null })

  const login = useCallback((token: string, username: string) => {
    setAuth({ accessToken: token, username })
  }, [])

  const logout = useCallback(async () => {
    await fetch('/api/auth/logout', { method: 'POST', credentials: 'include' })
    setAuth({ accessToken: null, username: null })
  }, [])

  const refresh = useCallback(async (): Promise<boolean> => {
    try {
      const res = await fetch('/api/auth/refresh', { method: 'POST', credentials: 'include' })
      if (!res.ok) return false
      const data = await res.json()
      const payload = JSON.parse(atob(data.accessToken.split('.')[1]))
      setAuth({ accessToken: data.accessToken, username: payload.unique_name })
      return true
    } catch {
      return false
    }
  }, [])

  return (
    <AuthContext.Provider value={{ ...auth, login, logout, refresh }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuthContext() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuthContext must be used inside AuthProvider')
  return ctx
}

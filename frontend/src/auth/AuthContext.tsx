import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { apiFetch } from './apiClient'
import { ApiError, NetworkError } from '../models/ApiErrors'
import type { AuthUser } from '../models/AuthUser'
import { getCurrentUser, login as loginRequest, logout as logoutRequest } from './authApi'
import { readCsrfCookie, refreshCsrfToken } from './csrf'
import type { AuthContextValue, AuthProviderProps } from '../types/auth'

const AuthContext = createContext<AuthContextValue | null>(null)

function sessionErrorMessage(error: unknown): string {
  if (error instanceof NetworkError) {
    return 'Session check failed because the API is unavailable.'
  }
  if (error instanceof ApiError && error.status >= 500) {
    return 'Session check failed because the authentication service is unavailable.'
  }
  return 'Session check failed. Please try again.'
}

export function AuthProvider({ children }: AuthProviderProps) {
  const [user, setUser] = useState<AuthUser | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [initializationError, setInitializationError] = useState<string | null>(null)

  useEffect(() => {
    let active = true
    // US-15: get the CSRF cookies before the first form is sent, so login does not start with a 403 and a retry.
    if (!readCsrfCookie()) void refreshCsrfToken()
    void getCurrentUser()
      .then(currentUser => {
        if (active) {
          setUser(currentUser)
          setInitializationError(null)
        }
      })
      .catch(error => {
        if (active) {
          setUser(null)
          setInitializationError(sessionErrorMessage(error))
        }
      })
      .finally(() => {
        if (active) setIsLoading(false)
      })

    return () => { active = false }
  }, [])

  const handleUnauthorized = useCallback(() => setUser(null), [])

  const refreshUser = useCallback(async () => {
    const currentUser = await getCurrentUser()
    if (!currentUser) throw new Error('Login did not create an active session')
    setUser(currentUser)
    setInitializationError(null)
  }, [])

  const login = useCallback(async (email: string, password: string) => {
    await loginRequest(email, password)
    await refreshUser()
  }, [refreshUser])

  const logout = useCallback(async () => {
    await logoutRequest()
    setUser(null)
    // US-15: logout removes the CSRF cookies of the ended session; get a new pair for the next login.
    void refreshCsrfToken()
  }, [])

  const request = useCallback(
    (input: RequestInfo | URL, init?: RequestInit) => apiFetch(input, init, handleUnauthorized),
    [handleUnauthorized]
  )

  const value = useMemo<AuthContextValue>(() => ({
    user,
    isLoading,
    isAuthenticated: user !== null,
    initializationError,
    refreshUser,
    login,
    logout,
    request
  }), [user, isLoading, initializationError, refreshUser, login, logout, request])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside AuthProvider')
  return context
}

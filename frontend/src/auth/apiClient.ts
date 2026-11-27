import { ApiError, NetworkError } from '../models/ApiErrors'
import type { UnauthorizedHandler } from '../types/api'
import { csrfFetch } from './csrf'

export async function apiFetch(
  input: RequestInfo | URL,
  init: RequestInit = {},
  onUnauthorized?: UnauthorizedHandler
): Promise<Response> {
  let response: Response
  try {
    // US-15: csrfFetch adds the CSRF header to state-changing requests.
    response = await csrfFetch(input, {
      ...init,
      credentials: 'same-origin'
    })
  } catch (error) {
    throw new NetworkError(error)
  }

  if (response.status === 401) {
    onUnauthorized?.()
  }

  return response
}

export async function apiJson<T>(
  input: RequestInfo | URL,
  init: RequestInit = {},
  onUnauthorized?: UnauthorizedHandler
): Promise<T> {
  const response = await apiFetch(input, init, onUnauthorized)
  if (!response.ok) {
    throw new ApiError(response.status)
  }

  return response.json() as Promise<T>
}

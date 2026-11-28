import { useCallback, useEffect, useRef, useState } from 'react'
import { DraftsApiError } from '../../api/drafts'
import type { DraftsGateway } from '../../api/drafts'
import type { CreateDraftData, Draft } from './DraftTypes'

export type LoadState = 'loading' | 'ready' | 'error'

const genericError = 'Zahtev za nacrte nije uspeo. Pokušajte ponovo.'

function messageOf(error: unknown): string {
  return error instanceof DraftsApiError ? error.message : genericError
}

/** Stanje stranice sa nacrtima: učitavanje liste, kreiranje i ponovno učitavanje posle kreiranja. */
export function useDrafts(gateway: DraftsGateway) {
  const [drafts, setDrafts] = useState<Draft[]>([])
  const [loadState, setLoadState] = useState<LoadState>('loading')
  const [loadError, setLoadError] = useState('')
  const [creating, setCreating] = useState(false)
  const [createError, setCreateError] = useState('')
  const creatingRef = useRef(false)

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoadState('loading')
    setLoadError('')
    try {
      const list = await gateway.list(signal)
      if (signal?.aborted) return
      setDrafts(list)
      setLoadState('ready')
    } catch (error) {
      if (signal?.aborted) return
      setLoadError(messageOf(error))
      setLoadState('error')
    }
  }, [gateway])

  useEffect(() => {
    const controller = new AbortController()
    void load(controller.signal)
    return () => controller.abort()
  }, [load])

  const reload = useCallback(() => { void load() }, [load])

  const clearCreateError = useCallback(() => setCreateError(''), [])

  /** Vraća true samo kada je server prihvatio nacrt; forma tada sme da obriše polja. */
  const create = useCallback(async (data: CreateDraftData): Promise<boolean> => {
    if (creatingRef.current) return false
    creatingRef.current = true
    setCreating(true)
    setCreateError('')
    try {
      await gateway.create(data)
    } catch (error) {
      setCreateError(messageOf(error))
      creatingRef.current = false
      setCreating(false)
      return false
    }

    // Lista se ponovo učitava sa servera, ne dopunjuje se lokalno.
    try {
      setDrafts(await gateway.list())
      setLoadState('ready')
    } catch (error) {
      setLoadError(messageOf(error))
      setLoadState('error')
    }
    creatingRef.current = false
    setCreating(false)
    return true
  }, [gateway])

  return { drafts, loadState, loadError, creating, createError, create, reload, clearCreateError }
}

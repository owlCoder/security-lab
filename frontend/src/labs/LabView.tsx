import { useEffect, useState } from 'react'
import type { ApiResult } from '../api/http'
import type { LabDevUser } from './api'
import type { StudentLabView } from './types'
import LabContent from './LabContent'

type LabViewProps = {
  labId: string
  user: LabDevUser
  load: (
    labId: string,
    user: LabDevUser,
  ) => Promise<ApiResult<StudentLabView>>
}

type LoadState =
  | { status: 'loading' }
  | { status: 'success'; lab: StudentLabView }
  | { status: 'error'; message: string }

export default function LabView({ labId, user, load }: LabViewProps) {
  const [state, setState] = useState<LoadState>({ status: 'loading' })

  useEffect(() => {
    let active = true

    setState({ status: 'loading' })

    async function loadLab() {
      const result = await load(labId, user)

      if (!active) {
        return
      }

      if (result.ok) {
        setState({ status: 'success', lab: result.data })
      } else {
        setState({ status: 'error', message: result.error.message })
      }
    }

    void loadLab()

    return () => {
      active = false
    }
  }, [labId, user, load])

  if (state.status === 'loading') {
    return <p role="status">Učitavanje laboratorije…</p>
  }

  if (state.status === 'error') {
    return <p role="alert">{state.message}</p>
  }

  return <LabContent lab={state.lab} />
}
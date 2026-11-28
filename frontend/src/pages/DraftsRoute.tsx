import { useMemo } from 'react'
import { useAuth } from '../auth/AuthContext'
import { createHttpDraftsGateway, isDraftsApiEnabled } from '../api/drafts'
import { createLocalDraftsGateway } from '../api/localDraftsGateway'
import DraftsPage from './DraftsPage'

// Jedino mesto gde se bira izvor podataka: server preko zajedničkog zahteva sesije
// (kolačić, 401 odjavljuje korisnika) ili demo sa lokalnim stanjem.
export default function DraftsRoute() {
  const { request } = useAuth()

  const gateway = useMemo(
    () => (isDraftsApiEnabled() ? createHttpDraftsGateway(request) : createLocalDraftsGateway()),
    [request],
  )

  return <DraftsPage gateway={gateway} />
}

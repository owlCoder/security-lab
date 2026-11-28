import type { DraftsGateway } from './drafts'
import type { Draft } from '../components/drafts/DraftTypes'

// DEMO: nacrti postoje samo u memoriji otvorene stranice, ništa se ne šalje serveru.
// ID i status ovde dodeljuje klijent samo zato što nema servera koji bi to uradio.
export function createLocalDraftsGateway(): DraftsGateway {
  const drafts: Draft[] = []

  return {
    persistsOnServer: false,

    async list() {
      return drafts.map(draft => ({ ...draft }))
    },

    async create(data) {
      const draft: Draft = { ...data, id: crypto.randomUUID(), status: 'Draft' }
      drafts.push(draft)
      return { ...draft }
    },
  }
}

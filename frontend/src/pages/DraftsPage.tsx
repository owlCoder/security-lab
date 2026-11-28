import { useState } from 'react'
import '../components/drafts/drafts.css'
import CreateDraftForm from '../components/drafts/CreateDraftForm'
import DraftList from '../components/drafts/DraftList'
import { useDrafts } from '../components/drafts/useDrafts'
import { labTypeLabel } from '../components/drafts/DraftTypes'
import type { CreateDraftData } from '../components/drafts/DraftTypes'
import type { DraftsGateway } from '../api/drafts'

type Props = {
  gateway: DraftsGateway
}

export default function DraftsPage({ gateway }: Props) {
  const {
    drafts, loadState, loadError, creating, createError, create, reload, clearCreateError,
  } = useDrafts(gateway)
  const [showCreateForm, setShowCreateForm] = useState(false)
  const [selectedId, setSelectedId] = useState<string | null>(null)

  // Izvodi se iz liste, ne čuva se kopija koja može da zastari.
  const selectedDraft = drafts.find(draft => draft.id === selectedId) ?? null

  async function handleCreate(data: CreateDraftData): Promise<boolean> {
    const created = await create(data)
    if (created) setShowCreateForm(false)
    return created
  }

  function handleToggleForm() {
    clearCreateError()
    setShowCreateForm(current => !current)
    setSelectedId(null)
  }

  return (
    <section className="drafts">
      <div className="drafts-header">
        <div>
          <h2>Nacrti zadataka</h2>
          <p>Pregled i kreiranje laboratorijskih zadataka.</p>
        </div>

        <button type="button" onClick={handleToggleForm}>
          {showCreateForm ? 'Zatvori formu' : 'Novi nacrt'}
        </button>
      </div>

      {!gateway.persistsOnServer && (
        <p className="drafts-demo" role="status">
          <strong>Demo režim:</strong> nacrti se ne čuvaju na serveru i nestaju
          nakon osvežavanja stranice.
        </p>
      )}

      {showCreateForm && (
        <CreateDraftForm
          onCreate={handleCreate}
          submitting={creating}
          error={createError}
        />
      )}

      {selectedDraft && (
        <section
          className="drafts-selected"
          aria-labelledby="selected-draft-heading"
        >
          <h3 id="selected-draft-heading">Izabrani nacrt</h3>
          <p><strong>Naziv:</strong> {selectedDraft.title}</p>
          <p><strong>Cilj:</strong> {selectedDraft.goal || 'Nije definisan'}</p>
          <p><strong>Tip:</strong> {labTypeLabel(selectedDraft.type)}</p>
        </section>
      )}

      {loadState === 'loading' && (
        <p aria-live="polite">Učitavanje nacrta…</p>
      )}

      {loadState === 'error' && (
        <div>
          <p className="error-text" role="alert">{loadError}</p>
          <button type="button" onClick={reload}>Pokušaj ponovo</button>
        </div>
      )}

      {loadState === 'ready' && (
        <DraftList
          drafts={drafts}
          onOpen={setSelectedId}
        />
      )}
    </section>
  )
}

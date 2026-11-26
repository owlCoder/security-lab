import { useState } from 'react'
import '../components/drafts/drafts.css'
import CreateDraftForm from '../components/drafts/CreateDraftForm'
import DraftList from '../components/drafts/DraftList'
import { labTypeLabel } from '../components/drafts/DraftTypes'
import type { CreateDraftData, Draft } from '../components/drafts/DraftTypes'

// DEMO: nacrti postoje samo u stanju stranice, ništa se ne šalje serveru.
// ID i status zapravo dodeljuje server, pa ova funkcija nestaje kad se stranica poveže.
function createLocalDraft(data: CreateDraftData): Draft {
  return {
    ...data,
    id: crypto.randomUUID(),
    status: 'Draft',
  }
}

export default function DraftsPage() {
  const [drafts, setDrafts] = useState<Draft[]>([])
  const [showCreateForm, setShowCreateForm] = useState(false)
  const [selectedId, setSelectedId] = useState<string | null>(null)

  // Izvodi se iz liste, ne čuva se kopija koja može da zastari.
  const selectedDraft = drafts.find(draft => draft.id === selectedId) ?? null

  function handleCreate(data: CreateDraftData) {
    setDrafts(currentDrafts => [...currentDrafts, createLocalDraft(data)])
    setShowCreateForm(false)
  }

  function handleToggleForm() {
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

      <p className="drafts-demo" role="status">
        <strong>Demo režim:</strong> nacrti se ne čuvaju na serveru i nestaju
        nakon osvežavanja stranice.
      </p>

      {showCreateForm && (
        <CreateDraftForm onCreate={handleCreate} />
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

      <DraftList
        drafts={drafts}
        onOpen={setSelectedId}
      />
    </section>
  )
}

import { DIFFICULTY_LABELS, TYPE_LABELS, type CatalogLab, type MissingPrerequisite } from './catalogTypes'

function UnlockCondition({ missing }: { missing: MissingPrerequisite[] }) {
  if (missing.length === 0) {
    return <p className="catalog-lock" role="note">Laboratorija trenutno nije dostupna.</p>
  }
  return (
    <div className="catalog-lock" role="note">
      <p>Za otključavanje je potrebno završiti:</p>
      <ul>
        {missing.map(m => <li key={m.id}>{m.title}</li>)}
      </ul>
    </div>
  )
}

export default function LabCard({ lab }: { lab: CatalogLab }) {
  const locked = !lab.access.isUnlocked

  return (
    <article className={locked ? 'catalog-card locked' : 'catalog-card'}>
      <header className="catalog-card-head">
        <h3>{lab.title}</h3>
        <span className={locked ? 'catalog-badge locked' : 'catalog-badge open'}>
          {locked ? 'Zaključano' : 'Otključano'}
        </span>
      </header>

      <p className="catalog-objective">{lab.objective}</p>

      <dl className="catalog-meta">
        <div><dt>Tip</dt><dd>{TYPE_LABELS[lab.type]}</dd></div>
        <div><dt>Težina</dt><dd>{DIFFICULTY_LABELS[lab.difficulty]}</dd></div>
      </dl>

      {lab.tags.length > 0 && (
        <ul className="catalog-tags" aria-label="Oznake">
          {lab.tags.map(tag => <li key={tag}>{tag}</li>)}
        </ul>
      )}

      {locked && <UnlockCondition missing={lab.access.missingPrerequisites} />}
    </article>
  )
}
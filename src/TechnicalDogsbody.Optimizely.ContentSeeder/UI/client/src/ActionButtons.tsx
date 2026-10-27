import { ReactNode, useEffect, useRef, useState } from 'react'
import { OperationName, SeedDefinitionStatus } from './api'

interface Props {
  running: boolean
  hasContent: boolean
  changedDefinitions: string[]
  definitions: SeedDefinitionStatus[]
  /** The definition the actions apply to, or null for every definition. */
  scope: string | null
  onScopeChange: (definitionId: string | null) => void
  error: string | null
  onRun: (name: OperationName) => Promise<void>
}

type Confirmable = 'Reseed' | 'Delete'

const confirmCopy: Record<Confirmable, { title: string; body: string; action: string }> = {
  Reseed: {
    title: 'Reseed content?',
    body: 'The seeded pages, blocks and images will be deleted, then built again from the current seed definition. Any edits made to that seeded content will be lost.',
    action: 'Delete and reseed',
  },
  Delete: {
    title: 'Delete seeded content?',
    body: 'The pages, blocks, sections and images the seeder created will be deleted. Content created by editors isn’t touched.',
    action: 'Delete',
  },
}

// "demo-seed-v1 · 42 pages · changed" — what the picker shows for each definition.
function optionLabel(d: SeedDefinitionStatus): string {
  const seeded = d.pageCount + d.blockCount + d.sectionCount > 0
  const parts = [d.seedDefinitionId, seeded ? `${d.pageCount} pages` : 'not seeded']
  if (d.isChanged) parts.push('changed')
  if (d.hasPartialRun) parts.push('interrupted')
  return parts.join(' · ')
}

export function ActionButtons({
  running,
  hasContent,
  changedDefinitions,
  definitions,
  scope,
  onScopeChange,
  error,
  onRun,
}: Props) {
  const [starting, setStarting] = useState<OperationName | null>(null)
  const [confirm, setConfirm] = useState<Confirmable | null>(null)
  const dialogRef = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = dialogRef.current
    if (!dialog) return
    if (confirm && !dialog.open) dialog.showModal()
    if (!confirm && dialog.open) dialog.close()
  }, [confirm])

  const start = async (name: OperationName) => {
    setConfirm(null)
    setStarting(name)
    try {
      await onRun(name)
    } finally {
      setStarting(null)
    }
  }

  const disabled = running || starting !== null
  // The drift callout only concerns the definitions the actions apply to.
  const changedInScope = scope === null ? changedDefinitions : changedDefinitions.filter(id => id === scope)
  const drift = changedInScope.length > 0 && hasContent
  const scopeName = scope ?? 'every seed definition'
  const scopeNote = scope === null
    ? 'Every seed definition is affected.'
    : `Only ${scope} is affected. Other seed definitions are left alone.`

  return (
    <section className="cs-card" aria-labelledby="cs-actions-heading">
      <div className="cs-card__header">
        <h2 id="cs-actions-heading">Actions</h2>
        {running && <span className="cs-small cs-muted">Available when the current run finishes</span>}
      </div>

      <div className="cs-field">
        <label htmlFor="cs-definition">Seed definition</label>
        <select
          id="cs-definition"
          className="cs-select"
          value={scope ?? ''}
          onChange={e => onScopeChange(e.target.value === '' ? null : e.target.value)}
          disabled={running || starting !== null}
        >
          <option value="">All definitions</option>
          {definitions.map(d => (
            <option key={d.seedDefinitionId} value={d.seedDefinitionId}>{optionLabel(d)}</option>
          ))}
        </select>
        <p className="cs-small cs-muted cs-scope-note">{scopeNote}</p>
      </div>

      {drift && (
        <div className="cs-callout cs-callout--info">
          <strong>
            {changedInScope.length === 1
              ? `${changedInScope[0]} has changed since it was seeded`
              : `${changedInScope.length} seed definitions have changed since they were seeded`}
          </strong>
          <span className="cs-small">
            Seed only creates content that’s missing, so pages and sections that already exist won’t pick up the change.
            Reseed to rebuild them from the current definition.
          </span>
          <div className="cs-callout__footer">
            <span className="cs-chips">
              {changedInScope.map(id => <span key={id} className="cs-badge cs-mono">{id}</span>)}
            </span>
            <button className="cs-button cs-button--primary" onClick={() => setConfirm('Reseed')} disabled={disabled}>
              Reseed now
            </button>
          </div>
        </div>
      )}

      <div className="cs-actions">
        <ActionRow
          title="Seed"
          description={`Create anything that’s missing for ${scopeName}. Content that’s already seeded is left as it is, and an interrupted run picks up where it stopped.`}
        >
          <button className={`cs-button ${drift ? 'cs-button--secondary' : 'cs-button--primary'}`} onClick={() => start('Seed')} disabled={disabled}>
            {starting === 'Seed' ? 'Starting…' : 'Seed'}
          </button>
        </ActionRow>
        <ActionRow
          title="Reseed"
          description={`Delete the seeded content for ${scopeName} and build it again from the current seed definition. Use this after changing a definition.`}
        >
          <button className={`cs-button ${drift ? 'cs-button--primary' : 'cs-button--secondary'}`} onClick={() => setConfirm('Reseed')} disabled={disabled || !hasContent}>
            {starting === 'Reseed' ? 'Starting…' : 'Reseed'}
          </button>
        </ActionRow>
        <ActionRow
          title="Delete"
          description={`Remove the content the seeder created for ${scopeName}, including its images. Content created by editors isn’t touched.`}
        >
          <button className="cs-button cs-button--danger" onClick={() => setConfirm('Delete')} disabled={disabled || !hasContent}>
            {starting === 'Delete' ? 'Starting…' : 'Delete'}
          </button>
        </ActionRow>
      </div>

      {error && <div className="cs-callout cs-callout--danger" role="alert">{error}</div>}

      <dialog ref={dialogRef} className="cs-dialog" onClose={() => setConfirm(null)} aria-labelledby="cs-dialog-title">
        {confirm && (
          <form method="dialog" onSubmit={e => e.preventDefault()}>
            <h2 id="cs-dialog-title">{confirmCopy[confirm].title}</h2>
            <p>{confirmCopy[confirm].body}</p>
            <p className="cs-small">{scopeNote}</p>
            <p className="cs-small cs-muted">This can’t be undone.</p>
            <div className="cs-dialog__actions">
              <button type="button" className="cs-button cs-button--ghost" onClick={() => setConfirm(null)} autoFocus>
                Cancel
              </button>
              <button type="button" className="cs-button cs-button--danger-solid" onClick={() => start(confirm)}>
                {confirmCopy[confirm].action}
              </button>
            </div>
          </form>
        )}
      </dialog>
    </section>
  )
}

function ActionRow({ title, description, children }: { title: string; description: string; children: ReactNode }) {
  return (
    <div className="cs-action">
      <div>
        <strong>{title}</strong>
        <p className="cs-muted cs-small">{description}</p>
      </div>
      {children}
    </div>
  )
}

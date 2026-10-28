import { ReactNode, useEffect, useRef, useState } from 'react'
import { EditorChoice, EditorContentReport, OperationName, SeedDefinitionStatus, getEditorContent } from './api'

interface Props {
  running: boolean
  hasContent: boolean
  changedDefinitions: string[]
  definitions: SeedDefinitionStatus[]
  /** The definition the actions apply to, or null for every definition. */
  scope: string | null
  onScopeChange: (definitionId: string | null) => void
  error: string | null
  onRun: (name: OperationName, choice?: EditorChoice) => Promise<void>
}

type Confirmable = 'Reseed' | 'Delete'

// How many editor pages the warning lists by name before it summarises the rest.
const MAX_LISTED_PAGES = 10

const confirmCopy: Record<Confirmable, { title: string; body: string; action: string }> = {
  Reseed: {
    title: 'Reseed content?',
    body: 'The seeded pages, blocks and images will be deleted, then built again from the current seed definition. Any edits made to that seeded content will be lost.',
    action: 'Delete and reseed',
  },
  Delete: {
    title: 'Delete seeded content?',
    body: 'The pages, blocks, sections and images the seeder created will be deleted. Pages the seeder didn’t create, but which sit under seeded pages, are checked first so you can keep them.',
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
  const [report, setReport] = useState<EditorContentReport | null>(null)
  const [reportError, setReportError] = useState<string | null>(null)
  const [option, setOption] = useState<'anyway' | 'move'>('move')
  const [moveTargetId, setMoveTargetId] = useState<number | null>(null)
  const dialogRef = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = dialogRef.current
    if (!dialog) return
    if (confirm && !dialog.open) dialog.showModal()
    if (!confirm && dialog.open) dialog.close()
  }, [confirm])

  // Opens the confirmation and checks for editor pages first, so the warning is shown before anything is deleted.
  const openConfirm = (name: Confirmable) => {
    setConfirm(name)
    setReport(null)
    setReportError(null)
    getEditorContent(scope)
      .then(found => {
        setReport(found)
        setOption(found.targets.length > 0 ? 'move' : 'anyway')
        setMoveTargetId(found.targets[0]?.contentId ?? null)
      })
      .catch(e => setReportError(e instanceof Error ? e.message : 'Unknown error'))
  }

  const start = async (name: OperationName, choice?: EditorChoice) => {
    setConfirm(null)
    setStarting(name)
    try {
      await onRun(name, choice)
    } finally {
      setStarting(null)
    }
  }

  // With editor pages found, the choice made in the warning goes with the request. Without any, the request is plain.
  const confirmAction = (name: Confirmable) => {
    if (!report || report.pages.length === 0) return start(name)
    if (option === 'anyway') return start(name, { kind: 'anyway' })
    if (moveTargetId !== null) return start(name, { kind: 'move', targetId: moveTargetId })
  }

  const disabled = running || starting !== null
  // The drift callout only concerns the definitions the actions apply to.
  const changedInScope = scope === null ? changedDefinitions : changedDefinitions.filter(id => id === scope)
  const drift = changedInScope.length > 0 && hasContent
  const scopeName = scope ?? 'every seed definition'
  const scopeNote = scope === null
    ? 'Every seed definition is affected.'
    : `Only ${scope} is affected. Other seed definitions are left alone.`

  // The editor-page part of the confirmation: a check in progress, a failed check, or the warning with its two choices.
  const editorWarning = (name: Confirmable) => {
    if (reportError) {
      return (
        <div className="cs-callout cs-callout--danger" role="alert">
          Couldn’t check for pages under seeded content: {reportError}. Cancel and try again.
        </div>
      )
    }
    if (!report) return <p className="cs-small cs-muted">Checking for pages under seeded content…</p>
    if (report.pages.length === 0) return null

    const count = report.pages.length
    const listed = report.pages.slice(0, MAX_LISTED_PAGES)
    const hidden = count - listed.length
    const canMove = report.targets.length > 0
    return (
      <div className="cs-callout cs-callout--danger">
        <strong>
          {count === 1
            ? '1 page not created by the seeder sits under seeded content'
            : `${count} pages not created by the seeder sit under seeded content`}
        </strong>
        <span className="cs-small">
          {name === 'Reseed' ? 'Reseeding' : 'Deleting the seeded content'} also deletes these pages and everything under them.
        </span>
        <ul className="cs-small" style={{ listStyle: 'disc', margin: '4px 0 8px 20px', padding: 0 }}>
          {listed.map(p => <li key={p.contentId} style={{ display: 'list-item', listStyleType: 'disc', listStylePosition: 'outside' }}>{p.name}</li>)}
          {hidden > 0 && <li style={{ display: 'list-item', listStyleType: 'disc', listStylePosition: 'outside' }}>and {hidden} more</li>}
        </ul>
        <fieldset className="cs-choice">
          <legend className="cs-small">What should happen to them?</legend>
          <label className="cs-small">
            <input type="radio" name="cs-editor-option" style={{ margin: '0 12px 0 0' }} checked={option === 'move'} disabled={!canMove}
              onChange={() => setOption('move')} />
            Move to{' '}
            <select className="cs-select" aria-label="Move editor pages to" value={moveTargetId ?? ''}
              disabled={!canMove || option !== 'move'}
              onChange={e => setMoveTargetId(Number(e.target.value))}>
              {report.targets.map(t => <option key={t.contentId} value={t.contentId}>{t.name}</option>)}
            </select>{' '}
            and delete
          </label>
          <label className="cs-small">
            <input type="radio" name="cs-editor-option" style={{ margin: '0 12px 0 0' }} checked={option === 'anyway'}
              onChange={() => setOption('anyway')} />
            Delete anyway
          </label>
          {!canMove && <span className="cs-small cs-muted">There’s no page to move them to, so they can only be deleted.</span>}
        </fieldset>
      </div>
    )
  }

  const actionLabel = (name: Confirmable) => {
    if (!report || report.pages.length === 0) return confirmCopy[name].action
    return option === 'anyway' ? 'Delete anyway' : 'Move and delete'
  }

  const actionDisabled = (): boolean => {
    if (reportError || !report) return true
    return report.pages.length > 0 && option === 'move' && moveTargetId === null
  }

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
            <button className="cs-button cs-button--primary" onClick={() => openConfirm('Reseed')} disabled={disabled}>
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
          <button className={`cs-button ${drift ? 'cs-button--primary' : 'cs-button--secondary'}`} onClick={() => openConfirm('Reseed')} disabled={disabled || !hasContent}>
            {starting === 'Reseed' ? 'Starting…' : 'Reseed'}
          </button>
        </ActionRow>
        <ActionRow
          title="Delete"
          description={`Remove the content the seeder created for ${scopeName}, including its images. Pages the seeder didn’t create that sit under seeded pages are listed first, so you can move or delete them.`}
        >
          <button className="cs-button cs-button--danger" onClick={() => openConfirm('Delete')} disabled={disabled || !hasContent}>
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
            {editorWarning(confirm)}
            <p className="cs-irreversible">This can’t be undone.</p>
            <div className="cs-dialog__actions">
              <button type="button" className="cs-button cs-button--ghost" onClick={() => setConfirm(null)} autoFocus>
                Cancel
              </button>
              <button type="button" className="cs-button cs-button--danger-solid" disabled={actionDisabled()}
                onClick={() => confirmAction(confirm)}>
                {actionLabel(confirm)}
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

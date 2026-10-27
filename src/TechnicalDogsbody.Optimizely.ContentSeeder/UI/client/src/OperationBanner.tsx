import { SeedOperation } from './api'
import { duration, timeAgo } from './format'

const runningCopy: Record<string, string> = {
  Seed: 'Seeding content',
  Reseed: 'Reseeding — removing seeded content, then creating it again',
  Delete: 'Deleting seeded content',
}

const pastTense: Record<string, string> = {
  Seed: 'Seed',
  Reseed: 'Reseed',
  Delete: 'Delete',
}

export function OperationBanner({ operation, now }: { operation: SeedOperation; now: number }) {
  if (operation.isRunning && operation.name) {
    return (
      <section className="cs-card cs-operation" aria-live="polite">
        <div className="cs-operation__row">
          <strong>{runningCopy[operation.name] ?? operation.name}…</strong>
          {operation.startedUtc && (
            <span className="cs-mono cs-muted">{duration(operation.startedUtc, now)}</span>
          )}
        </div>
        <div className="cs-progress" role="progressbar" aria-label={`${operation.name} in progress`}>
          <span className="cs-progress__bar" />
        </div>
        <p className="cs-muted cs-small">Counts below update live. You can leave this page — the run continues on the server.</p>
      </section>
    )
  }

  if (!operation.name || !operation.completedUtc) return null

  const took = operation.startedUtc ? duration(operation.startedUtc, new Date(operation.completedUtc).getTime()) : null
  const label = pastTense[operation.name] ?? operation.name

  if (operation.succeeded === false) {
    return (
      <div className="cs-callout cs-callout--danger" role="alert">
        <strong>{label} failed {timeAgo(operation.completedUtc, now)}.</strong>
        {operation.error && <span className="cs-mono cs-small cs-error-text">{operation.error}</span>}
        <span className="cs-small">Check the application log for the full error. Running Seed again resumes where it stopped.</span>
      </div>
    )
  }

  return (
    <div className="cs-callout cs-callout--success">
      <strong>{label} finished {timeAgo(operation.completedUtc, now)}</strong>
      {took && <span className="cs-small">Took {took}.</span>}
    </div>
  )
}

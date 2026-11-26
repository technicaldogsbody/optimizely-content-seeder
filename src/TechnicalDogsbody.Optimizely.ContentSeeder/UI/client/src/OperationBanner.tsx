import { useEffect, useState } from 'react'
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

// What is left after a stopped run, and how to carry on.
const stoppedCopy: Record<string, string> = {
  Seed: 'Content created before it stopped is kept. Run Seed to carry on from there.',
  Reseed: 'Some seeded content may have been removed and not yet created again. Run Seed to carry on from there.',
  Delete: 'Some seeded content may remain. Run Delete again to remove the rest.',
}

// The action word of an operation name: "Reseed live-demo-v1" is a Reseed.
const actionOf = (name: string) => name.split(' ')[0]

export function OperationBanner({
  operation,
  now,
  onCancel,
}: {
  operation: SeedOperation
  now: number
  onCancel: () => Promise<void>
}) {
  const [stopping, setStopping] = useState(false)
  const [stopError, setStopError] = useState<string | null>(null)

  // A new run starts with its own Stop button.
  useEffect(() => {
    setStopping(false)
    setStopError(null)
  }, [operation.startedUtc])

  if (operation.isRunning && operation.name) {
    const isStopping = operation.isCancelling || stopping
    const stop = async () => {
      setStopError(null)
      setStopping(true)
      try {
        await onCancel()
      } catch (e) {
        setStopping(false)
        setStopError(e instanceof Error ? e.message : 'Unknown error')
      }
    }

    return (
      <section className="cs-card cs-operation" aria-live="polite">
        <div className="cs-operation__row">
          <strong>
            {isStopping ? 'Stopping' : runningCopy[actionOf(operation.name)] ?? operation.name}…
          </strong>
          <span className="cs-operation__actions">
            {operation.startedUtc && (
              <span className="cs-mono cs-muted">{duration(operation.startedUtc, now)}</span>
            )}
            <button className="cs-button cs-button--danger cs-button--small" onClick={stop} disabled={isStopping}>
              {isStopping ? 'Stopping…' : 'Stop'}
            </button>
          </span>
        </div>
        <div className="cs-progress" role="progressbar" aria-label={`${operation.name} in progress`}>
          <span className="cs-progress__bar" />
        </div>
        {stopError && <span className="cs-small cs-error-text" role="alert">{stopError}</span>}
        <p className="cs-muted cs-small">
          {isStopping
            ? 'The run stops after the item it is working on. Content created so far is kept.'
            : 'Counts below update live. You can leave this page — the run continues on the server.'}
        </p>
      </section>
    )
  }

  if (!operation.name || !operation.completedUtc) return null

  const took = operation.startedUtc ? duration(operation.startedUtc, new Date(operation.completedUtc).getTime()) : null
  const label = pastTense[actionOf(operation.name)] ?? operation.name

  if (operation.cancelled) {
    return (
      <div className="cs-callout cs-callout--info" role="status">
        <strong>{label} stopped {timeAgo(operation.completedUtc, now)}.</strong>
        {took && <span className="cs-small">Ran for {took}.</span>}
        <span className="cs-small">{stoppedCopy[actionOf(operation.name)] ?? stoppedCopy.Seed}</span>
      </div>
    )
  }

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

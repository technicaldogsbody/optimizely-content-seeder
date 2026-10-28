import { useCallback, useEffect, useRef, useState } from 'react'
import { ContentSeederStatus, OperationName, getStatus, startOperation } from './api'
import { StatusPanel } from './StatusPanel'
import { OperationBanner } from './OperationBanner'
import { DryRunPanel } from './DryRunPanel'
import { ActionButtons } from './ActionButtons'

const RUNNING_POLL_MS = 1500
const IDLE_POLL_MS = 10000

export interface Counts {
  pages: number
  blocks: number
  sections: number
}

const countsOf = (s: ContentSeederStatus): Counts => ({
  pages: s.trackedPageCount,
  blocks: s.trackedBlockCount,
  sections: s.trackedSectionCount,
})

export default function App() {
  const [status, setStatus] = useState<ContentSeederStatus | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [pollCount, setPollCount] = useState(0)
  const [now, setNow] = useState(() => Date.now())
  // The definition the actions apply to. Null means every definition.
  const [selectedId, setSelectedId] = useState<string | null>(null)

  // Counts captured when a run starts, so tiles can show the change made by this run.
  const baseline = useRef<{ startedUtc: string; counts: Counts } | null>(null)
  // Counts just before this tab started an operation (more accurate than the first poll).
  const pendingBaseline = useRef<Counts | null>(null)

  const fetchStatus = useCallback(async () => {
    try {
      const data = await getStatus()
      const op = data.operation
      if (op.startedUtc && baseline.current?.startedUtc !== op.startedUtc) {
        // First sight of this run. If we only noticed it mid-run, fall back to the current counts.
        baseline.current = { startedUtc: op.startedUtc, counts: pendingBaseline.current ?? countsOf(data) }
        pendingBaseline.current = null
      }
      setStatus(data)
      setLoadError(null)
    } catch (e) {
      setLoadError(e instanceof Error ? e.message : 'Unable to load status.')
    } finally {
      setPollCount(c => c + 1)
    }
  }, [])

  const isRunning = status?.operation.isRunning ?? false

  // Schedules the next poll after every attempt: fast while running, slow when idle.
  useEffect(() => {
    const timer = setTimeout(fetchStatus, pollCount === 0 ? 0 : isRunning ? RUNNING_POLL_MS : IDLE_POLL_MS)
    return () => clearTimeout(timer)
  }, [fetchStatus, pollCount, isRunning])

  // Ticks the elapsed-time and "x minutes ago" labels.
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), isRunning ? 1000 : 30000)
    return () => clearInterval(timer)
  }, [isRunning])

  // A selection that no longer exists (a definition was removed) falls back to every definition.
  const scope: string | null =
    status && selectedId !== null && status.definitions.some(d => d.seedDefinitionId === selectedId) ? selectedId : null

  const run = async (name: OperationName) => {
    setActionError(null)
    if (status) pendingBaseline.current = countsOf(status)
    try {
      await startOperation(name, scope)
    } catch (e) {
      pendingBaseline.current = null
      setActionError(e instanceof Error ? e.message : 'Unknown error')
    }
    await fetchStatus()
  }

  const counts = status ? countsOf(status) : null
  const deltas = counts && baseline.current
    ? {
        pages: counts.pages - baseline.current.counts.pages,
        blocks: counts.blocks - baseline.current.counts.blocks,
        sections: counts.sections - baseline.current.counts.sections,
      }
    : null

  // Whether the chosen scope has any seeded content, which decides whether Reseed and Delete are offered.
  const scopedDefinition = scope === null ? null : status?.definitions.find(d => d.seedDefinitionId === scope) ?? null
  const hasContent = scope === null
    ? (counts?.pages ?? 0) + (counts?.blocks ?? 0) + (counts?.sections ?? 0) > 0
    : scopedDefinition !== null && scopedDefinition.pageCount + scopedDefinition.blockCount + scopedDefinition.sectionCount > 0

  return (
    <div className="cs-app">
      <div className="cs-shell">
        <header className="cs-header">
          <div>
            <p className="cs-eyebrow">Add-on</p>
            <h1 className="cs-title">Content Seeder</h1>
            <p className="cs-subtitle">Generate realistic pages, blocks{status?.supportsSections === false ? '' : ' and Visual Builder content'} from your seed definitions.</p>
          </div>
          <StatusPill status={status} />
        </header>

        {loadError && !status && <div className="cs-callout cs-callout--danger">{loadError}</div>}

        {status && (
          <>
            <OperationBanner operation={status.operation} now={now} />
            <StatusPanel status={status} deltas={deltas} now={now} stale={loadError !== null} />
            <ActionButtons
              running={isRunning}
              onRun={run}
              error={actionError}
              changedDefinitions={status.changedDefinitions ?? []}
              definitions={status.definitions}
              scope={scope}
              onScopeChange={setSelectedId}
              hasContent={hasContent}
            />
            <DryRunPanel disabled={isRunning} scope={scope} />
          </>
        )}

        {!status && !loadError && <div className="cs-card cs-skeleton" aria-busy="true">Loading status…</div>}

        <footer className="cs-footer">
          <span className="cs-ready" aria-hidden="true">READY.<span className="cs-cursor" /></span>
          <span>
            Built by{' '}
            <a href="https://www.technicaldogsbody.com" target="_blank" rel="noopener noreferrer">
              TechnicalDogsbody
            </a>
            {' · '}
            <a href="https://github.com/technicaldogsbody/optimizely-content-seeder" target="_blank" rel="noopener noreferrer">
              Documentation
            </a>
          </span>
        </footer>
      </div>
    </div>
  )
}

function StatusPill({ status }: { status: ContentSeederStatus | null }) {
  if (!status) return null
  const op = status.operation
  if (op.isRunning) {
    return <span className="cs-pill cs-pill--running"><span className="cs-pulse" />{op.name} running</span>
  }
  if (op.succeeded === false) return <span className="cs-pill cs-pill--danger">Last run failed</span>
  return <span className="cs-pill cs-pill--idle">Idle</span>
}

import { ContentSeederStatus, ProviderInfo } from './api'
import type { Counts } from './App'
import { duration, formatNumber, timeAgo } from './format'

interface Props {
  status: ContentSeederStatus
  deltas: Counts | null
  now: number
  stale: boolean
}

export function StatusPanel({ status, deltas, now, stale }: Props) {
  return (
    <section className="cs-card" aria-labelledby="cs-status-heading">
      <div className="cs-card__header">
        <h2 id="cs-status-heading">Seeded content</h2>
        {stale && <span className="cs-small cs-warning-text">Reconnecting…</span>}
      </div>

      <div className="cs-stats">
        <Stat label="Pages" value={status.trackedPageCount} delta={deltas?.pages} />
        <Stat label="Blocks" value={status.trackedBlockCount} delta={deltas?.blocks} />
        {status.supportsSections && <Stat label="Sections" value={status.trackedSectionCount} delta={deltas?.sections} />}
        <div className="cs-stat">
          {status.operation.isRunning && status.operation.startedUtc ? (
            <>
              <span className="cs-stat__label">Running for</span>
              <span className="cs-stat__value cs-stat__value--text cs-mono">{duration(status.operation.startedUtc, now)}</span>
            </>
          ) : (
            <>
              <span className="cs-stat__label">Last seeded</span>
              <span className="cs-stat__value cs-stat__value--text" title={status.lastSeededUtc ? new Date(status.lastSeededUtc).toLocaleString() : undefined}>
                {status.lastSeededUtc ? timeAgo(status.lastSeededUtc, now) : 'Never'}
              </span>
            </>
          )}
        </div>
      </div>

      <dl className="cs-meta">
        <div>
          <dt>Text</dt>
          <dd><Provider info={status.providers.text} /></dd>
        </div>
        <div>
          <dt>Images</dt>
          <dd><Provider info={status.providers.images} /></dd>
        </div>
        <div>
          <dt>Seed on startup</dt>
          <dd><span className={`cs-badge ${status.seedOnStartup ? 'cs-badge--on' : ''}`}>{status.seedOnStartup ? 'On' : 'Off'}</span></dd>
        </div>
        <div>
          <dt>Languages</dt>
          <dd>
            {status.languages.length > 0
              ? status.languages.map((l, i) => (
                  <span key={l} className="cs-badge" title={i === 0 ? 'Master language' : undefined}>
                    {l}{i === 0 && ' · master'}
                  </span>
                ))
              : <span className="cs-muted">Single language</span>}
          </dd>
        </div>
      </dl>

      {status.hasPartialRun && !status.operation.isRunning && (
        <div className="cs-callout cs-callout--warning">
          <strong>A previous run didn’t finish.</strong>
          <span className="cs-small">Seed will pick up where it stopped.</span>
        </div>
      )}
    </section>
  )
}

// What is generating this kind of content: blue for AI, neutral for placeholder data, plus the model.
function Provider({ info }: { info: ProviderInfo }) {
  return (
    <>
      <span className={`cs-badge ${info.isAi ? 'cs-badge--info' : ''}`}>{info.isAi ? `✦ ${info.name}` : info.name}</span>
      {info.detail && <span className="cs-mono cs-small cs-muted">{info.detail}</span>}
    </>
  )
}

function Stat({ label, value, delta }: { label: string; value: number; delta?: number }) {
  return (
    <div className="cs-stat">
      <span className="cs-stat__label">{label}</span>
      {/* Keyed on the value so the highlight animation replays each time it changes. */}
      <span key={value} className="cs-stat__value cs-bump">{formatNumber(value)}</span>
      {delta !== undefined && delta !== 0 && (
        <span className={`cs-delta ${delta > 0 ? 'cs-delta--up' : 'cs-delta--down'}`}>
          {delta > 0 ? '+' : '−'}{formatNumber(Math.abs(delta))} this run
        </span>
      )}
    </div>
  )
}

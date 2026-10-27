import { useEffect, useState } from 'react'
import { postDryRun, DryRunResult } from './api'
import { formatNumber } from './format'

export function DryRunPanel({ disabled, scope }: { disabled: boolean; scope: string | null }) {
  const [result, setResult] = useState<DryRunResult | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // A result describes the definitions it was calculated for, so it is cleared when the selection changes.
  useEffect(() => {
    setResult(null)
    setError(null)
  }, [scope])

  const handleDryRun = async () => {
    setLoading(true)
    setError(null)
    try {
      setResult(await postDryRun(scope))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unknown error')
    } finally {
      setLoading(false)
    }
  }

  const anyChanged = result?.definitions.some(d => d.definitionChanged) ?? false

  const totals = result?.definitions.reduce(
    (t, d) => ({ create: t.create + d.wouldCreate, skip: t.skip + d.wouldSkip }),
    { create: 0, skip: 0 },
  )

  return (
    <section className="cs-card" aria-labelledby="cs-dryrun-heading">
      <div className="cs-card__header">
        <div>
          <h2 id="cs-dryrun-heading">Dry run</h2>
          <p className="cs-muted cs-small">Preview what Seed would create, without writing anything.</p>
        </div>
        <button className="cs-button cs-button--ghost" onClick={handleDryRun} disabled={loading || disabled}>
          {loading ? 'Calculating…' : result ? 'Run again' : 'Run dry run'}
        </button>
      </div>

      {error && <div className="cs-callout cs-callout--danger" role="alert">{error}</div>}

      {result && result.definitions.length === 0 && (
        <p className="cs-muted">No seed definitions are registered.</p>
      )}

      {result && result.definitions.length > 0 && totals && (
        <table className="cs-table">
          <thead>
            <tr>
              <th scope="col">Definition</th>
              <th scope="col" className="cs-num">Would create</th>
              <th scope="col" className="cs-num">Already seeded</th>
            </tr>
          </thead>
          <tbody>
            {result.definitions.map(d => (
              <tr key={d.seedDefinitionId}>
                <td>
                  <span className="cs-mono">{d.seedDefinitionId}</span>
                  {d.definitionChanged && <span className="cs-badge cs-badge--info cs-badge--inline">Changed</span>}
                </td>
                <td className="cs-num">{formatNumber(d.wouldCreate)}</td>
                <td className="cs-num">{formatNumber(d.wouldSkip)}</td>
              </tr>
            ))}
          </tbody>
          {result.definitions.length > 1 && (
            <tfoot>
              <tr>
                <th scope="row">Total</th>
                <td className="cs-num">{formatNumber(totals.create)}</td>
                <td className="cs-num">{formatNumber(totals.skip)}</td>
              </tr>
            </tfoot>
          )}
        </table>
      )}

      {result && result.definitions.map(d => (
        <div key={d.seedDefinitionId} className="cs-breakdown">
          <p className="cs-small">
            <span className="cs-mono">{d.seedDefinitionId}</span>
            <span className="cs-muted"> · languages: {d.languages.join(', ')}</span>
            {d.hasPartialRun && <span className="cs-badge cs-badge--info cs-badge--inline">Interrupted run</span>}
          </p>
          <table className="cs-table">
            <thead>
              <tr>
                <th scope="col">Kind</th>
                <th scope="col" className="cs-num">Planned</th>
                <th scope="col" className="cs-num">Already seeded</th>
                <th scope="col" className="cs-num">Would create</th>
              </tr>
            </thead>
            <tbody>
              {d.kinds.map(k => (
                <tr key={k.kind}>
                  <td>{k.kind}</td>
                  <td className="cs-num">{formatNumber(k.planned)}</td>
                  <td className="cs-num">{formatNumber(k.alreadySeeded)}</td>
                  <td className="cs-num">{formatNumber(k.wouldCreate)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ))}

      {anyChanged && (
        <p className="cs-small cs-muted">
          Definitions marked <strong>Changed</strong> differ from the content already seeded. Seed won’t apply those
          changes to existing content, so they aren’t counted above — Reseed to rebuild it.
        </p>
      )}
    </section>
  )
}

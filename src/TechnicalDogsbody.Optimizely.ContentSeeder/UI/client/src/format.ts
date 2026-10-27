const relative = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' })

export function timeAgo(iso: string, now: number): string {
  const seconds = Math.round((new Date(iso).getTime() - now) / 1000)
  const abs = Math.abs(seconds)
  if (abs < 60) return relative.format(Math.round(seconds), 'second')
  if (abs < 3600) return relative.format(Math.trunc(seconds / 60), 'minute')
  if (abs < 86400) return relative.format(Math.trunc(seconds / 3600), 'hour')
  return relative.format(Math.trunc(seconds / 86400), 'day')
}

export function duration(fromIso: string, toMs: number): string {
  const total = Math.max(0, Math.floor((toMs - new Date(fromIso).getTime()) / 1000))
  const m = Math.floor(total / 60)
  const s = total % 60
  return m > 0 ? `${m}m ${String(s).padStart(2, '0')}s` : `${s}s`
}

export const formatNumber = (n: number) => n.toLocaleString()

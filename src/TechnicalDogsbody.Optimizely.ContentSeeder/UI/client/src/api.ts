export type OperationName = 'Seed' | 'Reseed' | 'Delete'

export interface SeedOperation {
  name: OperationName | null
  isRunning: boolean
  startedUtc: string | null
  completedUtc: string | null
  succeeded: boolean | null
  error: string | null
}

export interface ProviderInfo {
  name: string
  detail: string | null
  /** True when the content is AI-generated rather than placeholder data. */
  isAi: boolean
}

/** What has been seeded for one seed definition. */
export interface SeedDefinitionStatus {
  seedDefinitionId: string
  pageCount: number
  blockCount: number
  sectionCount: number
  lastSeededUtc: string | null
  hasPartialRun: boolean
  /** True when the definition's shape changed since it was seeded: Seed won't apply it, Reseed will. */
  isChanged: boolean
}

export interface ContentSeederStatus {
  /** False on a CMS without sections (Optimizely CMS 12): sections and Visual Builder are then hidden. */
  supportsSections: boolean
  trackedPageCount: number
  trackedBlockCount: number
  trackedSectionCount: number
  lastSeededUtc: string | null
  seedOnStartup: boolean
  languages: string[]
  hasPartialRun: boolean
  /** Seed definitions whose shape changed since they were seeded — Seed won't apply it, Reseed will. */
  changedDefinitions: string[]
  /** Every registered seed definition, including ones that have never been seeded. */
  definitions: SeedDefinitionStatus[]
  providers: { text: ProviderInfo; images: ProviderInfo }
  operation: SeedOperation
}

export interface DryRunKindCount {
  kind: string
  planned: number
  alreadySeeded: number
  wouldCreate: number
}

export interface DryRunDefinitionResult {
  seedDefinitionId: string
  wouldCreate: number
  wouldSkip: number
  definitionChanged: boolean
  /** Pages, blocks and shared sections, and section blueprints, each planned and counted on their own. */
  kinds: DryRunKindCount[]
  /** The languages a seed would write, the master language first. */
  languages: string[]
  /** True when an earlier run of this definition was interrupted and has unfinished items. */
  hasPartialRun: boolean
}

export interface DryRunResult {
  definitions: DryRunDefinitionResult[]
}

/** A page the seeder did not create, sitting under a seeded page. Deleting the seeded page deletes it too. */
export interface EditorPage {
  contentId: number
  name: string
  seededParentId: number
}

/** A page that editor pages can be moved under. */
export interface MoveTarget {
  contentId: number
  name: string
}

/** The editor pages a delete or reseed would remove, and where they could be moved. No pages means it is safe. */
export interface EditorContentReport {
  pages: EditorPage[]
  targets: MoveTarget[]
}

/** What to do with editor pages before a delete or reseed. */
export type EditorChoice = { kind: 'anyway' } | { kind: 'move'; targetId: number }

const BASE = '/api/contentseeder'

// All requests must include credentials so the ASP.NET auth cookie is sent.
const defaults: RequestInit = { credentials: 'include' }

// The URL for an operation, scoped to one definition when one is chosen. Null means every definition.
function operationUrl(path: string, definitionId: string | null): string {
  return definitionId === null
    ? `${BASE}/${path}`
    : `${BASE}/${path}?definitionId=${encodeURIComponent(definitionId)}`
}

export async function getStatus(): Promise<ContentSeederStatus> {
  const res = await fetch(`${BASE}/status`, { ...defaults, cache: 'no-store' })
  if (!res.ok) throw new Error(`Status fetch failed (${res.status})`)
  return res.json()
}

export async function postDryRun(definitionId: string | null): Promise<DryRunResult> {
  const res = await fetch(operationUrl('dryrun', definitionId), { ...defaults, method: 'POST' })
  if (res.status === 400) throw new Error('That seed definition no longer exists. Refresh the page.')
  if (!res.ok) throw new Error(`Dry run failed (${res.status})`)
  return res.json()
}

const operationPaths: Record<OperationName, string> = {
  Seed: 'seed',
  Reseed: 'reseed',
  Delete: 'delete',
}

/** The editor pages a delete or reseed would remove for one definition, or every definition when null. */
export async function getEditorContent(definitionId: string | null): Promise<EditorContentReport> {
  const res = await fetch(operationUrl('editor-content', definitionId), { ...defaults, cache: 'no-store' })
  if (!res.ok) throw new Error(`Checking for editor pages failed (${res.status})`)
  return res.json()
}

// Adds what to do with editor pages to the operation's URL. Reseed and Delete need it when editor pages exist.
function withEditorChoice(url: string, choice: EditorChoice | undefined): string {
  if (!choice) return url
  const params = new URLSearchParams(choice.kind === 'anyway'
    ? { editorContent: 'anyway' }
    : { editorContent: 'move', moveTargetId: String(choice.targetId) })
  return `${url}${url.includes('?') ? '&' : '?'}${params}`
}

export async function startOperation(
  name: OperationName,
  definitionId: string | null,
  choice?: EditorChoice,
): Promise<void> {
  const url = withEditorChoice(operationUrl(operationPaths[name], definitionId), choice)
  const res = await fetch(url, { ...defaults, method: 'POST' })
  if (res.status === 409) throw new Error('Another operation is already in progress.')
  if (res.status === 400) throw new Error('That seed definition no longer exists. Refresh the page.')
  if (res.status === 422) throw new Error('The pages under seeded content have changed. Check again before continuing.')
  if (!res.ok) throw new Error(`${name} failed to start (${res.status})`)
}

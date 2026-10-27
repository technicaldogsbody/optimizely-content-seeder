#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Installs the ContentSeeder "seed-definition" skill into a project, in the format each AI coding tool reads.

.DESCRIPTION
    skills/seed-definition/SKILL.md is the single source. This script wraps its body in the file format and
    location each tool expects, so the instructions never drift between tools. Re-running it updates the
    installed files in place (AGENTS.md / GEMINI.md / copilot-instructions.md are edited between markers only).

.PARAMETER Target
    The project to install into (default: the current directory).

.PARAMETER Tool
    One or more of: claude, copilot, cursor, windsurf, cline, codex, gemini, all (default: all).

.EXAMPLE
    ./install.ps1 -Target C:\src\MySite -Tool claude,copilot
#>
[CmdletBinding()]
param(
    [string]$Target = (Get-Location).Path,
    [ValidateSet('claude', 'copilot', 'cursor', 'windsurf', 'cline', 'codex', 'gemini', 'all')]
    [string[]]$Tool = @('all')
)

$ErrorActionPreference = 'Stop'

$sourcePath = Join-Path $PSScriptRoot 'seed-definition/SKILL.md'
if (-not (Test-Path $sourcePath)) { throw "Cannot find $sourcePath" }

$raw = (Get-Content -Raw -Path $sourcePath) -replace "`r`n", "`n"
if ($raw -notmatch '(?s)^---\n(?<front>.*?)\n---\n(?<body>.*)$') { throw 'SKILL.md must start with YAML frontmatter.' }
$body = $Matches['body'].Trim() + "`n"
$description = ([regex]::Match($Matches['front'], '(?m)^description:\s*(.+)$')).Groups[1].Value.Trim()
$title = '# Writing ContentSeeder seed definitions'
$globs = '**/*Seed*.cs'

$Target = (Resolve-Path $Target).Path
$tools = if ($Tool -contains 'all') { 'claude', 'copilot', 'cursor', 'windsurf', 'cline', 'codex', 'gemini' } else { $Tool }

function Write-File([string]$relative, [string]$content) {
    $path = Join-Path $Target $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null
    [System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))
    Write-Host "  wrote $relative"
}

# For files shared with other instructions (AGENTS.md etc.): replace our block if present, else append.
function Write-Block([string]$relative, [string]$content) {
    $start = '<!-- contentseeder:seed-definition:start -->'
    $end = '<!-- contentseeder:seed-definition:end -->'
    $block = "$start`n$content`n$end`n"
    $path = Join-Path $Target $relative
    $existing = if (Test-Path $path) { (Get-Content -Raw -Path $path) -replace "`r`n", "`n" } else { '' }
    $pattern = "(?s)$([regex]::Escape($start)).*?$([regex]::Escape($end))\n?"
    $updated = if ($existing -match $pattern) { [regex]::Replace($existing, $pattern, [System.Text.RegularExpressions.MatchEvaluator] { param($m) $block }) }
               elseif ($existing.Length -gt 0) { $existing.TrimEnd() + "`n`n" + $block }
               else { $block }
    Write-File $relative $updated
}

foreach ($t in $tools) {
    Write-Host "$t"
    switch ($t) {
        # Agent Skills format: the canonical file as it is.
        'claude' { Write-File '.claude/skills/seed-definition/SKILL.md' $raw }

        # Copilot (VS Code, Visual Studio, JetBrains, GitHub.com): path-scoped custom instructions.
        'copilot' {
            Write-File '.github/instructions/seed-definition.instructions.md' ("---`napplyTo: `"$globs`"`ndescription: `"$description`"`n---`n`n$title`n`n$body")
        }

        # Cursor project rule, attached automatically to seed definition files.
        'cursor' {
            Write-File '.cursor/rules/seed-definition.mdc' ("---`ndescription: $description`nglobs: $globs`nalwaysApply: false`n---`n`n$title`n`n$body")
        }

        # Windsurf workspace rule.
        'windsurf' {
            Write-File '.windsurf/rules/seed-definition.md' ("---`ntrigger: glob`nglobs: $globs`ndescription: $description`n---`n`n$title`n`n$body")
        }

        # Cline rules folder.
        'cline' { Write-File '.clinerules/seed-definition.md' "$title`n`n$description`n`n$body" }

        # OpenAI Codex, and any other tool that reads AGENTS.md.
        'codex' { Write-Block 'AGENTS.md' "## Writing ContentSeeder seed definitions`n`n$description`n`n$body" }

        # Gemini CLI.
        'gemini' { Write-Block 'GEMINI.md' "## Writing ContentSeeder seed definitions`n`n$description`n`n$body" }
    }
}

Write-Host "Done. Installed into $Target"

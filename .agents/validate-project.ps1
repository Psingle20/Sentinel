[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$cursor = $projectRoot
while (-not [string]::IsNullOrWhiteSpace($cursor)) {
    $workspaceValidator = Join-Path $cursor 'tools\validate-workspace.ps1'
    if (Test-Path -LiteralPath $workspaceValidator -PathType Leaf) {
        & $workspaceValidator
        return
    }
    $parent = Split-Path -Parent $cursor
    if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $cursor) { break }
    $cursor = $parent
}

$requiredFiles = @(
    'AGENTS.md', 'README.md', 'PROJECT.json', '.agents\steering.md', '.agents\commit-guidelines.md',
    '.agents\validate-commit-message.ps1',
    '.codex\config.toml', '.claude\CLAUDE.md', '.claude\settings.json', '.claude\hooks\stop-validation.ps1'
)
$requiredDirectories = @('docs', 'docs\business-contracts', 'artifacts', 'apps', 'services', 'packages', 'deploy', 'scripts', 'personal-docs')
$errors = [System.Collections.Generic.List[string]]::new()

foreach ($relative in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot $relative) -PathType Leaf)) {
        $errors.Add("Missing required project file '$relative'.")
    }
}
foreach ($relative in $requiredDirectories) {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot $relative) -PathType Container)) {
        $errors.Add("Missing required project directory '$relative'.")
    }
}
foreach ($obsoleteMirror in @('.agents\skills', '.claude\skills')) {
    if (Test-Path -LiteralPath (Join-Path $projectRoot $obsoleteMirror)) {
        $errors.Add("Remove obsolete local skill mirror '$obsoleteMirror'; shared skills are installed globally.")
    }
}

$gitignorePath = Join-Path $projectRoot '.gitignore'
if (-not (Test-Path -LiteralPath $gitignorePath -PathType Leaf) -or ((Get-Content -Raw -LiteralPath $gitignorePath) -notmatch '(?m)^/?personal-docs/?\s*$')) {
    $errors.Add("'.gitignore' must exclude 'personal-docs/'; personal-use documents are never committed.")
}

$metadataPath = Join-Path $projectRoot 'PROJECT.json'
if (Test-Path -LiteralPath $metadataPath -PathType Leaf) {
    try { $null = Get-Content -Raw -LiteralPath $metadataPath | ConvertFrom-Json }
    catch { $errors.Add("Invalid PROJECT.json: $($_.Exception.Message)") }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    throw "Standalone project validation failed with $($errors.Count) error(s)."
}

Write-Output 'Standalone project and agent configuration validation passed.'

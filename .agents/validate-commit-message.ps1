[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Message,

    [string]$MessageFile
)

$ErrorActionPreference = 'Stop'
if (-not [string]::IsNullOrWhiteSpace($MessageFile)) {
    if (-not (Test-Path -LiteralPath $MessageFile -PathType Leaf)) {
        throw "Commit message file not found: $MessageFile"
    }
    $Message = Get-Content -Raw -LiteralPath $MessageFile
}
if ([string]::IsNullOrWhiteSpace($Message)) {
    throw 'Provide -Message or -MessageFile.'
}

$header = ($Message -split "`r?`n", 2)[0]
$typePattern = 'feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert'
$headerPattern = "^(?:$typePattern)(?:\([a-z0-9][a-z0-9._/-]*\))?!?: \S(?:.*\S)?$"
$errors = [System.Collections.Generic.List[string]]::new()

if ($header -notmatch $headerPattern) {
    $errors.Add('Header must use type(scope)!: summary with an approved lowercase type and optional lowercase scope.')
}
if ($header.Length -gt 100) {
    $errors.Add("Header is $($header.Length) characters; maximum is 100.")
}
if ($header.EndsWith('.')) {
    $errors.Add('Header must not end with a period.')
}

$agentTrailerKeyPattern = '^(?:generated-by|generated-with|assisted-by|ai-assisted-by|ai-generated-by|agent|agent-by|model|model-by|tool|tool-by):'
$attributionTrailerPattern = '^(?:co-authored-by|signed-off-by|authored-by|created-by):'
$agentIdentityPattern = '(?:codex|claude|chatgpt|openai|copilot|gemini|cursor|anthropic|artificial intelligence|\bai\b|\bagent\b|\bbot\b)'
foreach ($line in ($Message -split "`r?`n")) {
    if ($line -match $agentTrailerKeyPattern -or ($line -match $attributionTrailerPattern -and $line -match $agentIdentityPattern)) {
        $errors.Add("AI-agent attribution trailer is not allowed: $line")
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ }
    throw "Commit message validation failed with $($errors.Count) error(s)."
}

Write-Output "Commit message is valid: $header"

# Claude Code Stop hook: exit 2 returns validation failures to Claude for one repair pass;
# a repeated failure (stop_hook_active) exits 1 so it is reported without looping.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$adapterRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$validator = @(
    (Join-Path $adapterRoot '.agents\validate-project.ps1'),
    (Join-Path $adapterRoot 'tools\validate-workspace.ps1')
) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if ($null -eq $validator) {
    [Console]::Error.WriteLine("No workspace or project validator was found under '$adapterRoot'.")
    exit 1
}

$stopHookActive = $false
if ([Console]::IsInputRedirected) {
    $payload = [Console]::In.ReadToEnd()
    if (-not [string]::IsNullOrWhiteSpace($payload)) {
        try { $stopHookActive = [bool](ConvertFrom-Json -InputObject $payload).stop_hook_active } catch { }
    }
}

$log = [System.Collections.Generic.List[string]]::new()
$failure = $null
try {
    & $validator *>&1 | ForEach-Object { $log.Add([string]$_) }
} catch {
    $failure = $_.Exception.Message
}

if ($null -eq $failure) {
    $log | ForEach-Object { Write-Output $_ }
    exit 0
}

$report = (@($log) + $failure | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join [Environment]::NewLine
if ($stopHookActive) {
    [Console]::Error.WriteLine("Validation is still failing after a repair pass; leaving it for the user.`n$report")
    exit 1
}
[Console]::Error.WriteLine("Validation failed. Fix these problems, or explain why they are outside this task, before finishing:`n$report")
exit 2

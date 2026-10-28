[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('setup', 'build', 'test', 'lint')]
    [string]$Task
)

# Runs a project task across the .NET solution and Sentinel.Web, stopping at the first failure.
# Works in Windows PowerShell 5.1 and PowerShell 7.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'Sentinel.slnx'
$web = Join-Path $root 'apps/Sentinel.Web'

function Invoke-Step([string]$name, [scriptblock]$command) {
    Write-Host "==> $name"
    # Native tools write progress to stderr, which Windows PowerShell treats as an error under
    # 'Stop'. Success is judged by exit code instead.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $command
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($LASTEXITCODE -ne 0) {
        throw "$name failed with exit code $LASTEXITCODE."
    }
}

switch ($Task) {
    'setup' {
        Invoke-Step 'dotnet restore' { dotnet restore $solution }
        Invoke-Step 'npm ci (Sentinel.Web)' { npm --prefix $web ci }
    }
    'build' {
        Invoke-Step 'dotnet build' { dotnet build $solution }
        Invoke-Step 'next build (Sentinel.Web)' { npm --prefix $web run build }
    }
    'test' {
        Invoke-Step 'dotnet test' { dotnet test $solution }
        Invoke-Step 'playwright (Sentinel.Web)' { npm --prefix $web test }
    }
    'lint' {
        Invoke-Step 'dotnet format whitespace' { dotnet format whitespace $solution --verify-no-changes }
        Invoke-Step 'eslint and design-system checks (Sentinel.Web)' { npm --prefix $web run lint }
        Invoke-Step 'typecheck (Sentinel.Web)' { npm --prefix $web run typecheck }
    }
}

Write-Host "$Task passed."

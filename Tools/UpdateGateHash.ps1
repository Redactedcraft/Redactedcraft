param(
    [string]$GateUrl = "",
    [string]$AdminToken = "",
    [string]$ExePath = "",
    [ValidateSet("latest", "release", "debug")]
    [string]$BuildType = "latest",
    [ValidateSet("auto", "release", "dev", "client")]
    [string]$Target = "auto",
    [ValidateSet("replace_source", "merge")]
    [string]$ApplyMode = "replace_source",
    [bool]$ReplaceTargetList = $true,
    [bool]$ClearOtherHashes = $false,
    [switch]$ShowRuntime
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    $current = [System.IO.DirectoryInfo]$PSScriptRoot
    while ($null -ne $current) {
        $projectPath = Join-Path $current.FullName "LatticeVeilMonoGame\LatticeVeilMonoGame.csproj"
        if (Test-Path -LiteralPath $projectPath) {
            return $current.FullName
        }

        $current = $current.Parent
    }

    throw "Could not locate repo root. Run this script from inside the LatticeVeil project."
}

function Resolve-GateUrl {
    param([string]$ConfiguredUrl)

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredUrl)) {
        return $ConfiguredUrl.Trim().TrimEnd("/")
    }

    $envUrl = [string]$env:LV_GATE_URL
    $envUrl = $envUrl.Trim()
    if (-not [string]::IsNullOrWhiteSpace($envUrl)) {
        return $envUrl.TrimEnd("/")
    }

    return "https://eos-service.onrender.com"
}

function Resolve-AdminToken {
    param([string]$ConfiguredToken)

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredToken)) {
        return $ConfiguredToken.Trim()
    }

    $tokenFromEnv = [string]$env:GATE_ADMIN_TOKEN
    $tokenFromEnv = $tokenFromEnv.Trim()
    if (-not [string]::IsNullOrWhiteSpace($tokenFromEnv)) {
        return $tokenFromEnv
    }

    $tokenFromLegacyEnv = [string]$env:LV_GATE_ADMIN_TOKEN
    $tokenFromLegacyEnv = $tokenFromLegacyEnv.Trim()
    if (-not [string]::IsNullOrWhiteSpace($tokenFromLegacyEnv)) {
        return $tokenFromLegacyEnv
    }

    throw "Missing admin token. Pass -AdminToken or set GATE_ADMIN_TOKEN in your shell."
}

function Get-GameDir {
    param([string]$RepoRoot)
    return Join-Path $RepoRoot "LatticeVeilMonoGame"
}

function Get-DevDropDir {
    param([string]$RepoRoot)
    return Join-Path $RepoRoot "DEV"
}

function Get-ReleaseDropDir {
    param([string]$RepoRoot)
    return Join-Path $RepoRoot "RELEASE"
}

function Get-PublishDir {
    param([string]$RepoRoot)
    return Join-Path (Get-GameDir -RepoRoot $RepoRoot) "bin\Release\net8.0-windows\win-x64\publish"
}

function Get-ReleaseExePath {
    param([string]$RepoRoot)

    $releaseDropDir = Get-ReleaseDropDir -RepoRoot $RepoRoot
    if (Test-Path -LiteralPath $releaseDropDir) {
        $dropExe = Get-ChildItem -Path $releaseDropDir -Filter "*.exe" -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($dropExe) {
            return $dropExe.FullName
        }
    }

    $publishDir = Get-PublishDir -RepoRoot $RepoRoot
    if (-not (Test-Path -LiteralPath $publishDir)) {
        return $null
    }

    $preferred = @("LatticeVeilMonoGame.exe", "LatticeVeilGame.exe", "LatticeVeil.exe")
    foreach ($name in $preferred) {
        $candidate = Join-Path $publishDir $name
        if (Test-Path -LiteralPath $candidate) {
            return (Get-Item -LiteralPath $candidate).FullName
        }
    }

    $fallback = Get-ChildItem -Path $publishDir -Filter "*.exe" -File -ErrorAction SilentlyContinue |
        Sort-Object Length -Descending |
        Select-Object -First 1
    if ($fallback) {
        return $fallback.FullName
    }

    return $null
}

function Get-DebugExePath {
    param([string]$RepoRoot)

    $devDropDir = Get-DevDropDir -RepoRoot $RepoRoot
    if (Test-Path -LiteralPath $devDropDir) {
        $dropExe = Get-ChildItem -Path $devDropDir -Filter "*.exe" -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($dropExe) {
            return $dropExe.FullName
        }
    }

    $debugDir = Join-Path (Get-GameDir -RepoRoot $RepoRoot) "bin\Debug\net8.0-windows\win-x64"
    if (-not (Test-Path -LiteralPath $debugDir)) {
        return $null
    }

    $preferred = @("LatticeVeilMonoGame.exe", "LatticeVeilGame.exe", "LatticeVeil.exe")
    foreach ($name in $preferred) {
        $candidate = Join-Path $debugDir $name
        if (Test-Path -LiteralPath $candidate) {
            return (Get-Item -LiteralPath $candidate).FullName
        }
    }

    $fallback = Get-ChildItem -Path $debugDir -Filter "*.exe" -File -ErrorAction SilentlyContinue |
        Sort-Object Length -Descending |
        Select-Object -First 1
    if ($fallback) {
        return $fallback.FullName
    }

    return $null
}

function Get-ExeCandidates {
    param([string]$RepoRoot)

    $candidates = New-Object System.Collections.Generic.List[object]

    $releaseExe = Get-ReleaseExePath -RepoRoot $RepoRoot
    if (-not [string]::IsNullOrWhiteSpace($releaseExe)) {
        $item = Get-Item -LiteralPath $releaseExe
        $candidates.Add([PSCustomObject]@{
            Kind = "release"
            Path = $item.FullName
            LastWriteUtc = $item.LastWriteTimeUtc
        })
    }

    $debugExe = Get-DebugExePath -RepoRoot $RepoRoot
    if (-not [string]::IsNullOrWhiteSpace($debugExe)) {
        $item = Get-Item -LiteralPath $debugExe
        $candidates.Add([PSCustomObject]@{
            Kind = "debug"
            Path = $item.FullName
            LastWriteUtc = $item.LastWriteTimeUtc
        })
    }

    return $candidates
}

function Resolve-BuildType {
    param(
        [string]$RequestedBuildType,
        [string]$ConfiguredTarget
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedBuildType) -and -not $RequestedBuildType.Equals("latest", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $RequestedBuildType
    }

    $normalizedTarget = ([string]$ConfiguredTarget).Trim().ToLowerInvariant()
    switch ($normalizedTarget) {
        "dev" { return "debug" }
        "release" { return "release" }
        default { return "latest" }
    }
}

function Resolve-ExePath {
    param(
        [string]$ConfiguredPath,
        [string]$RepoRoot,
        [string]$RequestedBuildType
    )

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredPath)) {
        $full = [System.IO.Path]::GetFullPath($ConfiguredPath)
        if (-not (Test-Path -LiteralPath $full)) {
            throw "EXE path does not exist: $full"
        }

        return $full
    }

    $candidates = Get-ExeCandidates -RepoRoot $RepoRoot
    if ($candidates.Count -eq 0) {
        throw "No EXE candidates found. Build the game first."
    }

    $normalizedBuildType = if ([string]::IsNullOrWhiteSpace($RequestedBuildType)) {
        "latest"
    }
    else {
        $RequestedBuildType.ToLowerInvariant()
    }

    $filtered = switch ($normalizedBuildType) {
        "release" { $candidates | Where-Object { $_.Kind -eq "release" } }
        "debug" { $candidates | Where-Object { $_.Kind -eq "debug" } }
        default { $candidates }
    }

    if (-not $filtered -or $filtered.Count -eq 0) {
        throw "No $RequestedBuildType EXE candidates found. Build that configuration first."
    }

    return ($filtered | Sort-Object LastWriteUtc -Descending | Select-Object -First 1).Path
}

function Resolve-Target {
    param(
        [string]$ConfiguredTarget,
        [string]$ResolvedExePath
    )

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredTarget) -and -not $ConfiguredTarget.Equals("auto", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $ConfiguredTarget
    }

    $path = ([string]$ResolvedExePath).ToLowerInvariant()
    if ($path -match "[\\/](debug|dev)[\\/]") {
        return "dev"
    }

    if ($path -match "[\\/](release|publish)[\\/]") {
        return "release"
    }

    return "release"
}

$repoRoot = Resolve-RepoRoot
$gateBaseUrl = Resolve-GateUrl -ConfiguredUrl $GateUrl
$admin = Resolve-AdminToken -ConfiguredToken $AdminToken
$effectiveBuildType = Resolve-BuildType -RequestedBuildType $BuildType -ConfiguredTarget $Target
$resolvedExePath = Resolve-ExePath -ConfiguredPath $ExePath -RepoRoot $repoRoot -RequestedBuildType $effectiveBuildType
$resolvedTarget = Resolve-Target -ConfiguredTarget $Target -ResolvedExePath $resolvedExePath
$hash = (Get-FileHash -LiteralPath $resolvedExePath -Algorithm SHA256).Hash.ToLowerInvariant()

Write-Host "Repo root: $repoRoot"
Write-Host "Gate URL: $gateBaseUrl"
Write-Host "Build selection: $effectiveBuildType"
Write-Host "EXE path: $resolvedExePath"
Write-Host "Target: $resolvedTarget"
Write-Host "SHA256: $hash"

$payload = @{
    hash = $hash
    target = $resolvedTarget
    replaceTargetList = [bool]$ReplaceTargetList
    clearOtherHashes = [bool]$ClearOtherHashes
    applyMode = $ApplyMode
} | ConvertTo-Json

$headers = @{
    Authorization = "Bearer $admin"
}

$setHashUrl = "$gateBaseUrl/admin/allowlist/runtime/current-hash"
$result = $null
try {
    $result = Invoke-RestMethod `
        -Method Post `
        -Uri $setHashUrl `
        -Headers $headers `
        -ContentType "application/json" `
        -Body $payload
}
catch {
    throw "Gate API call failed: $($_.Exception.Message). Verify -GateUrl and -AdminToken."
}

if (-not $result.ok) {
    $reason = if ($null -ne $result -and $null -ne $result.reason -and -not [string]::IsNullOrWhiteSpace([string]$result.reason)) {
        [string]$result.reason
    }
    else {
        "unknown reason"
    }

    throw ("Gate rejected hash update: " + $reason)
}

Write-Host ""
Write-Host "Hash update accepted by gate."
$message = if ($null -ne $result -and $null -ne $result.message -and -not [string]::IsNullOrWhiteSpace([string]$result.message)) {
    [string]$result.message
}
else {
    "ok"
}
$runtimeHashCount = if ($null -ne $result -and $null -ne $result.runtime -and $null -ne $result.runtime.hashCount) {
    [string]$result.runtime.hashCount
}
else {
    "unknown"
}
$runtimeApplyMode = if ($null -ne $result -and $null -ne $result.runtime -and $null -ne $result.runtime.applyMode -and -not [string]::IsNullOrWhiteSpace([string]$result.runtime.applyMode)) {
    [string]$result.runtime.applyMode
}
else {
    "unknown"
}

Write-Host ("Message: " + $message)
Write-Host ("Runtime hash count: " + $runtimeHashCount)
Write-Host ("Runtime apply mode: " + $runtimeApplyMode)

try {
    Set-Clipboard -Value $hash
    Write-Host "SHA256 copied to clipboard."
}
catch {
    # Clipboard is best-effort only.
}

if ($ShowRuntime) {
    $runtimeUrl = "$gateBaseUrl/admin/allowlist/runtime"
    $runtime = Invoke-RestMethod -Method Get -Uri $runtimeUrl -Headers $headers
    Write-Host ""
    Write-Host "Runtime allowlist snapshot:"
    $runtime | ConvertTo-Json -Depth 8
}

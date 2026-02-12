param(
    [switch]$NoUi,
    [string]$GateUrl = "",
    [string]$AdminToken = "",
    [string]$ExePath = "",
    [ValidateSet("latest", "release", "debug")]
    [string]$BuildType = "latest",
    [ValidateSet("auto", "dev", "release")]
    [string]$Target = "auto"
)

# Simple live hash updater GUI for Render gate runtime allowlist.
# Security model:
# - No token is hardcoded.
# - No token is saved to disk.
# - The update call only works with a valid GATE_ADMIN_TOKEN.

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

[System.Windows.Forms.Application]::EnableVisualStyles()

$ErrorActionPreference = "Stop"
$defaultGateUrl = "https://eos-service.onrender.com"

function Resolve-RepoRoot {
    $current = [System.IO.DirectoryInfo]$PSScriptRoot
    while ($null -ne $current) {
        $projectPath = Join-Path $current.FullName "LatticeVeilMonoGame\LatticeVeilMonoGame.csproj"
        if (Test-Path -LiteralPath $projectPath) {
            return $current.FullName
        }

        $current = $current.Parent
    }

    return $null
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

    if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
        return ""
    }

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
        return ""
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

    return ""
}

function Get-DebugExePath {
    param([string]$RepoRoot)

    if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
        return ""
    }

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
        return ""
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

    return ""
}

function Get-LatestExe {
    param([string]$RepoRoot)

    $candidates = @()
    $releaseExe = Get-ReleaseExePath -RepoRoot $RepoRoot
    if (-not [string]::IsNullOrWhiteSpace($releaseExe)) {
        $releaseItem = Get-Item -LiteralPath $releaseExe
        $candidates += [PSCustomObject]@{
            Path = $releaseItem.FullName
            LastWriteUtc = $releaseItem.LastWriteTimeUtc
        }
    }

    $debugExe = Get-DebugExePath -RepoRoot $RepoRoot
    if (-not [string]::IsNullOrWhiteSpace($debugExe)) {
        $debugItem = Get-Item -LiteralPath $debugExe
        $candidates += [PSCustomObject]@{
            Path = $debugItem.FullName
            LastWriteUtc = $debugItem.LastWriteTimeUtc
        }
    }

    if (-not $candidates -or $candidates.Count -eq 0) {
        return ""
    }

    return ($candidates | Sort-Object LastWriteUtc -Descending | Select-Object -First 1).Path
}

function Resolve-EffectiveBuildType {
    param(
        [string]$RequestedBuildType,
        [string]$RequestedTarget
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedBuildType) -and -not $RequestedBuildType.Equals("latest", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $RequestedBuildType
    }

    $normalizedTarget = ([string]$RequestedTarget).Trim().ToLowerInvariant()
    switch ($normalizedTarget) {
        "dev" { return "debug" }
        "release" { return "release" }
        default { return "latest" }
    }
}

function Resolve-DefaultExePath {
    param(
        [string]$RepoRoot,
        [string]$RequestedBuildType
    )

    $effectiveBuildType = if ([string]::IsNullOrWhiteSpace($RequestedBuildType)) {
        "latest"
    }
    else {
        $RequestedBuildType.Trim().ToLowerInvariant()
    }
    switch ($effectiveBuildType) {
        "release" { return Get-ReleaseExePath -RepoRoot $RepoRoot }
        "debug" { return Get-DebugExePath -RepoRoot $RepoRoot }
        default { return Get-LatestExe -RepoRoot $RepoRoot }
    }
}

function Write-OutputLine {
    param(
        [System.Windows.Forms.TextBox]$OutputBox,
        [string]$Text
    )

    if ($OutputBox.TextLength -gt 0) {
        $OutputBox.AppendText([Environment]::NewLine)
    }

    $OutputBox.AppendText($Text)
    $OutputBox.SelectionStart = $OutputBox.TextLength
    $OutputBox.ScrollToCaret()
}

function Resolve-HashTarget {
    param(
        [string]$ResolvedExePath,
        [string]$TargetOverride
    )

    $normalizedOverride = ([string]$TargetOverride).Trim().ToLowerInvariant()
    if (-not [string]::IsNullOrWhiteSpace($normalizedOverride) -and $normalizedOverride -ne "auto") {
        return $normalizedOverride
    }

    $normalizedPath = ([string]$ResolvedExePath).ToLowerInvariant()
    if ($normalizedPath -match "[\\/](debug|dev)[\\/]") {
        return "dev"
    }

    if ($normalizedPath -match "[\\/](release|publish)[\\/]") {
        return "release"
    }

    return "release"
}

function Invoke-LiveHashReplace {
    param(
        [string]$GateUrl,
        [string]$AdminToken,
        [string]$ExePath,
        [string]$TargetOverride = "auto"
    )

    if ([string]::IsNullOrWhiteSpace($GateUrl)) {
        throw "Gate URL is empty."
    }

    if ([string]::IsNullOrWhiteSpace($AdminToken)) {
        throw "Admin token is required."
    }

    if ([string]::IsNullOrWhiteSpace($ExePath)) {
        throw "EXE path is required."
    }

    $resolvedExe = [System.IO.Path]::GetFullPath($ExePath)
    if (-not (Test-Path -LiteralPath $resolvedExe)) {
        throw "EXE path does not exist: $resolvedExe"
    }

    $hash = (Get-FileHash -LiteralPath $resolvedExe -Algorithm SHA256).Hash.ToLowerInvariant()
    $target = Resolve-HashTarget -ResolvedExePath $resolvedExe -TargetOverride $TargetOverride

    $payload = @{
        hash = $hash
        target = $target
        replaceTargetList = $true
        clearOtherHashes = $false
        applyMode = "replace_source"
    } | ConvertTo-Json

    $baseUrl = $GateUrl.Trim().TrimEnd("/")
    $url = "$baseUrl/admin/allowlist/runtime/current-hash"
    $headers = @{
        Authorization = "Bearer $AdminToken"
    }

    $result = $null
    try {
        $result = Invoke-RestMethod -Method Post -Uri $url -Headers $headers -ContentType "application/json" -Body $payload
    }
    catch {
        throw "Gate API call failed: $($_.Exception.Message). Check Gate URL and Admin Token."
    }
    if (-not $result.ok) {
        $reason = "unknown reason"
        if ($null -ne $result.reason -and -not [string]::IsNullOrWhiteSpace([string]$result.reason)) {
            $reason = [string]$result.reason
        }

        throw "Gate rejected hash update: $reason"
    }

    return [PSCustomObject]@{
        Hash = $hash
        Target = $target
        ExePath = $resolvedExe
        Message = [string]$result.message
        RuntimeHashCount = [string]$result.runtime.hashCount
        RuntimeApplyMode = [string]$result.runtime.applyMode
    }
}

$repoRoot = Resolve-RepoRoot
$effectiveBuildType = Resolve-EffectiveBuildType -RequestedBuildType $BuildType -RequestedTarget $Target
$detectedExe = Resolve-DefaultExePath -RepoRoot $repoRoot -RequestedBuildType $effectiveBuildType
$initialExe = ([string]$ExePath).Trim()
if ([string]::IsNullOrWhiteSpace($initialExe)) {
    $initialExe = $detectedExe
}
$initialGateUrl = ([string]$GateUrl).Trim()
if ([string]::IsNullOrWhiteSpace($initialGateUrl)) {
    $initialGateUrl = ([string]$env:LV_GATE_URL).Trim()
}
if ([string]::IsNullOrWhiteSpace($initialGateUrl)) {
    $initialGateUrl = $defaultGateUrl
}

$initialToken = ([string]$AdminToken).Trim()
if ([string]::IsNullOrWhiteSpace($initialToken)) {
    $initialToken = ([string]$env:GATE_ADMIN_TOKEN).Trim()
}

$initialTarget = ([string]$Target).Trim().ToLowerInvariant()
if ([string]::IsNullOrWhiteSpace($initialTarget)) {
    $initialTarget = "auto"
}

if ($NoUi) {
    $result = Invoke-LiveHashReplace -GateUrl $initialGateUrl -AdminToken $initialToken -ExePath $initialExe -TargetOverride $initialTarget
    Write-Output ("Build selection: " + $effectiveBuildType)
    Write-Output ("EXE: " + $result.ExePath)
    Write-Output ("Target: " + $result.Target)
    Write-Output ("SHA256: " + $result.Hash)
    Write-Output ("Result: " + $result.Message)
    Write-Output ("Runtime hash count: " + $result.RuntimeHashCount + " (mode=" + $result.RuntimeApplyMode + ")")
    exit 0
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "LatticeVeil Hash Updater"
$form.StartPosition = "CenterScreen"
$form.Size = New-Object System.Drawing.Size(880, 470)
$form.MinimumSize = New-Object System.Drawing.Size(880, 470)
$form.BackColor = [System.Drawing.Color]::FromArgb(16, 16, 16)
$form.ForeColor = [System.Drawing.Color]::White

$title = New-Object System.Windows.Forms.Label
$title.Text = "Live Render Hash Replace"
$title.Font = New-Object System.Drawing.Font("Segoe UI", 16, [System.Drawing.FontStyle]::Bold)
$title.AutoSize = $true
$title.Location = New-Object System.Drawing.Point(18, 14)
$title.ForeColor = [System.Drawing.Color]::FromArgb(235, 235, 235)
$form.Controls.Add($title)

$subtitle = New-Object System.Windows.Forms.Label
$subtitle.Text = "Select DEV or RELEASE explicitly, or use AUTO from EXE path. Token is never stored."
$subtitle.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$subtitle.AutoSize = $true
$subtitle.Location = New-Object System.Drawing.Point(20, 47)
$subtitle.ForeColor = [System.Drawing.Color]::FromArgb(170, 170, 170)
$form.Controls.Add($subtitle)

$lblGate = New-Object System.Windows.Forms.Label
$lblGate.Text = "Gate URL:"
$lblGate.AutoSize = $true
$lblGate.Location = New-Object System.Drawing.Point(20, 80)
$lblGate.ForeColor = [System.Drawing.Color]::FromArgb(200, 200, 200)
$form.Controls.Add($lblGate)

$lblGateValue = New-Object System.Windows.Forms.Label
$lblGateValue.Text = $initialGateUrl
$lblGateValue.AutoSize = $false
$lblGateValue.Location = New-Object System.Drawing.Point(108, 80)
$lblGateValue.Size = New-Object System.Drawing.Size(740, 20)
$lblGateValue.ForeColor = [System.Drawing.Color]::FromArgb(235, 235, 235)
$form.Controls.Add($lblGateValue)

$lblTarget = New-Object System.Windows.Forms.Label
$lblTarget.Text = "Target:"
$lblTarget.AutoSize = $true
$lblTarget.Location = New-Object System.Drawing.Point(20, 113)
$lblTarget.ForeColor = [System.Drawing.Color]::FromArgb(200, 200, 200)
$form.Controls.Add($lblTarget)

$cmbTarget = New-Object System.Windows.Forms.ComboBox
$cmbTarget.DropDownStyle = "DropDownList"
$cmbTarget.Location = New-Object System.Drawing.Point(108, 110)
$cmbTarget.Size = New-Object System.Drawing.Size(220, 24)
[void]$cmbTarget.Items.Add("AUTO (By EXE Path)")
[void]$cmbTarget.Items.Add("DEV")
[void]$cmbTarget.Items.Add("RELEASE")
switch ($initialTarget) {
    "dev" { $cmbTarget.SelectedIndex = 1 }
    "release" { $cmbTarget.SelectedIndex = 2 }
    default { $cmbTarget.SelectedIndex = 0 }
}
$cmbTarget.BackColor = [System.Drawing.Color]::FromArgb(24, 24, 24)
$cmbTarget.ForeColor = [System.Drawing.Color]::FromArgb(235, 235, 235)
$form.Controls.Add($cmbTarget)

$lblExe = New-Object System.Windows.Forms.Label
$lblExe.Text = "EXE Path:"
$lblExe.AutoSize = $true
$lblExe.Location = New-Object System.Drawing.Point(20, 146)
$lblExe.ForeColor = [System.Drawing.Color]::FromArgb(200, 200, 200)
$form.Controls.Add($lblExe)

$txtExe = New-Object System.Windows.Forms.TextBox
$txtExe.Text = $initialExe
$txtExe.Location = New-Object System.Drawing.Point(108, 143)
$txtExe.Size = New-Object System.Drawing.Size(640, 24)
$txtExe.BorderStyle = "FixedSingle"
$txtExe.BackColor = [System.Drawing.Color]::FromArgb(24, 24, 24)
$txtExe.ForeColor = [System.Drawing.Color]::FromArgb(235, 235, 235)
$form.Controls.Add($txtExe)

$btnBrowse = New-Object System.Windows.Forms.Button
$btnBrowse.Text = "Browse"
$btnBrowse.Location = New-Object System.Drawing.Point(756, 141)
$btnBrowse.Size = New-Object System.Drawing.Size(92, 27)
$btnBrowse.FlatStyle = "Flat"
$btnBrowse.FlatAppearance.BorderColor = [System.Drawing.Color]::FromArgb(70, 70, 70)
$btnBrowse.FlatAppearance.BorderSize = 1
$btnBrowse.BackColor = [System.Drawing.Color]::FromArgb(34, 34, 34)
$btnBrowse.ForeColor = [System.Drawing.Color]::White
$form.Controls.Add($btnBrowse)

$lblToken = New-Object System.Windows.Forms.Label
$lblToken.Text = "Admin Token:"
$lblToken.AutoSize = $true
$lblToken.Location = New-Object System.Drawing.Point(20, 182)
$lblToken.ForeColor = [System.Drawing.Color]::FromArgb(200, 200, 200)
$form.Controls.Add($lblToken)

$txtToken = New-Object System.Windows.Forms.TextBox
$txtToken.Text = $initialToken
$txtToken.UseSystemPasswordChar = $true
$txtToken.Location = New-Object System.Drawing.Point(108, 179)
$txtToken.Size = New-Object System.Drawing.Size(640, 24)
$txtToken.BorderStyle = "FixedSingle"
$txtToken.BackColor = [System.Drawing.Color]::FromArgb(24, 24, 24)
$txtToken.ForeColor = [System.Drawing.Color]::FromArgb(235, 235, 235)
$form.Controls.Add($txtToken)

$btnToggleToken = New-Object System.Windows.Forms.Button
$btnToggleToken.Text = "Show"
$btnToggleToken.Location = New-Object System.Drawing.Point(756, 177)
$btnToggleToken.Size = New-Object System.Drawing.Size(92, 27)
$btnToggleToken.FlatStyle = "Flat"
$btnToggleToken.FlatAppearance.BorderColor = [System.Drawing.Color]::FromArgb(70, 70, 70)
$btnToggleToken.FlatAppearance.BorderSize = 1
$btnToggleToken.BackColor = [System.Drawing.Color]::FromArgb(34, 34, 34)
$btnToggleToken.ForeColor = [System.Drawing.Color]::White
$form.Controls.Add($btnToggleToken)

$btnUpdate = New-Object System.Windows.Forms.Button
$btnUpdate.Text = "Replace Live Hash"
$btnUpdate.Location = New-Object System.Drawing.Point(20, 220)
$btnUpdate.Size = New-Object System.Drawing.Size(180, 34)
$btnUpdate.FlatStyle = "Flat"
$btnUpdate.FlatAppearance.BorderColor = [System.Drawing.Color]::FromArgb(80, 80, 80)
$btnUpdate.FlatAppearance.BorderSize = 1
$btnUpdate.BackColor = [System.Drawing.Color]::FromArgb(36, 72, 112)
$btnUpdate.ForeColor = [System.Drawing.Color]::White
$form.Controls.Add($btnUpdate)

$btnCopyHash = New-Object System.Windows.Forms.Button
$btnCopyHash.Text = "Copy Last Hash"
$btnCopyHash.Location = New-Object System.Drawing.Point(210, 220)
$btnCopyHash.Size = New-Object System.Drawing.Size(140, 34)
$btnCopyHash.FlatStyle = "Flat"
$btnCopyHash.FlatAppearance.BorderColor = [System.Drawing.Color]::FromArgb(80, 80, 80)
$btnCopyHash.FlatAppearance.BorderSize = 1
$btnCopyHash.BackColor = [System.Drawing.Color]::FromArgb(34, 34, 34)
$btnCopyHash.ForeColor = [System.Drawing.Color]::White
$btnCopyHash.Enabled = $false
$form.Controls.Add($btnCopyHash)

$btnOpenRender = New-Object System.Windows.Forms.Button
$btnOpenRender.Text = "Open Render URL"
$btnOpenRender.Location = New-Object System.Drawing.Point(360, 220)
$btnOpenRender.Size = New-Object System.Drawing.Size(140, 34)
$btnOpenRender.FlatStyle = "Flat"
$btnOpenRender.FlatAppearance.BorderColor = [System.Drawing.Color]::FromArgb(80, 80, 80)
$btnOpenRender.FlatAppearance.BorderSize = 1
$btnOpenRender.BackColor = [System.Drawing.Color]::FromArgb(34, 34, 34)
$btnOpenRender.ForeColor = [System.Drawing.Color]::White
$form.Controls.Add($btnOpenRender)

$output = New-Object System.Windows.Forms.TextBox
$output.Multiline = $true
$output.ReadOnly = $true
$output.ScrollBars = "Vertical"
$output.Location = New-Object System.Drawing.Point(20, 264)
$output.Size = New-Object System.Drawing.Size(828, 168)
$output.Font = New-Object System.Drawing.Font("Consolas", 9)
$output.BorderStyle = "FixedSingle"
$output.BackColor = [System.Drawing.Color]::FromArgb(20, 20, 20)
$output.ForeColor = [System.Drawing.Color]::FromArgb(220, 220, 220)
$form.Controls.Add($output)

$lastHash = ""
$lastAutoExePath = $initialExe
$exePathAutoManaged = $true

function Resolve-BuildTypeFromTargetLabel {
    param([string]$SelectedLabel)

    $normalized = if ([string]::IsNullOrWhiteSpace($SelectedLabel)) {
        ""
    }
    else {
        $SelectedLabel.Trim().ToUpperInvariant()
    }

    switch ($normalized) {
        "DEV" { return "debug" }
        "RELEASE" { return "release" }
        default { return "latest" }
    }
}

$btnBrowse.Add_Click({
    $dlg = New-Object System.Windows.Forms.OpenFileDialog
    $dlg.Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*"
    $dlg.Title = "Select LatticeVeilMonoGame.exe"
    if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $txtExe.Text = $dlg.FileName
        $exePathAutoManaged = $false
    }
    $dlg.Dispose()
})

$btnToggleToken.Add_Click({
    $txtToken.UseSystemPasswordChar = -not $txtToken.UseSystemPasswordChar
    $btnToggleToken.Text = if ($txtToken.UseSystemPasswordChar) { "Show" } else { "Hide" }
})

$btnOpenRender.Add_Click({
    $gate = [string]$initialGateUrl
    $gate = $gate.Trim()
    if ([string]::IsNullOrWhiteSpace($gate)) {
        $gate = $defaultGateUrl
    }

    try {
        Start-Process $gate | Out-Null
    }
    catch {
        Write-OutputLine -OutputBox $output -Text ("Failed to open URL: " + $_.Exception.Message)
    }
})

$btnCopyHash.Add_Click({
    if ([string]::IsNullOrWhiteSpace($lastHash)) {
        return
    }

    try {
        Set-Clipboard -Value $lastHash
        Write-OutputLine -OutputBox $output -Text "Copied hash to clipboard."
    }
    catch {
        Write-OutputLine -OutputBox $output -Text ("Clipboard copy failed: " + $_.Exception.Message)
    }
})

$cmbTarget.Add_SelectedIndexChanged({
    try {
        if (-not $exePathAutoManaged) {
            return
        }

        $selected = [string]$cmbTarget.SelectedItem
        $mappedBuildType = Resolve-BuildTypeFromTargetLabel -SelectedLabel $selected
        $resolved = Resolve-DefaultExePath -RepoRoot $repoRoot -RequestedBuildType $mappedBuildType
        if ([string]::IsNullOrWhiteSpace($resolved)) {
            return
        }

        $currentExe = ([string]$txtExe.Text).Trim()
        if ([string]::IsNullOrWhiteSpace($currentExe) -or $currentExe -eq $lastAutoExePath -or -not (Test-Path -LiteralPath $currentExe)) {
            $txtExe.Text = $resolved
            $lastAutoExePath = $resolved
        }
    }
    catch {
        # Keep UI responsive; best-effort only.
    }
})

$txtExe.Add_TextChanged({
    if ($txtExe.Focused) {
        $exePathAutoManaged = $false
    }
})

$btnUpdate.Add_Click({
    $btnUpdate.Enabled = $false
    try {
        $gate = [string]$initialGateUrl
        $gate = $gate.Trim()
        $token = [string]$txtToken.Text
        $token = $token.Trim()
        $exe = [string]$txtExe.Text
        $exe = $exe.Trim()
        $targetOverride = "auto"
        $selectedTargetLabel = [string]$cmbTarget.SelectedItem
        if ($selectedTargetLabel -eq "DEV") {
            $targetOverride = "dev"
        }
        elseif ($selectedTargetLabel -eq "RELEASE") {
            $targetOverride = "release"
        }

        Write-OutputLine -OutputBox $output -Text ("[" + (Get-Date -Format "HH:mm:ss") + "] Updating live hash (target=" + $targetOverride + ")...")

        $result = Invoke-LiveHashReplace -GateUrl $gate -AdminToken $token -ExePath $exe -TargetOverride $targetOverride
        $lastHash = $result.Hash
        $btnCopyHash.Enabled = $true

        Write-OutputLine -OutputBox $output -Text ("EXE: " + $result.ExePath)
        Write-OutputLine -OutputBox $output -Text ("Target: " + $result.Target)
        Write-OutputLine -OutputBox $output -Text ("SHA256: " + $result.Hash)
        Write-OutputLine -OutputBox $output -Text ("Result: " + $result.Message)
        Write-OutputLine -OutputBox $output -Text ("Runtime hash count: " + $result.RuntimeHashCount + " (mode=" + $result.RuntimeApplyMode + ")")

        try {
            Set-Clipboard -Value $result.Hash
            Write-OutputLine -OutputBox $output -Text "Hash copied to clipboard."
        }
        catch {
            Write-OutputLine -OutputBox $output -Text "Hash updated, but clipboard copy failed."
        }
    }
    catch {
        Write-OutputLine -OutputBox $output -Text ("ERROR: " + $_.Exception.Message)
    }
    finally {
        $btnUpdate.Enabled = $true
    }
})

Write-OutputLine -OutputBox $output -Text "Ready. This tool does not store your admin token."
Write-OutputLine -OutputBox $output -Text ("Build selection: " + $effectiveBuildType + " (same resolution rules as Build GUI).")
Write-OutputLine -OutputBox $output -Text "Update mode: target=auto|dev|release, replaceTargetList=true, clearOtherHashes=false."

[void]$form.ShowDialog()
$form.Dispose()

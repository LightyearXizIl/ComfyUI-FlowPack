[CmdletBinding()]
param([ValidateSet('Start', 'Stop')][string]$Mode = 'Start')
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$previewRoot = Join-Path $repoRoot 'artifacts\live-preview'
$statePath = Join-Path $previewRoot 'preview-process.json'
$logPath = Join-Path $previewRoot 'preview.log'
$buildRoot = Join-Path $previewRoot ('build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $previewRoot -Force | Out-Null
if ($Mode -eq 'Stop') {
    if (-not (Test-Path -LiteralPath $statePath)) { exit }
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $ownedProcess = Get-Process -Id $state.ProcessId -ErrorAction SilentlyContinue
    if ($ownedProcess -and $ownedProcess.StartTime.ToUniversalTime().Ticks.ToString() -eq $state.StartTicks) {
        $details = Get-CimInstance Win32_Process -Filter "ProcessId=$($state.ProcessId)"
        if ($details.CommandLine -and $details.CommandLine.Contains($PSCommandPath)) {
            if ($state.BuildRoot) {
                $ownedRoot = [IO.Path]::GetFullPath($state.BuildRoot)
                if ($ownedRoot.StartsWith($previewRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                    Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($ownedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and $_.Name -in 'ComfyUI.FlowPack.exe', 'ComfyUI.FlowPack.Worker.exe' } | ForEach-Object {
                        $current = Get-CimInstance Win32_Process -Filter "ProcessId=$($_.ProcessId)"
                        if ($current -and $current.CreationDate -eq $_.CreationDate -and $current.ExecutablePath -eq $_.ExecutablePath) { & taskkill.exe /PID $_.ProcessId /T /F 2>$null | Out-Null }
                    }
                }
            }
            & taskkill.exe /PID $state.ProcessId /T /F | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Could not stop the preview process.' }
            Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue
        }
    }
    exit
}
$hash = [Security.Cryptography.SHA256]::Create()
$key = [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($repoRoot))).Replace('-', '')
$mutex = [Threading.Mutex]::new($false, ('Local\FlowPackLivePreview-' + $key))
$ownsMutex = $false
try {
    try { $ownsMutex = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $ownsMutex = $true }
    if (-not $ownsMutex) { exit }
    @{ ProcessId = $PID; StartTicks = (Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks.ToString(); BuildRoot = $buildRoot } |
        ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
    Set-Location -LiteralPath $repoRoot
    # Poll only product sources, never output/data. Failed builds keep the last successful window.
    function Get-SourceFingerprint {
        $files = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Recurse -File |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Extension -in '.cs', '.xaml', '.csproj', '.props', '.targets', '.json' })
        $files += Get-ChildItem -LiteralPath $repoRoot -File | Where-Object { $_.Extension -in '.props', '.targets', '.sln' }
        return (($files | Sort-Object FullName | ForEach-Object { $_.FullName + ':' + $_.Length + ':' + $_.LastWriteTimeUtc.Ticks }) -join '|')
    }
    function Stop-OwnedPreviewChildren {
        # The executable must belong to this controller's unique build directory. Do not inspect secrets.
        $children = Get-CimInstance Win32_Process | Where-Object {
            $_.ExecutablePath -and $_.ExecutablePath.StartsWith($buildRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
            $_.Name -in 'ComfyUI.FlowPack.exe', 'ComfyUI.FlowPack.Worker.exe'
        }
        foreach ($child in $children) {
            $current = Get-CimInstance Win32_Process -Filter "ProcessId=$($child.ProcessId)"
            if ($current -and $current.CreationDate -eq $child.CreationDate -and $current.ExecutablePath -eq $child.ExecutablePath) {
                & taskkill.exe /PID $child.ProcessId /T /F 2>$null | Out-Null
            }
        }
    }
    $lastFingerprint = $null
    $cycle = 0
    while ($true) {
        $fingerprint = Get-SourceFingerprint
        if ($fingerprint -ne $lastFingerprint) {
            Start-Sleep -Milliseconds 800
            $fingerprint = Get-SourceFingerprint
            $lastFingerprint = $fingerprint
            $cycle++
            $appOutput = Join-Path $buildRoot ('preview-' + $cycle)
            ('[' + (Get-Date -Format 's') + '] Rebuilding App and Worker') | Add-Content -LiteralPath $logPath
            & dotnet publish src/FlowPack.App/FlowPack.App.csproj -c Debug --artifacts-path $buildRoot --self-contained false -o $appOutput *>> $logPath
            $appSucceeded = $LASTEXITCODE -eq 0
            if ($appSucceeded) {
                & dotnet publish src/FlowPack.Worker/FlowPack.Worker.csproj -c Debug --artifacts-path $buildRoot --self-contained false -o (Join-Path $appOutput 'worker') *>> $logPath
                if ($LASTEXITCODE -eq 0) {
                    Stop-OwnedPreviewChildren
                    $appExecutable = Join-Path $appOutput 'ComfyUI.FlowPack.exe'
                    # This is the visible application the user requested; the controller/Worker stay hidden.
                    $previewApp = Start-Process -FilePath $appExecutable -ArgumentList @('--portable-root', ('"' + $previewRoot + '"'), '--home') -PassThru
                    ('[' + (Get-Date -Format 's') + '] Preview ready; App PID=' + $previewApp.Id + '; cycle=' + $cycle) | Add-Content -LiteralPath $logPath
                }
            }
        }
        Start-Sleep -Milliseconds 750
    }
} catch {
    $_ | Out-String | Add-Content -LiteralPath $logPath
    Add-Type -AssemblyName PresentationFramework
    [Windows.MessageBox]::Show("Preview could not start. See: $logPath", 'FlowPack preview') | Out-Null
} finally {
    if ($ownsMutex) {
        Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue
        $mutex.ReleaseMutex()
    }
    $mutex.Dispose()
    $hash.Dispose()
}

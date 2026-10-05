#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScopePath, [Parameter(Mandatory)][ValidateSet('source','native','adopted')][string]$Instance,
      [int]$StopDesktopPid = 0)
$ErrorActionPreference = 'Stop'
$scope = Get-Content -LiteralPath $ScopePath -Raw | ConvertFrom-Json
$root = [IO.Path]::GetFullPath($scope.FixtureRoot)
$allowed = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts\acceptance')) + '\'
if (-not $root.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or
    -not ([IO.Path]::GetFullPath($scope.DesktopProfile)).StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid isolated scope.' }
if ($StopDesktopPid) {
    $owned = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $StopDesktopPid)
    if ($owned) {
        if ($owned.ExecutablePath -ne $scope.DesktopExecutable -or -not $owned.CommandLine.Contains($scope.DesktopProfile)) { throw 'The process does not belong to this scope.' }
        $queue = Invoke-RestMethod http://127.0.0.1:8188/queue -TimeoutSec 5
        if ($queue.queue_running.Count -or $queue.queue_pending.Count) { throw 'Wait for the isolated queue before stopping Desktop.' }
        (Get-Process -Id $StopDesktopPid).Kill($true)
    }
}
$id = switch ($Instance) { source {$scope.SourceInstanceId}; native {$scope.NativeTargetInstanceId}; adopted {$scope.AdoptedTargetInstanceId} }
$data = switch ($Instance) { source {$scope.SourceData}; native {$scope.NativeData}; adopted {$scope.AdoptedData} }
$settingsPath = Join-Path $scope.DesktopProfile 'settings.json'
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$settings.autoLaunchOnStartup = $id
[IO.File]::WriteAllText($settingsPath,($settings | ConvertTo-Json -Depth 8))
$run = Join-Path $root ('desktop-launch\' + $Instance + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$environmentRoot = Join-Path $root 'process-environment'
$scopedEnvironment = @{ APPDATA = Join-Path $environmentRoot 'Roaming'; LOCALAPPDATA = Join-Path $environmentRoot 'Local' }
foreach ($directory in $scopedEnvironment.Values) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
$launchArgs = @('--user-data-dir="'+$scope.DesktopProfile+'"','--force-renderer-accessibility')
$process = Start-Process -FilePath $scope.DesktopExecutable -ArgumentList $launchArgs -WindowStyle Hidden -PassThru -Environment $scopedEnvironment -RedirectStandardOutput (Join-Path $run 'stdout.log') -RedirectStandardError (Join-Path $run 'stderr.log')
$metadata = @{ desktopPid=$process.Id; executable=$scope.DesktopExecutable; productVersion=(Get-Item -LiteralPath $scope.DesktopExecutable).VersionInfo.ProductVersion;
    executableSha256=(Get-FileHash -LiteralPath $scope.DesktopExecutable).Hash; desktopProfile=$scope.DesktopProfile; processEnvironment=$scopedEnvironment; instanceId=$id; data=$data; startedUtc=(Get-Date).ToUniversalTime().ToString('O') }
[IO.File]::WriteAllText((Join-Path $run 'launch.json'),($metadata | ConvertTo-Json -Depth 8))
$deadline = (Get-Date).AddSeconds(45)
do {
    if ($process.HasExited) { throw 'Desktop exited before startup.' }
    try {
        $listener = Get-NetTCPConnection -LocalPort 8188 -State Listen -ErrorAction Stop | Select-Object -First 1
        $runtime = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $listener.OwningProcess)
        if (-not $runtime.CommandLine.Contains($data)) { throw 'The listener is not the isolated instance.' }
        $info = Invoke-RestMethod http://127.0.0.1:8188/object_info/FlowPackAcceptancePass -TimeoutSec 2
        if ($info.FlowPackAcceptancePass) {
            $metadata.runtimeProcess = @{pid=$runtime.ProcessId;parentPid=$runtime.ParentProcessId;executable=$runtime.ExecutablePath;commandLine=$runtime.CommandLine}
            $metadata.ready = $true
            [IO.File]::WriteAllText((Join-Path $run 'launch.json'),($metadata | ConvertTo-Json -Depth 8))
            Write-Output ($metadata | ConvertTo-Json -Depth 8 -Compress)
            return
        }
    } catch { if ($_ -like '*not the isolated instance*') { throw } }
    Start-Sleep -Milliseconds 500
} while ((Get-Date) -lt $deadline)
throw ('Isolated Desktop did not become ready. Evidence: ' + $run)

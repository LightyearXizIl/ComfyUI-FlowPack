[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScopePath, [Parameter(Mandatory)][string]$CompletedRun,
      [ValidateSet('native','adopted','both')][string]$Target = 'both')
$ErrorActionPreference = 'Stop'
$scope = Get-Content -LiteralPath $ScopePath -Raw | ConvertFrom-Json
$root = [IO.Path]::GetFullPath($scope.FixtureRoot)
$allowed = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\artifacts\acceptance')) + '\'
if (-not $root.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)) { throw 'Not an isolated acceptance root.' }
$evidencePath = [IO.Path]::GetFullPath($CompletedRun)
if (-not $evidencePath.StartsWith($root+'\transfer-runs\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Run outside this fixture.' }
$evidence = Get-Content -LiteralPath (Join-Path $evidencePath 'result.json') -Raw | ConvertFrom-Json
if (-not $evidence.passed) { throw 'Retain incomplete runs without resetting them.' }
$targets = @(@{name='native';data=$scope.NativeData;python=$scope.NativePython},@{name='adopted';data=$scope.AdoptedData;python=$scope.AdoptedPython})
if ($Target -ne 'both') { $targets = @($targets | Where-Object { $_.name -eq $Target }) }
$files = @()
foreach ($targetEntry in $targets) {
    $data = [IO.Path]::GetFullPath($targetEntry.data)
    $python = [IO.Path]::GetFullPath($targetEntry.python)
    if (-not $data.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase) -or
        -not $python.StartsWith($data+'\.venv\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Target not isolated.' }
    $active = Get-CimInstance Win32_Process -Filter "name = 'python.exe'" | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($data)}
    if ($active) { throw 'Stop this isolated instance before reset.' }
    foreach ($file in $evidence.exportFiles) {
        $relative = if ($file.ArchivePath.StartsWith('workflows/')) { 'user/default/' + $file.ArchivePath } else { $file.ArchivePath }
        $path = [IO.Path]::GetFullPath((Join-Path $data $relative))
        if (-not $path.StartsWith($data+'\',[StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $path)) { throw 'Unexpected target file.' }
        if ((Get-FileHash -LiteralPath $path).Hash -ne $file.Sha256) { throw ('Target changed: '+$path) }
        $files += @{path=$path;relative=$relative;target=$targetEntry.name;sha256=$file.Sha256}
    }
    $version = & $python -I -c "import importlib.metadata as m; print(m.version('humanize'))"
    if ($LASTEXITCODE -ne 0 -or $version.Trim() -ne '4.16.0') { throw 'The installed test dependency changed.' }
}
$backup = Join-Path $root ('reset-runs\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
foreach ($file in $files) {
    $destination = [IO.Path]::GetFullPath((Join-Path $backup (Join-Path $file.target $file.relative)))
    if (-not $destination.StartsWith($backup+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Backup path escaped.' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Move-Item -LiteralPath $file.path -Destination $destination
}
foreach ($targetEntry in $targets) {
    $before = & $targetEntry.python -I -c "import importlib.metadata as m,json;print(json.dumps({d.metadata['Name'].lower():d.version for d in m.distributions()}))"
    [IO.File]::WriteAllText((Join-Path $backup ($targetEntry.name+'-python-before.json')), $before)
    & $targetEntry.python -I -m pip --isolated uninstall -y humanize *> (Join-Path $backup ($targetEntry.name+'-uninstall.log'))
    if ($LASTEXITCODE -ne 0) { throw 'Isolated uninstall failed; retained evidence requires review.' }
    $after = & $targetEntry.python -I -c "import importlib.metadata as m,json;print(json.dumps({d.metadata['Name'].lower():d.version for d in m.distributions()}))"
    [IO.File]::WriteAllText((Join-Path $backup ($targetEntry.name+'-python-after.json')), $after)
    $first = $before | ConvertFrom-Json -AsHashtable
    $last = $after | ConvertFrom-Json -AsHashtable
    if ($last.ContainsKey('humanize')) { throw 'The test dependency was not removed.' }
    foreach ($key in $first.Keys | Where-Object {$_ -ne 'humanize'}) { if ($last[$key] -ne $first[$key]) { throw 'An existing Python package changed.' } }
}
[IO.File]::WriteAllText((Join-Path $backup 'reset.json'),(@{completedEvidence=$evidencePath;preservedFiles=$files;scope=$root;purpose='fresh production binary replay';onlyNewTestDependencyRemoved='humanize==4.16.0'} | ConvertTo-Json -Depth 8))
Write-Output $backup

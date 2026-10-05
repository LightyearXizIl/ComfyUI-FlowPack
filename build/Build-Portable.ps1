[CmdletBinding()]
param([string]$Name = ('FlowPack-Portable-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($Name -notmatch '^[A-Za-z0-9_-]+$') { throw 'Name must contain only letters, numbers, hyphens and underscores.' }
$outputRoot = Join-Path $repoRoot 'artifacts\portable'
$packageRoot = Join-Path $outputRoot $Name
if (Test-Path -LiteralPath $packageRoot) { throw 'Output already exists. Use a new name; existing portable data is never overwritten.' }
$appOutput = Join-Path $packageRoot 'App'
$buildRoot = Join-Path $outputRoot ('build-' + $Name)
New-Item -ItemType Directory -Path $appOutput -Force | Out-Null
Push-Location $repoRoot
try {
    & dotnet restore FlowPack.sln --locked-mode --artifacts-path $buildRoot
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & dotnet publish src/FlowPack.App/FlowPack.App.csproj -c Release -r win-x64 --self-contained true --no-restore --artifacts-path $buildRoot -o $appOutput -p:PublishTrimmed=false -p:PublishAot=false
    if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }
    & dotnet publish src/FlowPack.Worker/FlowPack.Worker.csproj -c Release -r win-x64 --self-contained true --no-restore --artifacts-path $buildRoot -o (Join-Path $appOutput 'worker') -p:PublishTrimmed=false -p:PublishAot=false
    if ($LASTEXITCODE -ne 0) { throw 'Worker publish failed.' }
} finally { Pop-Location }
[IO.File]::WriteAllText((Join-Path $appOutput 'portable.mode'), '')
[IO.File]::WriteAllText((Join-Path $packageRoot 'Open-FlowPack.cmd'), "@echo off`r`nstart `"`" `"%~dp0App\ComfyUI.FlowPack.exe`" --home`r`n", [Text.Encoding]::ASCII)
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\PORTABLE_PREVIEW.md') -Destination (Join-Path $packageRoot 'README.txt')
$zipPath = Join-Path $outputRoot ($Name + '.zip')
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($packageRoot, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $true)
[IO.File]::WriteAllText(($zipPath + '.sha256'), (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash + ' *' + [IO.Path]::GetFileName($zipPath))
Write-Output "PortableFolder=$packageRoot"
Write-Output "PortableZip=$zipPath"

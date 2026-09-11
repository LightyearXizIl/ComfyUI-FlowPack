[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.[0-9]\.[0-9]$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$declaredVersion = (Get-Content -Raw (Join-Path $repoRoot 'VERSION')).Trim()
if ($Version -ne $declaredVersion) {
    throw "Requested version $Version does not match VERSION ($declaredVersion)."
}

function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
}

function Reset-OutputDirectory([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $expectedPrefix = $repoRoot.TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset an output path outside the repository: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resolved -Force | Out-Null
}

$publishRoot = Join-Path $repoRoot 'artifacts\publish'
$installerRoot = Join-Path $repoRoot 'artifacts\installer'
$appOutput = Join-Path $publishRoot 'app'
$workerOutput = Join-Path $publishRoot 'worker'
Reset-OutputDirectory $publishRoot
Reset-OutputDirectory $installerRoot

$iconPath = Join-Path $repoRoot 'src\FlowPack.App\Assets\FlowPack.ico'
if (-not (Test-Path -LiteralPath $iconPath)) {
    & (Join-Path $PSScriptRoot 'Convert-LogoToIcon.ps1') | Out-Host
}

Push-Location $repoRoot
try {
    Invoke-Checked 'dotnet' @('restore', '.\FlowPack.sln', '--locked-mode')
    Invoke-Checked 'dotnet' @('test', '.\FlowPack.sln', '-c', 'Release', '--no-restore', '--logger', 'trx;LogFileName=release.trx', '--results-directory', ".\artifacts\acceptance\release-$Version\local")
    Invoke-Checked 'dotnet' @('publish', '.\src\FlowPack.App\FlowPack.App.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore', '-o', $appOutput, '-p:PublishTrimmed=false', '-p:PublishAot=false', "-p:Version=$Version", "-p:FileVersion=$Version.0", "-p:InformationalVersion=$Version")
    Invoke-Checked 'dotnet' @('publish', '.\src\FlowPack.Worker\FlowPack.Worker.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore', '-o', $workerOutput, '-p:PublishTrimmed=false', '-p:PublishAot=false', "-p:Version=$Version", "-p:FileVersion=$Version.0", "-p:InformationalVersion=$Version")
}
finally {
    Pop-Location
}

$appExe = Join-Path $appOutput 'ComfyUI.FlowPack.exe'
$workerExe = Join-Path $workerOutput 'ComfyUI.FlowPack.Worker.exe'
foreach ($requiredFile in @($appExe, $workerExe)) {
    if (-not (Test-Path -LiteralPath $requiredFile)) {
        throw "Required published executable is missing: $requiredFile"
    }
}

$appVersion = (Get-Item -LiteralPath $appExe).VersionInfo.ProductVersion
if (-not $appVersion.StartsWith($Version, [StringComparison]::Ordinal)) {
    throw "Published application version is $appVersion; expected $Version."
}

$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$iscc = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) {
    $isccCommand = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($isccCommand) { $iscc = $isccCommand.Source }
}
if (-not $iscc) {
    throw 'Inno Setup 6 was not found. Install it before building the installer.'
}

$installerScript = Join-Path $repoRoot 'installer\FlowPack.iss'
Invoke-Checked $iscc @("/DMyAppVersion=$Version", $installerScript)

$installerPath = Join-Path $installerRoot "ComfyUI-FlowPack-$Version-Setup.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Installer was not generated: $installerPath"
}

$checksumLines = @(
    "{0} *{1}" -f (Get-FileHash -Algorithm SHA256 $installerPath).Hash, (Split-Path -Leaf $installerPath),
    "{0} *{1}" -f (Get-FileHash -Algorithm SHA256 $appExe).Hash, (Split-Path -Leaf $appExe),
    "{0} *worker/{1}" -f (Get-FileHash -Algorithm SHA256 $workerExe).Hash, (Split-Path -Leaf $workerExe)
)
[IO.File]::WriteAllLines((Join-Path $installerRoot 'SHA256SUMS.txt'), $checksumLines, [Text.UTF8Encoding]::new($false))

Write-Output "Installer=$installerPath"
Write-Output "Version=$appVersion"
Write-Output "SHA256=$((Get-FileHash -Algorithm SHA256 $installerPath).Hash)"

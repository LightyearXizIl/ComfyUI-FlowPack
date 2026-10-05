[CmdletBinding()]
param(
    [string]$RunId = ([Guid]::NewGuid().ToString('N')),
    [string]$CoreSource = 'E:\Comfy-Desktop\ComfyUI-Installs\ComfyUI\ComfyUI',
    [string]$PythonEnvironmentSource = 'C:\Users\16054\Documents\ComfyUI\.venv',
    [string]$DesktopExecutable = 'E:\Software\ComfyUI\ComfyUI\Comfy Desktop\Comfy Desktop.exe'
)
$ErrorActionPreference = 'Stop'
if ($RunId -notmatch '^[a-fA-F0-9]{32}$') { throw 'RunId must be a fresh 32-character GUID.' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$acceptanceParent = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts\acceptance\simplify-20261005'))
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $acceptanceParent ('runtime-' + $RunId)))
if (-not $fixtureRoot.StartsWith($acceptanceParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture root escaped the acceptance parent.' }
if (Test-Path -LiteralPath $fixtureRoot) { throw 'This fixture already exists. Use a fresh RunId; prior evidence and resources are never replaced.' }
$oldSource = Join-Path $repoRoot 'artifacts\acceptance\core-implementation\official-core-4cc5e414738f431388fcc8aa50e5f158\source\ComfyUI'
foreach ($required in @((Join-Path $CoreSource 'main.py'), (Join-Path $CoreSource 'comfyui_version.py'), (Join-Path $PythonEnvironmentSource 'Scripts\python.exe'), $DesktopExecutable,
    (Join-Path $oldSource 'models\upscale_models\RealESRGAN_x2plus.pth'), (Join-Path $oldSource 'input\fixture.png'))) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required source asset is missing: $required" }
}
if ((Get-Content -LiteralPath (Join-Path $CoreSource 'comfyui_version.py') -Raw) -notmatch '__version__\s*=\s*"0\.38\.0"') { throw 'This acceptance fixture requires the verified Core 0.38.0 source.' }
if ((Get-Item -LiteralPath $DesktopExecutable).VersionInfo.ProductVersion -ne '1.1.4.0') { throw 'This acceptance fixture requires Desktop 1.1.4.0.' }
if (Get-ChildItem -LiteralPath (Join-Path $PythonEnvironmentSource 'Lib\site-packages') -Filter 'humanize*dist-info') { throw 'The chosen dependency must be absent from the copied baseline.' }
foreach ($source in @($CoreSource, $PythonEnvironmentSource)) {
    if ((Get-Item -LiteralPath $source -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Source is a reparse point: $source" }
    if (Get-ChildItem -LiteralPath $source -Recurse -Force -Attributes ReparsePoint -ErrorAction Stop | Select-Object -First 1) { throw "Source contains a reparse point; copies must not absorb unrelated paths: $source" }
}
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
$logs = Join-Path $fixtureRoot 'preparation-logs'
New-Item -ItemType Directory -Path $logs | Out-Null
function Copy-Tree([string]$Source, [string]$Destination, [string]$LogName, [string[]]$Exclude = @()) {
    if (-not ([IO.Path]::GetFullPath($Destination)).StartsWith($fixtureRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Copy target is outside the new fixture.' }
    $copyArgs = @($Source, $Destination, '/E', '/COPY:DAT', '/DCOPY:DAT', '/XJ', '/R:1', '/W:1', '/MT:16', '/NFL', '/NDL', '/NP', ('/LOG:' + (Join-Path $logs $LogName)))
    if ($Exclude.Count -gt 0) { $copyArgs += '/XD'; $copyArgs += $Exclude }
    & robocopy @copyArgs | Out-Null
    if ($LASTEXITCODE -gt 7) { throw "Asset copy failed; retain the new fixture and log for investigation: $LogName ($LASTEXITCODE)" }
}
$sourceCore = Join-Path $fixtureRoot 'source\ComfyUI'
$nativeCore = Join-Path $fixtureRoot 'native-target\ComfyUI'
$adoptedCore = Join-Path $fixtureRoot 'adopted-core\ComfyUI'
$adoptedData = Join-Path $fixtureRoot 'adopted-data'
$exclude = @('models','input','output','user','custom_nodes','temp','.git','.venv','.comfy-downloads','__pycache__') | ForEach-Object { Join-Path $CoreSource $_ }
foreach ($entry in @(@{ Name = 'source'; Core = $sourceCore; Data = $sourceCore }, @{ Name = 'native'; Core = $nativeCore; Data = $nativeCore }, @{ Name = 'adopted'; Core = $adoptedCore; Data = $adoptedData })) {
    Write-Output "Preparing $($entry.Name): isolated Core and Python copy"
    Copy-Tree $CoreSource $entry.Core ($entry.Name + '-core-copy.log') $exclude
    Copy-Tree $PythonEnvironmentSource (Join-Path $entry.Data '.venv') ($entry.Name + '-python-copy.log')
    foreach ($directory in @('models','input','output','custom_nodes','user\default\workflows','temp')) { New-Item -ItemType Directory -Path (Join-Path $entry.Data $directory) -Force | Out-Null }
}
$sourceId = 'flowpack-source-' + $RunId.Substring(0,8)
$nativeId = 'flowpack-native-' + $RunId.Substring(0,8)
$adoptedId = 'flowpack-adopted-' + $RunId.Substring(0,8)
foreach ($marker in @(@{ Root = (Split-Path -Parent $sourceCore); Id = $sourceId }, @{ Root = (Split-Path -Parent $nativeCore); Id = $nativeId }, @{ Root = (Split-Path -Parent $adoptedCore); Id = $adoptedId })) {
    [IO.File]::WriteAllText((Join-Path $marker.Root '.comfyui-desktop-2'), $marker.Id)
}
$profile = Join-Path $fixtureRoot 'desktop-profile'
New-Item -ItemType Directory -Path $profile | Out-Null
foreach ($directory in @('unused-shared\models','unused-shared\input','unused-shared\output','download-cache')) { New-Item -ItemType Directory -Path (Join-Path $fixtureRoot $directory) -Force | Out-Null }
$records = @(
    @{ id=$sourceId; name='FlowPack acceptance source'; sourceId='standalone'; installPath=(Split-Path -Parent $sourceCore); adopted=$false; status='installed'; useSharedModels=$false; useSharedInput=$false; useSharedOutput=$false; autoUpdateComfyUI=$false; launchArgs='--cpu --listen 127.0.0.1 --disable-auto-launch'; launchMode='window'; portConflict='auto'; inputDir=(Join-Path $sourceCore 'input'); outputDir=(Join-Path $sourceCore 'output') },
    @{ id=$nativeId; name='FlowPack acceptance native target'; sourceId='standalone'; installPath=(Split-Path -Parent $nativeCore); adopted=$false; status='installed'; useSharedModels=$false; useSharedInput=$false; useSharedOutput=$false; autoUpdateComfyUI=$false; launchArgs='--cpu --listen 127.0.0.1 --disable-auto-launch'; launchMode='window'; portConflict='auto'; inputDir=(Join-Path $nativeCore 'input'); outputDir=(Join-Path $nativeCore 'output') },
    @{ id=$adoptedId; name='FlowPack acceptance adopted target'; sourceId='standalone'; installPath=(Split-Path -Parent $adoptedCore); adopted=$true; adoptedBaseDir=$adoptedData; adoptedPythonPath=(Join-Path $adoptedData '.venv\Scripts\python.exe'); status='installed'; useSharedModels=$false; useSharedInput=$false; useSharedOutput=$false; autoUpdateComfyUI=$false; launchArgs='--cpu --listen 127.0.0.1 --disable-auto-launch'; launchMode='window'; portConflict='auto'; inputDir=(Join-Path $adoptedData 'input'); outputDir=(Join-Path $adoptedData 'output') }
)
[IO.File]::WriteAllText((Join-Path $profile 'installations.json'), ($records | ConvertTo-Json -Depth 8))
[IO.File]::WriteAllText((Join-Path $profile 'settings.json'), (@{ modelsDirs=@((Join-Path $fixtureRoot 'unused-shared\models')); inputDir=(Join-Path $fixtureRoot 'unused-shared\input'); outputDir=(Join-Path $fixtureRoot 'unused-shared\output'); installDir=$fixtureRoot; cacheDir=(Join-Path $fixtureRoot 'download-cache'); onAppClose='quit'; telemetryEnabled=$false; firstUseCompleted=$true; autoInstallUpdates=$false; autoLaunchOnStartup='none' } | ConvertTo-Json -Depth 5))
[IO.File]::WriteAllText((Join-Path $profile 'data-location.json'), '{"mode":"local-appdata"}')
$modelPath = Join-Path $sourceCore 'models\upscale_models\RealESRGAN_x2plus.pth'
New-Item -ItemType Directory -Path (Split-Path -Parent $modelPath) -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $oldSource 'models\upscale_models\RealESRGAN_x2plus.pth') -Destination $modelPath
Copy-Item -LiteralPath (Join-Path $oldSource 'input\fixture.png') -Destination (Join-Path $sourceCore 'input\fixture.png')
$nodeRoot = Join-Path $sourceCore 'custom_nodes\FlowPackAcceptanceNode'
New-Item -ItemType Directory -Path $nodeRoot | Out-Null
$nodeCode = @'
import json
import sys
from pathlib import Path
import torch
import folder_paths

class FlowPackAcceptanceFixture:
    @classmethod
    def INPUT_TYPES(cls): return {"required": {}}
    RETURN_TYPES = ("IMAGE",)
    FUNCTION = "run"
    CATEGORY = "FlowPack acceptance"
    def run(self): return (torch.full((1, 8, 8, 3), 0.25, dtype=torch.float32),)

class FlowPackAcceptancePass:
    @classmethod
    def INPUT_TYPES(cls): return {"required": {"image": ("IMAGE",)}}
    RETURN_TYPES = ("IMAGE",)
    FUNCTION = "run"
    CATEGORY = "FlowPack acceptance"
    def run(self, image):
        import humanize
        import importlib.metadata
        value = humanize.intcomma(12345)
        assert value == "12,345"
        proof = {"dependency": "humanize", "version": importlib.metadata.version("humanize"), "result": value,
                 "pythonPrefix": sys.prefix, "packageFile": humanize.__file__}
        (Path(folder_paths.get_output_directory()) / "dependency-proof.json").write_text(json.dumps(proof), encoding="utf-8")
        print("FLOWPACK_ACCEPTANCE_HUMANIZE " + value)
        return (image,)

NODE_CLASS_MAPPINGS = {"FlowPackAcceptanceFixture": FlowPackAcceptanceFixture, "FlowPackAcceptancePass": FlowPackAcceptancePass}
'@
[IO.File]::WriteAllText((Join-Path $nodeRoot '__init__.py'), $nodeCode)
[IO.File]::WriteAllText((Join-Path $nodeRoot 'requirements.txt'), "humanize==4.16.0`n")
$workflowPath = Join-Path $sourceCore 'user\default\workflows\upscale.json'
$workflow = Get-Content -LiteralPath (Join-Path $oldSource 'user\default\workflows\upscale.json') -Raw | ConvertFrom-Json -AsHashtable
$workflow['2'] = @{ class_type='FlowPackAcceptanceFixture'; inputs=@{} }
[IO.File]::WriteAllText($workflowPath, ($workflow | ConvertTo-Json -Depth 12))
$dependencyRoot = Join-Path $fixtureRoot 'dependency-source'
New-Item -ItemType Directory -Path $dependencyRoot | Out-Null
$wheelName = 'humanize-4.16.0-py3-none-any.whl'
$wheelPath = Join-Path $dependencyRoot $wheelName
$wheelUrl = 'https://files.pythonhosted.org/packages/b0/aa/0b7365d30fed43e7a3449aba1fe20a0a7174d9cf13e282af4e69ac825441/humanize-4.16.0-py3-none-any.whl'
$wheelSha = '353eb2f34c09d098b2880eee8bef21832eae6d174f48c5762fff7e5fcb74d01d'
Invoke-WebRequest -Uri $wheelUrl -OutFile $wheelPath
if ((Get-FileHash -LiteralPath $wheelPath -Algorithm SHA256).Hash -ne $wheelSha) { throw 'The pinned PyPI wheel hash does not match.' }
$externalDataRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('ComfyUI FlowPack\Acceptance\simplify-' + $RunId.Substring(0,8))
if (Test-Path -LiteralPath $externalDataRoot) { throw 'External empty scope root already exists; use a fresh RunId.' }
New-Item -ItemType Directory -Path $externalDataRoot | Out-Null
$scope = @{
    FixtureRoot=$fixtureRoot; DesktopProfile=$profile; ExternalDataRoot=$externalDataRoot;
    InstanceIds=@($sourceId,$nativeId,$adoptedId); SourceInstanceId=$sourceId; NativeTargetInstanceId=$nativeId; AdoptedTargetInstanceId=$adoptedId;
    SourceCore=$sourceCore; SourceData=$sourceCore; NativeCore=$nativeCore; NativeData=$nativeCore; AdoptedCore=$adoptedCore; AdoptedData=$adoptedData;
    SourcePython=(Join-Path $sourceCore '.venv\Scripts\python.exe'); NativePython=(Join-Path $nativeCore '.venv\Scripts\python.exe'); AdoptedPython=(Join-Path $adoptedData '.venv\Scripts\python.exe');
    WorkflowPath=$workflowPath; SourceInputPath=(Join-Path $sourceCore 'input\fixture.png'); ModelPath=$modelPath; ModelSha256=(Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash; NodePackageSource=$nodeRoot;
    DependencyName='humanize'; DependencyVersion='4.16.0'; DependencyWheel=$wheelPath; DependencyWheelSha256=$wheelSha; DependencySource='https://pypi.org/project/humanize/4.16.0/';
    CoreVersion='0.38.0'; DesktopVersion='1.1.4.0'; DesktopExecutable=$DesktopExecutable; PreparationOnly=$true;
    PythonBaseline='All three copied environments lack humanize. Install the verified wheel only into source before its initial inference; targets stay untouched until FlowPack installs.';
    LibraryRoot=(Join-Path $fixtureRoot 'FlowPack'); ProductionProfilePath=(Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Comfy Desktop')
}
$productionRoot = $scope.ProductionProfilePath
$scope.ProductionConfigurationHashes = @{}
foreach ($file in @('installations.json','settings.json')) { $scope.ProductionConfigurationHashes[$file] = (Get-FileHash -LiteralPath (Join-Path $productionRoot $file) -Algorithm SHA256).Hash }
$scopePath = Join-Path $fixtureRoot 'prepared-scope.json'
[IO.File]::WriteAllText($scopePath, ($scope | ConvertTo-Json -Depth 10))
[IO.File]::WriteAllText((Join-Path $fixtureRoot 'PREPARATION.txt'), "Fresh isolated resource copies. No Desktop or Worker started; no package installed; no deployment qualification granted. Core 0.38.0, Desktop 1.1.4.0. Use prepared-scope.json and retain logs.`n")
Write-Output "PreparedRoot=$fixtureRoot"
Write-Output "PreparedScope=$scopePath"

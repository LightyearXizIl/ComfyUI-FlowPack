# FlowPack 开发指南

二级 UI 便携验收：`pwsh -NoProfile -File build/Verify-PortableTransfer.ps1 -AppExe <解压后的绝对 EXE 路径> -Scope <已准备的独立验收范围 JSON> -RunId <新的 32 位十六进制 ID> -SecondaryDetailsOnly`。仅检查页面与菜单，克隆独立 Desktop 配置，不执行安装，也不启动 Desktop。该参数与 `-Install` 互斥。详细输入、结果与边界见 [二级界面记录](SECONDARY_UI_2026-10-05.md)。

本指南依据 2026-10-05 工作区脚本，适用于 Windows 本地开发。日常修改使用隔离构建或源码预览；便携测试包和正式安装器是不同交付物。项目分层、数据流与安装门禁见 [架构说明](ARCHITECTURE.md)，本轮界面与真实安装证据见 [界面简化记录](UI_SIMPLIFICATION_2026-10-05.md)、[Desktop 1.1.4 验收记录](DESKTOP_1_1_4_TRANSFER_ACCEPTANCE.md)，最终结果以 [项目状态](PROJECT_STATUS.md)补录为准。

## 环境和入口

- Windows x64；App、Worker、Tests、Smoke 的目标框架为 `net10.0-windows`，App 使用 WPF。
- [global.json](../global.json) 指定 .NET SDK `10.0.112`、`rollForward: latestPatch`、不接受预览版。可使用对应功能带内适配的更新补丁；不是任意 .NET 10 SDK 都符合此选择规则。
- PowerShell。只有生成正式安装器时需要 Inno Setup 6；便携包不需要 Inno Setup。
- NuGet 包版本集中在 [Directory.Packages.props](../Directory.Packages.props)，各项目提交 `packages.lock.json`。常规还原使用 `--locked-mode`，不要为了绕过失败删除锁文件。
- 源码版本由 [VERSION](../VERSION) 与 [Directory.Build.props](../Directory.Build.props) 管理，当前为 `0.0.6`。界面测试改动不自动代表新版本已发布。

从项目根目录运行以下命令。示例路径是本工作区，其他电脑按实际克隆目录修改。

```powershell
Set-Location -LiteralPath 'E:\Vibe coding\ComfyUI FlowPack'
dotnet --version
```

## 当前流程与 App/Worker 契约

普通导出只有一份 `IsSelected` 最终清单；旧草稿的 `IsReference` 字段继续兼容，但不是第二份页面选择。进入导出时自动补充能唯一确认的工作流依赖，同一选择范围内每个工作流只补一次；取消依赖后，更新计划或返回不会恢复勾选，显式“重新添加依赖”才补选。导入选择来源后自动分析并生成计划。导入、导出的异步结果必须核对输入版本，来源、实例或选择变化后不能用旧结果启用执行按钮。

当前 Worker 协议为 **6**，App/Worker 必须成对构建和打包。安装计划的可空 `RequiresPythonDependencies` 由 Worker 依据声明、`requirements.txt`、`pyproject.toml` 计算，界面与执行能力检查共用；执行前重新核验实际需求与内置资格，旧计划缺少该字段时拒绝安装并要求重新生成。

App 支持 `--desktop-profile <绝对目录>` 并传给 Worker；目录须存在并包含有效登记，显式配置的发现和关联不回落用户默认配置。同库 Worker 会话绑定规范化配置路径，不同配置拒绝连接；旧会话缺字段只接受默认配置。隔离验收应同时使用独立 FlowPack 资源库；该参数仅限定发现范围，不授予文件或 Python 安装资格。

## 隔离还原、构建与测试

下面把中间产物放到专用目录，避免正在运行的预览或旧输出占用默认 `bin/obj`。**restore、build、test 必须使用同一个 `--artifacts-path`**，并保持构建配置一致。

```powershell
$repoRoot = (Get-Location).Path
$devArtifacts = Join-Path $repoRoot 'artifacts\acceptance\dev-build'
$devResults = Join-Path $repoRoot ('artifacts\acceptance\dev-tests-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
dotnet restore .\FlowPack.sln --locked-mode --artifacts-path $devArtifacts
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
dotnet build .\FlowPack.sln -c Release --no-restore --artifacts-path $devArtifacts
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
dotnet test .\FlowPack.sln -c Release --no-build --no-restore --artifacts-path $devArtifacts --logger 'trx;LogFileName=dev.trx' --results-directory $devResults
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
```

源码改变后先重新 build，再使用 `--no-build` 测试。针对资源转移修改，可在上述构建后运行下列筛选；需要完整回归时仍运行全量测试。

```powershell
dotnet test .\src\FlowPack.Tests\FlowPack.Tests.csproj -c Release --no-build --no-restore --artifacts-path $devArtifacts --filter 'FullyQualifiedName~ResourceTransferTests'
```

隔离编译目录不是 App 的普通 Worker 查找路径。需要交互调试时优先使用下面的预览入口，它会把匹配的 App 和 Worker 放在一起；不要只启动一份随手找到的 App 输出。

## 真实 WPF 源码预览

双击根目录 [实时预览.cmd](../实时预览.cmd) 启动，[停止实时预览.cmd](../停止实时预览.cmd) 停止。也可以在终端前台运行控制器，观察其生命周期：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\Live-Preview.ps1 -Mode Start
```

停止时在另一个 PowerShell 窗口执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\Live-Preview.ps1 -Mode Stop
```

[Live-Preview.ps1](../build/Live-Preview.ps1) 监视源码文件变化，重建 App 和 Worker；两者都成功后才替换当前预览窗口。它使用独立构建目录，并通过便携工作区启动首页。具体限制如下：

- 这是编译后重启真实 WPF 窗口，保存源码后不会在原窗口内热替换控件；当前页面及未保存操作可能重置。
- 重启会中断这份预览中的运行任务。测试大文件导出、下载、取消和恢复时，使用固定便携包，或停止修改源码。
- 构建失败保留上一次成功窗口。查看 `artifacts/live-preview/preview.log`，修正并再次保存源码后触发新一轮构建。
- 数据保存在 `artifacts/live-preview/Data`。仅关闭窗口不会停止监视器；使用停止入口结束它管理的进程，Data 会保留。
- 文档 Markdown 不在监视范围内。预览入口不会进入外发便携 ZIP；外部测试电脑不需要源码或 SDK。

## 生成便携测试包

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\Build-Portable.ps1
```

[Build-Portable.ps1](../build/Build-Portable.ps1) 默认使用时间生成新名称，执行锁定还原，分别自包含发布 App、Worker，再生成 ZIP 和 SHA-256 文件。输出在 `artifacts/portable`；也可传入新的 `-Name`，仅接受英文字母、数字、连字符和下划线。已有同名目录会拒绝覆盖。

该脚本**不运行测试**。打包前应完成与修改相适应的测试，打包后还需从 ZIP 重新完整解压，双击 `Open-FlowPack.cmd` 验证实际启动。包内包含 .NET 运行时，首次运行才在便携根目录创建 Data。便携包固定为打包时的代码，不随源码更新。详细使用说明见 [便携测试与实时预览](PORTABLE_PREVIEW.md)。

## 正式安装器与远端发布

[Build-Release.ps1](../build/Build-Release.ps1) 开始时会清空仓库内的 `artifacts/publish` 和 `artifacts/installer`，然后还原、测试、自包含发布、调用 Inno Setup 并生成校验文件。需要保留的旧产物应事先另存；日常界面测试使用便携打包流程。

仅在明确需要生成正式安装器时执行：

```powershell
$releaseVersion = (Get-Content -Raw -LiteralPath .\VERSION).Trim()
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build\Build-Release.ps1 -Version $releaseVersion
```

版本参数必须与 `VERSION` 一致。成功结果位于 `artifacts/installer`，包括安装器和 `SHA256SUMS.txt`；本地脚本不会安装软件、推送代码或创建 GitHub Release。

远端发布由 [.github/workflows/release.yml](../.github/workflows/release.yml) 在版本标签推送时触发，常规路径会构建并创建 GitHub Release。存在 `release-notes/<tag>.archive.json` 时采用旧二进制归档分支，跳过常规重建发布步骤。不要为了测试构建推送版本标签。版本和历史归档约定见 [VERSIONING.md](../VERSIONING.md)。

## 五项按需启用的专项测试

以下五项默认按条件跳过，四种开关中 Python 两项共用一个。只在专门配置的隔离环境中启用；路径不得指向用户生产 Desktop 数据或需要保留的 Python 环境。开启开关不等于准备工作已经完成。

| 专项与测试源码 | 启用条件和必需配置 | 验证范围 |
|---|---|---|
| [DesktopRuntimeAcceptanceTests](../src/FlowPack.Tests/DesktopRuntimeAcceptanceTests.cs) | `FLOWPACK_TEST_DESKTOP_RUNTIME=1`；需 `FLOWPACK_DESKTOP_TEST_PROFILE`、`FLOWPACK_DESKTOP_TEST_INSTANCE`、`FLOWPACK_DESKTOP_TEST_PROMPT`。跨目录测试用 `FLOWPACK_DESKTOP_TEST_SCOPE` 明确范围；`FLOWPACK_DESKTOP_TEST_SCENARIO` 可为 `zip-roundtrip` 或 `downloaded-model`。 | 读取由隔离 Desktop GUI 启动的实例及其已完成提示历史，检查实际输出；不会替你准备或提交这次 GUI 生成。 |
| [OfficialCoreRoundtripTests](../src/FlowPack.Tests/OfficialCoreRoundtripTests.cs) | `FLOWPACK_TEST_OFFICIAL_CORE=1`；需 `FLOWPACK_TEST_CORE`、`FLOWPACK_TEST_PYTHON_ENV`、`FLOWPACK_TEST_MODEL`、`FLOWPACK_OFFICIAL_TEST_ROOT`。 | 复制两个隔离 Core 和 Python 环境，执行资源转移与实际推理；不等同 Desktop 安装器验收。 |
| [PythonEnvironmentIntegrationTests](../src/FlowPack.Tests/PythonEnvironmentIntegrationTests.cs) | 设置 `FLOWPACK_TEST_PYTHON_BASE` 为基础 Python 可执行文件，并设置 `FLOWPACK_OFFICIAL_TEST_ROOT`。 | 创建一次性 venv，从 PyPI 下载经核对的 wheel，验证安装及已有版本保护。 |
| [PythonInterruptedInstallIntegrationTests](../src/FlowPack.Tests/PythonInterruptedInstallIntegrationTests.cs) | 同上，共用 `PythonEnvironmentFact` 条件。 | 新建 venv 和本地测试 wheel，验证真实 pip 中断及 Worker 重启后的待修复状态。 |
| [Zip64RoundtripTests](../src/FlowPack.Tests/Zip64RoundtripTests.cs) | `FLOWPACK_TEST_ZIP64=1`；需 `FLOWPACK_LARGE_TEST_ROOT`，源码要求测试盘剩余空间大于 13 GiB。 | 创建实际超过 4 GiB 的资源与 ZIP，校验导出和导入哈希；会清理本次夹具目录。 |

专项测试应先阅读对应源码与隔离范围校验，再设置进程级环境变量并使用测试类筛选。不要在日常终端长期留下这些开关，以免后续全量测试或安装器构建意外启动耗时专项。

## 常见排错

| 现象 | 检查位置与处理 |
|---|---|
| SDK 无法解析 | 核对 `dotnet --version` 与 `global.json`；安装所需 SDK，不随意改版本约束。 |
| 锁定还原失败 | 核对网络、包源和锁文件差异；确需改依赖时一并审查中央版本和生成的锁文件。 |
| 输出文件被占用 | 使用新的隔离构建路径；预览通过专用停止入口关闭，不按进程名批量结束所有 FlowPack 或 ComfyUI。 |
| 未找到或无法连接 Worker | 核对包内 `App/worker` 是否完整且与 App 同版；检查日志和资源库路径，不删除数据库或锁文件来强行绕过在用进程。 |
| 保存后界面没变 | 查看 `artifacts/live-preview/preview.log` 的构建结果；失败时看到的是上一版窗口。普通便携包本来就不会随源码刷新。 |
| 能导入但不能安装 | 查看“需要处理”、摘要详情和 [部署能力](../src/FlowPack.Infrastructure/DeploymentCapability.cs)。当前内置资格仅含归一化 Desktop `1.1.4` 的原生/接管两布局及文件、Python 能力，EvidenceId 为 `desktop-1.1.4-transfer-20261005`，见 [验收证据](DESKTOP_1_1_4_TRANSFER_ACCEPTANCE.md)；其他版本、未知布局或旧计划仍会阻断，不通过测试开关绕过。 |

## 文档和验证记录维护

用户可见行为变化时同步 [使用指南](USER_GUIDE.md)；构建、预览或打包脚本变化时更新本文；契约和存储变化时更新 [架构说明](ARCHITECTURE.md)。设计规则更新到 [DESIGN.md](../DESIGN.md)，当前成果和待办更新到 [项目状态](PROJECT_STATUS.md)，交接入口保留在 [HANDOFF.md](../HANDOFF.md)。

测试记录应写明实际命令、通过/失败/跳过数量、产物路径和未执行范围。分别记录源码测试、打包、解压启动、真实 Desktop 安装/推理与远端发布结果，不互相替代。只改文档时核对链接、命令与脚本并检查差异即可，不需要为此重跑整套软件测试。

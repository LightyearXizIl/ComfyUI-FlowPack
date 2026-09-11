# ComfyUI FlowPack 开发交接

最后更新：2026-09-11
仓库：[LightyearXizIl/ComfyUI-FlowPack](https://github.com/LightyearXizIl/ComfyUI-FlowPack)
主分支：`main`
当前版本：`0.0.2`

## 1. 先读结论

当前仓库是 **0.0.2 工程预览版**，不是已完成真实 ComfyUI 环境部署验证的完整产品。

- M0 本机工程出口已完成：WPF 壳层、九个独立页面、导航和空状态修复、测试与安装器构建链路均已有证据。
- 资源库 SQLite 持久化、主题偏好、格式 1 清单、`.cpack` 导入/不完整工作流包导出、原始工作流保留、Worker IPC、经 SHA-256 校验的 staging 下载、任务记录、列表筛选和脱敏诊断摘要已完成本机自动化验证。
- 真实 ComfyUI Desktop 绑定、部署、恢复、完整离线包、试运行、维护、安装生命周期和兼容矩阵仍待实机验证；这仍然**不代表 M10 完整验收通过**。
- 禁止用模拟连接、模拟资源、模拟任务或编造进度把未实现能力展示为已完成。

完整范围、安全边界和阶段出口以 [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md) 为准；需求追踪以 [`REQUIREMENTS_ACCEPTANCE.md`](REQUIREMENTS_ACCEPTANCE.md) 为准。

## 2. Git 与发布状态

| 项目 | 当前事实 |
| --- | --- |
| `v0.0.2` 源码提交 | `4e3a95cd5ebea5b5edff8b4806dbffa435369264` |
| GitHub Actions | [运行 34579299090](https://github.com/LightyearXizIl/ComfyUI-FlowPack/actions/runs/34579299090)，结论为 `success` |
| GitHub Release | [`v0.0.2`](https://github.com/LightyearXizIl/ComfyUI-FlowPack/releases/tag/v0.0.2)，非草稿、非预发布 |
| 远程安装包 | [ComfyUI-FlowPack-0.0.2-Setup.exe](https://github.com/LightyearXizIl/ComfyUI-FlowPack/releases/download/v0.0.2/ComfyUI-FlowPack-0.0.2-Setup.exe) |
| 远程大小与 SHA-256 | 74,284,159 字节；`DD4498F0938290AC4F9E5DD41DED367D12E51FC39B0712F73372F521ED2A75A3` |
| 本地安装包 | `artifacts/installer/ComfyUI-FlowPack-0.0.2-Setup.exe` |
| 本地大小与 SHA-256 | 74,275,707 字节；`E34524FBD4CDE48C64AE82A754408566FA20717A5BB10FDB9CFF901FE8197317` |
| 代码签名 | `NotSigned`，不能声称已签名 |

本地构建已通过锁定还原、Debug/Release 各 53 项测试、自包含发布和安装器编译。远程资产已核对 GitHub 服务器摘要与 `SHA256SUMS.txt` 一致，但本机没有重新执行安装、启动、卸载或完整下载远程安装包，因此不能登记“安装生命周期”或“远程下载后本机安装”通过。两份安装包来自不同构建，大小和哈希不同是已记录事实，不能把本地哈希用于验证远程文件。

详细证据见 [`docs/RELEASE_0.0.2.md`](docs/RELEASE_0.0.2.md)。`v0.0.2` 已正式发布，不要移动或重写该标签；后续文档和代码应通过新提交推进。

## 3. 当前工程结构

| 模块 | 当前职责与状态 |
| --- | --- |
| `src/FlowPack.App` | WPF 桌面壳层、导航、主题切换、清单文件选择与会话内展示 |
| `src/FlowPack.App/Views` | 九个独立页面 View，由单一页面宿主切换 |
| `src/FlowPack.Core` | 少量契约、主题定义和安全安装规划骨架；尚未冻结格式 1 |
| `src/FlowPack.Infrastructure` | 清单读取和资源库路径校验骨架；尚无 SQLite 持久化 |
| `src/FlowPack.ComfyUI` | 仅有早期 `ComfyUiInspector` 骨架，不能用于真实写入决策 |
| `src/FlowPack.Worker` | 只输出一行就绪信息；没有 IPC、任务状态机或执行能力 |
| `src/FlowPack.Tests` | 当前单元/UI 壳层测试 |
| `src/FlowPack.Smoke` | 当前基础 Smoke 入口 |

界面基准见 [`FRONTEND_REFERENCE.md`](FRONTEND_REFERENCE.md)。当前 Logo 唯一源文件是 `LOGO 图标.png`，安装与程序图标使用由它生成的 `src/FlowPack.App/Assets/FlowPack.ico`。

## 4. 已完成范围

- 九个页面已从同一长页面拆为独立 View，并由 `ContentControl` 承载。
- 顶部主导航按内容区真实居中；960、1280、1600 DIP 已做回归。
- 修复按钮模板 Padding、焦点和禁用状态。
- 正式启动为空资源、空任务和“尚未检查环境”，不预置虚假连接或进度。
- `.NET SDK 10.0.112`、中央 NuGet 版本和各项目 `packages.lock.json` 已锁定。
- Solution 已覆盖 Debug/Release 的 App、Core、Infrastructure、ComfyUI、Worker、Tests 和 Smoke。
- 本机 Debug/Release 各 53 项测试通过；Worker IPC 和 WPF Shell 冒烟通过。真实 Desktop 与安装生命周期仍未复验。
- Windows x64 自包含发布、Inno Setup 安装器和 GitHub Release 工作流已建立。

## 5. 不能越过的安全边界

1. `ShellViewModel.StartInstallCommand` 当前故意禁用。真实检查、计划摘要、用户确认和 Worker 复检完成前，不得直接启用安装按钮。
2. 当前 `ComfyUiInspector` 会递归抓取第一个 `python*.exe` 并猜测 `user` 目录。实施 M2 时必须按真实 Desktop 版本、配置来源和只读证据重建，不能把这段骨架接入写操作。
3. App 只负责交互与显示；所有资源库和 ComfyUI 写操作必须由 Worker 执行，并经过持久化计划、实例锁、指纹复检、日志和恢复机制。
4. UI 断开不能等同于取消任务；后续 Worker 必须支持重连和任务恢复。
5. 任何安装、升级、卸载和重装都必须保留用户资源、数据库与主题；没有真实保留测试前不能声称通过 M10。
6. 当前没有签名证书，也没有仓库许可证文件。代码签名和开源许可证都需要明确决策，不能自行声明。
7. 当前 Inno Setup 安装界面使用官方英文语言文件，应用界面仍为中文。这是已知限制，不应写成简体中文安装器。

## 6. 下一阶段：实机 M2–M10 验证

下一位开发者应先在隔离的官方 ComfyUI Desktop 实例上完成只读识别和绑定，再进入部署验证。当前代码的本机功能不能替代真实写入、恢复或试运行证据。

建议按以下顺序推进：

1. 准备与日常环境隔离的官方 Desktop、资源库和恢复基线。
2. 执行只读发现、手选确认、实例指纹及路径保护验证；未知环境不得开启安装。
3. 验证 staging 下载、任务中断恢复和完整 `.cpack` 在隔离环境中的真实行为。
4. 仅在通过目标实例、资源、恢复与试运行矩阵后，才实现并启用部署写入。

阶段表在 [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md#13-分阶段执行顺序依赖与阶段出口)。其中 M5–M7 才覆盖真实部署、资源包导出和试运行，M8 覆盖完整九页与主题，M9 是综合验收，M10 是最终交付验收。

## 7. 本地验证与构建

在仓库根目录使用 PowerShell：

```powershell
dotnet restore .\FlowPack.sln --locked-mode
dotnet test .\FlowPack.sln -c Debug --no-restore
dotnet test .\FlowPack.sln -c Release --no-restore
.\build\Build-Release.ps1 -Version 0.0.2
```

生成安装器需要 Windows x64、.NET SDK 10.0.112 和 Inno Setup 6。构建脚本只会重建仓库内的 `artifacts/publish/` 与 `artifacts/installer/`；输出目录受仓库路径校验保护。安装器位于 `artifacts/installer/`。

发布前还需要做真实安装生命周期检查，不能只以 `dotnet test` 或安装器编译成功代替安装、启动、卸载和数据保留证据。

## 8. 版本与后续发布

版本采用三段式十进制满 10 进 1：`0.0.9` 的下一版是 `0.1.0`，`0.9.9` 的下一版是 `1.0.0`。详见 [`VERSIONING.md`](VERSIONING.md)。

后续正式发布至少需要：

1. 先完成目标阶段的代码、测试和验收证据。
2. 同步 `VERSION`、项目版本、`CHANGELOG.md`、发布说明和本交接文档。
3. 运行锁定还原、Debug/Release 测试、发布构建与隔离安装生命周期检查。
4. 提交后只创建一次对应版本标签并推送；已发布标签不得移动。
5. 等待目标标签的 GitHub Actions 完成，核对 Release 状态、资产大小、服务器摘要和校验清单。
6. 如果用户需要“远程下载可安装”的证据，还必须实际下载远程资产并重新执行本机安装验证。

现有工作流使用 `gh release create` 创建新 Release；对同一标签直接重跑可能遇到 Release 已存在的问题。需要重跑发布时先审查工作流行为，不要删除正式 Release 或移动标签来绕过失败。

## 9. 开发与验收约定

- 每个已确认缺陷都应增加回归覆盖。
- 对真实 Desktop、Python、GPU、模型库或节点行为没有证据时，明确标记“待验证”，不要猜测路径或兼容性。
- 阶段证据放到 `artifacts/acceptance/<阶段>/<运行标识>/`，记录源码标识、环境、测试结果、截图、脱敏日志和关键哈希。
- `artifacts/` 当前被 Git 忽略；需要长期保留的结论应整理进 `docs/`，大体积证据不要直接提交。
- 任何发布结论都要区分本地构建、CI 成功、远程资产存在、远程下载复验和真实设备验收，不能互相替代。
- 处理现有工作区时保留用户未提交修改，禁止用重置或覆盖清理无关改动。

## 10. 交接检查清单

- [ ] 阅读 `IMPLEMENTATION_PLAN.md`、`REQUIREMENTS_ACCEPTANCE.md` 和本文件。
- [ ] 运行 `git status`，确认没有覆盖前任或用户的未提交修改。
- [ ] 运行锁定还原和 Debug/Release 测试，记录实际通过数量。
- [ ] 仅在有真实证据时更新阶段状态与验收结论。
- [ ] 完成 M1 后更新本文件的当前提交、已完成范围、未完成范围和下一步。
- [ ] 发版时同步版本、变更记录、发布说明、交接文档和远程验证结果。

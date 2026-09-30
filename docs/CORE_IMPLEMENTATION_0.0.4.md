# 0.0.4 核心实施与本地验收记录

2026-09-12 最新状态：用户确认“安装”后，隔离页面实际提交被 Worker 以“安装计划不是本 Worker 生成的原始计划”拒绝；四个目标载荷均未写入。计划比较已改为 JSON 语义一致性校验，仍要求 Worker 留存的完整计划及当前部署资格；新增格式差异/内容篡改/版本变化回归，Release 285通过、4专项跳过（artifacts/acceptance/core-completion-audit/plan-equality-regression.trx）。修正版隔离宿主已启动，但用户随后按 Esc 停止界面操作，尚未重新安装。需要用户明确恢复操作后继续真实闭环。264E…安装器不包含此次比较修正，未重新打包，生产安装资格仍为空。下文相关“当前源码已全部打包”“安装尚未执行/待首次确认”为历史状态。

日期：2026-09-12。源码为本地候选；未提交、推送、打标签或发布。最新已发布版本仍是 0.0.3。

## 结论

本地哈希进度新增LocalHashProgress，MatchAsync按131072字节缓冲流式核对，读取前/中上报当前文件进度，取消传播且释放源句柄；Worker resource.match-local更新Stage/CompletedBytes/TotalBytes，普通更新间隔至少400ms，文件开始/结束立即更新。512KiB正常/中途取消两条回归通过，完整284/4，最终专项4通过；不是大模型真实UI验收，721B…安装器未包含。

导出最新修复：PlannedZipExportService以ownsTemporary标志区分本次创建与已有暂存，创建失败不清理他者文件；PersistentWorker export.execute比较已保存export.plan的完整JSON，仅一致时执行。PlannedZipExportRecoveryTests4场景、Worker导出未改/篡改2场景通过，完整282/4，diff检查通过。源码追加，当前721B…安装器未重建。

哈希复用增量：OnlineLocalHashMatcher接收原清单声明，多个模型候选按大小+SHA-256读取匹配，返回路径与证据而不是安装资源；持久Worker resource.match-local从保留计划取声明，后续仍materialize-local。App保留先前类别/路径过滤，结果应用前核对实例和计划身份。新增两条理论回归、Worker命令测试；常规276/4。测试使用合成文件，不是实际模型/共享目录UI闭环。

预览一致性修正：资源变动使_deploymentInputRevision递增；PrepareDeploymentAsync在两个异步阶段后核对revision/实例引用/importId，过期结果丢弃并清除其依赖显示。执行期间积累选择变动在持久保存后重新规划。ImportInstanceRefreshTests加入真实NamedPipe计划响应延迟，取消选择后旧计划返回仍保持_deployment为空。专项1通过，完整274/4，diff检查通过；不是实际Desktop GUI验收。

导入选择/映射持久化增量：会话兼容可选ResourceChoices，旧会话缺失字段继续保持未选中；保存字段不包含安装计划/资格/确认状态。Worker按原计划资源ID和SourcePath验证，目标可保存未完成输入但安装仍需原有路径检查与确认。App勾选和目标修改自动保存、恢复修改目标为NeedsConfirmation；NamedPipe+Worker+新VM回归通过。常规274/4，专项19/19，最后加强选中恢复断言1/1；当前721B…安装器未更新。

来源恢复最新核验：online.source幂等重试只接受已完成修改的当前派生链，切换无关导入后重试明确冲突且不改会话；新增Worker重建验证链接与其他下载关联保留、关联下载换源被拒绝。Worker专项18通过；完整常规274通过/4专项跳过；无真实网络下载/图形安装，当前安装器未重建。

来源补全新增：OnlineSourceEditing + Worker online.source + 导入页SaveOnlineSourceCommand已连接。Worker事务保存resource.source计划及会话，保留来源声明大小/哈希、资源身份和目标；新计划参与FindOnlinePlan/ContinuesImportPlan，旧版本计划不改写。源URL编辑拒绝已关联下载和过期Revision；HTTPS/网页/固定提交规则有回归，HTTP内容校验仍在下载阶段执行。常规273通过/4专项跳过；来源详情展开后的实际WPF夹具截图在 artifacts/acceptance/online-source-ui，浅深已目视检查。当前安装器未包含本次更新，真实页面下载尚未验证。

最新增量：PythonDependencyService.RunAsync 补上预取消检查和取消后的进程退出/输出等待；PythonProcessCancellationTests 使用独立测试进程验证，不调用pip、不触碰真实Python环境。专项2/2、常规264通过/4专项跳过。实际安装中断、后代进程全部退出和环境修复仍需单独验收；当前安装器未包含本次追加。

本轮新增了八项核心需求的主要服务和页面操作，但完整计划尚未验收完成。生产 `PersistentWorkerService`/Worker 程序没有开启安装写入权限；不能通过修改一个布尔值跳过隔离 Desktop 资格验证。

已分别验证：普通资源 ZIP 往返、真实超过 4 GiB ZIP64、两份隔离官方 ComfyUI 核心上包含实际 RealESRGAN 模型和自定义节点的运行、独立 Python 环境兼容 wheel 安装并阻止替换已有版本。Desktop GUI、FlowPack 安装器生命周期和全部异常/视觉矩阵仍待验收。

## 实现范围

- `DesktopInstanceDiscovery`：现代实例登记、接管关系、显式路径参数、额外 YAML、模型搜索/写入路径、Python、指纹、多实例选择；云端不进入本地安装目标；手动关联未登记目录仅只读，保存后重新解析。
- `ResourceInventoryService` / `InventoryDependencyAnalyzer`：实际文件索引；UI/API 工作流、节点类型声明、包身份和版本、模型类别和相对引用、可选指定 SHA-256、明确输入素材；未知与歧义显示，不通过文件夹存在判定节点满足。静态声明匹配不声称运行加载已验证。
- `ResourceImportService` / `PlannedZipExportService`：第三方包装目录、节点仓库、工作流和模型混合包；普通 ZIP、ZIP64、原始 JSON、依赖去重、清单哈希、源变化阻断；不靠本软件清单才能识别。旧 `.cpack` 校验读取和旧在线清单记录/下载入口保留。
- `PersistentWorkerService` / `WorkerLibraryClient`：协议 v3、schema v7 迁移备份、业务数据库 Worker 写入、持久任务、请求幂等、并发下载上限 3、暂停/继续/取消/重试、断线重连。旧任务没有执行计划则标记重新检查，不继承写权限。
- `ResourceInstallationService`：不可变计划、源/目标哈希、同名异内容阻断、实例/共享文件/共享解释器锁、写前配置/进程复核、磁盘检查、先 journal 后提交、重启核对。文件已落盘不会假称 Python 已回滚。
- `PythonDependencyService`：requirements/静态 pyproject/Registry 声明合并；固定既有版本约束，pip dry-run 解析完整集合，仅下载带哈希 wheel，离线 no-deps 安装已解析集合；特殊脚本、动态依赖、源码构建或既有版本变动明确阻断。失败记录 NeedsRepair。
- `DependencySourceResolver` / `VerifiedDownloadService`：Registry 活跃固定版本、GitHub 标签/分支解析到固定提交；模型工作流元数据或用户提供来源；HTTPS、HTML 拒绝、强 ETag/指定哈希续传、截断/哈希不符失败。下载计算哈希与来源给定哈希分别记录，缺来源哈希不声称通过来源校验。
- App：六区导航，三类资源勾选/筛选、导出预览、依赖及导入映射、安装预览、实时任务控制；默认索引；24 小时自动更新检查及点击校验下载，活动任务安全结束后退出 Worker；浅/深/系统主题即时保存、重建语义画刷以修复缓存导致的浅色未生效。

## 已取得的本地证据

以下均位于项目 `artifacts/acceptance/core-implementation/`，不纳入源码提交。

| 证据 | 结果及边界 |
|---|---|
| 常规 `dotnet test` | Release 131 项通过、0 失败、3 项专项默认跳过；结果在 `artifacts/acceptance/release-0.0.4/local/release.trx`；专项另有下述通过结果 |
| WPF / Worker smoke | `dotnet run --project src/FlowPack.Smoke -c Release` 退出码 0，页面初始化与真实 Named Pipe Worker 持久任务读取通过 |
| `zip64/zip64.trx` | 真实逻辑长度 4 GiB + 131072 字节文件、实际 ZIP 大于 4 GiB，导入后 SHA-256 一致；测试生成的大文件已清理 |
| `official-core/official-core-retained.trx` | 两份隔离核心完成导出/导入/文件安装、模型可见、节点加载及实际放大推理；1 项通过 |
| `official-core-4cc5e414738f431388fcc8aa50e5f158/result.json` | 两次测试自己的 prompt_id、文件数量及成功结果；同目录保留 ZIP、输出图、日志和独立副本，以供 Desktop GUI 后续验收 |
| `python/python-environment.trx` | 独立新建 venv，下载并安装 six 1.17.0，已有包保持不变；重复兼容要求不再下载；要求 six 1.16.0 被阻断；1 项通过 |
| `python-integration-*/result.json` | 对应 before/after/final 包清单；临时 venv 已清理，报告保留 |
| `ui/Light-*.png`、`ui/Dark-*.png` | 全路由 WPF 渲染及深浅色板；960/1280/1600 DIP 布局和勾选保留；不等价于真实高 DPI、键盘、下拉展开验收 |

复测命令：

```powershell
dotnet restore FlowPack.sln --locked-mode
dotnet test FlowPack.sln -c Release --no-restore
dotnet run --project src/FlowPack.Smoke -c Release
./build/Build-Release.ps1 -Version 0.0.4
```

三项专项分别通过环境变量显式启用：`FLOWPACK_TEST_ZIP64=1`、`FLOWPACK_TEST_OFFICIAL_CORE=1`（需提供核心/环境/模型/产物根目录）、`FLOWPACK_TEST_PYTHON_BASE`（需产物根目录）。测试文件声明了准确变量；不要把实际用户实例作为写入目标。

## 用户原环境保护与隔离 Desktop 状态

原 Desktop 1.0.47 可执行文件在 E 盘，登记核心在 E 盘，接管数据和 Python 在 C 盘。只读扫描曾得到 32 个工作流、54 个模型、18 个节点包；这是扫描时快照，不是资源能运行的证明。

原配置文件的启动前/启动后 SHA-256 一致：

- `settings.json`：`40DEF31C964E367BB054B4AEE24169C514C292FACDB5105C2AE3EF182FB23C6D`
- `installations.json`：`E60E869401278B3E9FBA058425157E462DD6CA9D403540C71A82A054D4F009CD`

Desktop 测试使用 `official-core-4cc5e414738f431388fcc8aa50e5f158/desktop-profile` 作为 `--user-data-dir`，只登记项目内 source/target 两个副本。未修改原 Desktop 源码、登记数据、原模型或原 Python。隐藏测试窗口未被 Windows 自动化列出；已请求用户允许显示窗口，尚未取得可见 UI 验收结果。独立配置启动本身不能解除写入门禁。交付前已核对命令行并关闭仅属于该隔离配置的测试进程树，副本及证据保留供续验。

启动规则来源：

- [Desktop v1.0.47 standalone 启动](https://github.com/Comfy-Org/Comfy-Desktop/blob/v1.0.47/src/main/sources/standalone/index.ts)
- [Desktop v1.0.47 启动资源路径覆盖](https://github.com/Comfy-Org/Comfy-Desktop/blob/v1.0.47/src/main/lib/ipc/sessionActions/launch.ts)
- [Registry 固定版本安装接口](https://docs.comfy.org/registry/api-reference/nodes/returns-a-node-version-to-be-installed)

## 未完成的工作及验收

1. 两个官方 Desktop GUI 实例的真实全流程，含 FlowPack Worker 驱动的安装和 Desktop 启动验证；其中应覆盖接管布局。现有两核心测试不能替代。
2. 默认共享目录、运行实例只读 `/object_info` 与动态类型、复杂子图/模型目录包的完整矩阵。当前声明扫描有明确能力边界。
3. 旧在线清单目前保留旧入口，没有全部转换为新 ImportPlan 下载/部署闭环；新功能页面中文尚未全部资源键化。
4. 全部任务的持续字节进度、强制中止恢复、磁盘空间耗尽、跨库共享解释器并发、Python 真正部分失败、鉴权下载的实测矩阵；现有单测仅覆盖其中部分。
5. 实际旧版安装器升级、安装/卸载/重装数据保留、安全退出竞态、干净机自包含启动；中文安装器翻译仍为旧最小覆盖层，公开发布前需补齐。
6. 系统主题事件、下拉/模态弹窗状态、键盘、高对比与实际 150%/200% DPI 测试。

资源迁移、批量清理与包维护等扩展按用户本轮范围保留后续；未删除长期要求。安装器生成和单测不作为上述未完成项的替代证据。

## 本地交付

已生成 `artifacts/installer/ComfyUI-FlowPack-0.0.4-Setup.exe` 与同目录 `SHA256SUMS.txt`：

- 字节数：74,744,126。
- SHA-256：`9EC0A457DD7CDD570B99B89C95D790FFE4508CA174AF541E2C224FBD30EB9594`。
- 安装器版本 0.0.4；Authenticode 为 `NotSigned`。
- 锁定还原、Release 测试、App/Worker win-x64 自包含发布、Inno 编译均通过；尚未执行此安装器的安装/升级/卸载，不称为已发布版本。
- 0.0.3 原安装器和原校验文件已完整复制保留在 `artifacts/archive/v0.0.3/`，旧 EXE 的哈希与已发布记录一致；常规 `artifacts/installer`、`artifacts/publish` 已由构建脚本重新生成，旧文件可从归档取回。

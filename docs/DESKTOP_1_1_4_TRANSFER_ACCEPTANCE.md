# Desktop 1.1.4 安装资格与证据

资格编号：`desktop-1.1.4-transfer-20261005`。登记键为产品版本 `1.1.4`，来自本机 Desktop ProductVersion `1.1.4.0`；只覆盖 `standalone-native` 与 `standalone-adopted`，包含文件部署及 Python 依赖。其他版本、未知布局、缺失配置来源继续拒绝安装。

本轮隔离范围为 `artifacts/acceptance/simplify-20261005/runtime-617bf9742aed42eeaab7376b15c5ec8a/`。准备脚本和 `prepared-scope.json` 记录原始配置哈希、Desktop/Core/Python 版本与资产哈希；三套 Python 环境均为独立文件副本。原生产配置、模型、节点和数据未作为安装目标。

## 已核实的候选闭环

- Desktop 1.1.4.0，Core 0.38.0，Python 3.12.11。
- WPF 页面命令经 NamedPipe/PersistentWorker 完成导出、自动导入分析、实际安装，两布局各新增 4 个文件。再次导入全部复用。工作流手动取消模型后重新计划及返回页面均未自动补回；显式重新添加才恢复。
- 两目标原先均无 `humanize`，实际安装 `4.16.0` 后所有原有包版本保持不变；要求 `4.15.0` 被实际 pip 约束拒绝，未写入冲突节点，页面进入失败状态并禁用旧计划。
- 候选页面证据：`transfer-runs/e3cb4081a9b44e3e877a3d2c4203b31d/`。首次宿主已写完整成功证据，但退出时发生 WPF 关闭重入异常，退出码 1；随后改为独立 DispatcherFrame 管理宿主生命周期。此退出记录保留，不作为正常退出证据。
- 官方 Desktop 以本轮 `--user-data-dir` 配置实际启动三个隔离 Core，命令行、父子进程、监听端口、节点信息已记录。真实工作流通过 API 提交到该 Desktop 启动的运行时；不是前端页面点击运行，也没有伪造前端提交元数据。
- 原生目标 prompt `b0cda624-1e94-4d74-b136-de0ac6bd4c4f`、接管目标 prompt `4c4011fc-cbf6-416d-904e-1744c00eebc3` 均成功，全部节点无缓存。RealESRGAN_x2plus 实际将 8×8 夹具放大为 16×16，节点实际导入新 Python 依赖并写出环境证明。
- 运行证据位于 `desktop-runtime/native-3c3675ae8cb24723939b827e0086004a/` 与 `desktop-runtime/adopted-29102f5ca3f944148868205dabcd2e50/`，包括输出哈希、完整 history、提交图及节点信息。官方程序启动信息在 `desktop-launch/`。
- 真实 pip 部分安装取消与重启修复检查：`artifacts/acceptance/simplify-20261005/python-real.trx`，2 项通过；三个文件部署强制中断阶段与重复恢复：同目录 `crash-recovery.json`。恢复保留日志和暂存，不冒称自动修好了 Python。

## 正式二进制复验

候选证据通过后登记上述资格。最终 R3 包已生成并从 ZIP 解压复验，正式资格读取内置登记，不读取测试范围文件、不使用测试放行开关。R3 SHA-256 为 `145E270AB32E1382D7F7F56EE56E90441B8078758484C6F46FB3C32B8CCA2EDA`，大小 106383545 字节；包与各二进制哈希见 `artifacts/acceptance/simplify-20261005/portable-R3-verification.json`。

| 正式复验 | 结果与证据 |
|---|---|
| 原生布局实际 App | 解压的 R3 App 通过真实页面勾选、取消并返回、重新添加依赖、原生保存/打开对话框、自动导入分析及一次点击安装。新增 4 个文件，源/目标/计划哈希一致；实际安装 humanize 4.16.0，308 个原有包版本保持不变。`portable-ui-runs/0fcc8178b5dc43239b3755b040ce4fa4/`，脚本退出 0，App/Worker 均退出。 |
| 接管布局正式 Worker | 页面命令验收宿主连接 R3 包中的实际 Worker，使用内置资格，新增 4 文件、重复导入 4 复用；安装 humanize 4.16.0，保护全部原有版本。4.15.0 冲突被真实 pip 拒绝，未写节点、未改 Python，页面失败且旧安装按钮禁用。`transfer-runs/bbb2349c70bb49b7b6c651d0b683fbbf/`，宿主退出 0，Worker 正常退出。 |
| 接管布局实际 App | R3 App 页面导出、自动导入及复用计划再次通过；4 个文件全部复用、所需空间 0 B，未执行安装。离线节点尚未核验时页面保留“需要处理”，未冒称已准备好。`portable-ui-runs/8af3aa4b30e149cf90bde65eea4d98e7/`，脚本退出 0，App/Worker 均退出。 |
| 原生布局安装后输出 | R3 实际安装后，由官方 Desktop 新启动隔离 Core，无缓存执行成功。prompt `3a578056-af9e-4660-b678-ddb9ea9fbfdb`，16×16 输出哈希 `8FEFBE85BC808B7FCADE1E354C29C118300899EF6C5E9735AD01CF3205FC3EFE`。`desktop-runtime/native-60e6ef9cde1a4664a8c281ad6b754853/`。 |
| 接管布局安装后输出 | R3 实际安装后，由官方 Desktop 新启动隔离 Core，无缓存执行成功。prompt `4464ad16-2f73-478f-b62d-c4711613117c`，16×16 输出哈希 `24E47202A90E757F69939975EC75990BEBD69D9EBB9744E3F53A50064245389D`。`desktop-runtime/adopted-dac620649ad14113bdec633a522ea3b8/`。 |

两次正式输出都验证了节点从各自独立 `.venv` 实际加载新依赖。输出通过 API 提交，`guiSubmission=false`；没有把 API 输出记为 ComfyUI 前端点击运行。运行后检查队列为空，关闭本轮拥有的 Desktop 进程，并把隔离配置自动启动设回 none。

最终隔离核验 `final-isolation-verification.json` 确认生产 `installations.json`、`settings.json` SHA-256 与准备前一致，本轮隔离范围中的 App、Worker、Desktop 和 Python 进程均已退出。早期失败页面运行遗留的窗口按实际 PID、EXE 与测试目录核对后正常关闭，闲置 Worker 通过 StopWhenIdle 退出，失败证据继续保留。

完整 Release 为 363 通过、0 失败、5 专项跳过，见 `artifacts/acceptance/simplify-20261005/theme-full-final.trx`；真实 Python 两项与 ZIP64 一项另跑通过，不改写标准套件的跳过结果。两个旧 opt-in（独立 Core 夹具、ComfyUI 前端点击运行）未执行；本轮另有上述新 Desktop 1.1.4 正式闭环证据。

中间失败记录继续保留：一次 ZIP 暂存访问被拒绝、一次 wheel 下载 SSL 失败，准确根因未查明；同一范围后续重试通过，不能写成已修复了系统权限或网络原因。另一次已完成安装的宿主因仍读取原长错误文案而断言失败，已改为验证详情原文并在上述正式接管闭环中正常退出。失败记录没有替代最终成功记录。

## 验证边界

本轮验证小型真实图像放大工作流，不涵盖任意社区节点的安装脚本、动态 Python 依赖或所有大型生成工作流；这些情况仍由现有阻断规则处理。ComfyUI 前端从侧栏打开 API 格式工作流的兼容性不在此次资格证据中，原 JSON 没有自动改写。

隔离启动依据为本机 Desktop `resources/app.asar` 中的 `paths.ts`、`settings.ts`、`desktopDetect.ts` 和 `sources/standalone/index.ts`：Windows 配置由 Electron 的 `--user-data-dir` 指定，`data-location.json` 只影响默认资源位置；空模型目录或无效输入、输出目录仍可能回到默认资源位置，因此本轮均显式使用新范围内的有效目录并关闭共享资源。旧 Desktop 探测另行读取进程 `APPDATA/ComfyUI/config.json`，不受上述参数约束；后续 `Start-TransferDesktop.ps1` 使用 PowerShell 7.4 及以上的进程环境覆盖，将 `APPDATA`、`LOCALAPPDATA` 指向范围内的 `process-environment/Roaming`、`Local`，并记录在启动证据中。该调整不改变正在运行的进程；首次候选启动没有覆盖这两个环境变量，但未自动登记或启动生产实例。Python 的基础解释器仍按虚拟环境配置只读访问，独立环境及安装目标不变。

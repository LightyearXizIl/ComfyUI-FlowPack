# ComfyUI FlowPack 开发交接

最后更新：2026-09-12｜工作分支：`main`｜当前源码版本：`0.0.3`（已发布，未签名）

## 结论

`v0.0.3` 已完成 A–F 基线封存、六区壳层、运行时中英双语、受限 ZIP staging、schema v6 迁移备份、只读 Desktop 适配器和更新元数据校验，并已发布。它**不是**已完成实机验收的正式版：真实 Desktop 写入入口仍禁用，L1–L4、journal 恢复、安装/升级/卸载、下载续传和干净机安装器验收均未完成。

不要把本地候选构建、自动化单测或页面文案当成正式发布/实机验证证据。

## Git 与提交边界

- A–F 后端基础已独立提交：`7c539f4 feat: add ComfyUI resource foundations`。
- v0.0.3 源码候选基线为 `ac82558`，版本号修正为 `7b664ab`，安装器证据修正为 `3e8041a`；发布后交接记录为 `2fc6de4`。已发布标签 `v0.0.3` 固定指向 `3e8041a`，不得移动。
- `.workbuddy/` 已在 `.gitignore`，不得提交。
- `main` 已推送至 `origin`；发布标签 `v0.0.3` 与 [GitHub Release](https://github.com/LightyearXizIl/ComfyUI-FlowPack/releases/tag/v0.0.3) 已于北京时间 2026-09-12 11:41 发布，均非草稿/预发布；安装器为 `NotSigned`，不能声称已签名。

## 当前实现

| 区域 | 已实现 | 明确边界 |
| --- | --- | --- |
| 壳层与语言 | `Home / Library / Packaging / Install / Tasks / Settings`；资源库聚合工作流、模型、节点；安装聚合包和预览；`ILocalizationService` 支持系统、简中、英文即时切换并保存偏好 | 历史视图中的所有遗留中文尚未全部资源键化；需补 UI 自动化与高对比验证 |
| 包与导出 | `.cpack`、`.cpack.json`、展开目录和原生 ZIP 识别；原生 ZIP 私有 staging；ZIP64 大模型不压缩导出 | `.cpack` 三格式统一预览、全部依赖证据和 4 GB 实体回归未完成 |
| staging 安全 | 拒绝 traversal、绝对/盘符/UNC/ADS、保留名、重复路径、链接和超量展开；失败删除 `.incoming-*` | 未做真实 4 GB、空间不足/强制中止的完整矩阵 |
| 资源库 | schema v6；v5 迁移前 `VACUUM INTO` 备份；新增资源、引用、安装、缓存、尝试、journal、备份、验证报告表 | App 仍有历史直接数据库访问；尚未完全收口为 Worker 唯一写者；journal 没有恢复执行器 |
| Worker | 协议 v2；持久化下载、状态与任务读取；安装计划/验证命令明确返回 `safety-gate-not-met` | 暂停/继续/取消/重试、每实例串行、下载并发 3、重连与写锁尚未实现 |
| Desktop | `OfficialComfyDesktopAdapter` 从只读探测生成目标指纹和冻结计划；默认 `AllowsWriteExecution=false` | 未知/普通真实实例禁止写；未实现配置受管段、实例锁、停机、Python wheel 策略或写前复检 |
| 更新与安装器 | GitHub latest release 检查要求 HTTPS setup 资产及同名 `SHA256SUMS.txt` 行；设置页可手动检查；Inno 开启语言选择/安装目录/保留语言和目录 | 不下载、不启动安装器、无 24h 冷却开关；中文 `.isl` 是最小覆盖层，发布前须替换为固定的完整 MIT 上游翻译 |

## 安全不变量

1. 不修改 Desktop 的 `resource/ComfyUI`。
2. 不对真实 Desktop、Python、模型或节点做写入；`StartInstallCommand` 必须继续禁用，直到隔离实例实测通过。
3. 所有下载仅写 FlowPack staging；不允许任意 HTTP 或第三方脚本自动执行。
4. 计划执行必须重新核对目标指纹、磁盘空间、冲突和备份；同名异哈希、本地修改、外部节点和 Python 替换均应阻断。
5. 未来 schema 拒写；迁移失败不能清空旧库。

## 已验证的本机证据

- 锁定还原、Debug 与 Release 均为 **101/101**，`git diff --check` 通过。
- 本地安装器：`artifacts/installer/ComfyUI-FlowPack-0.0.3-Setup.exe`，74,387,576 字节，SHA-256 `175DFC33E1DC6B9FDF63F6013EDC4762F1018E25D607BCBC5E29249974FEEB62`，Authenticode `NotSigned`。仅验证生成，未做干净机安装/卸载。
- 远程资产：[ComfyUI-FlowPack-0.0.3-Setup.exe](https://github.com/LightyearXizIl/ComfyUI-FlowPack/releases/download/v0.0.3/ComfyUI-FlowPack-0.0.3-Setup.exe)（74,387,576 字节）与 [SHA256SUMS.txt](https://github.com/LightyearXizIl/ComfyUI-FlowPack/releases/download/v0.0.3/SHA256SUMS.txt)（99 字节）。已于 2026-09-12 复读远程 `SHA256SUMS.txt`，内容与上述 SHA-256 和文件名一致；未重新下载完整 EXE 做端到端哈希。
- 本机只读确认过 Desktop 配置和实例路径；未进行写入、停机、配置修改或真实生成。

## 下一步（按安全依赖顺序）

1. 为 v0.0.3 之后的下一轮开发建立新版本计划；已发布标签不得移动。
2. 将库写操作收口到 Worker：实现 requestId 幂等、库锁、任务尝试、暂停/继续/取消/重试及 journal 恢复报告。
3. 以两个隔离官方 Desktop 实例实测适配器、配置备份受管段、实例锁、源/目标哈希与 Python wheel 冲突策略；不要用当前真实配置目录写测。
4. 实现 L1–L3；L4 只跟踪 FlowPack 发出的 `prompt_id`，禁止清全局队列或中断他人任务。
5. 补齐 `.cpack` 三格式往返、ZIP64/恶意归档、HTTP 恢复、DPI/高对比/键盘和安装器生命周期测试。
6. 后续版本取得明确授权后才签名（如有证书）、打标签、推送和发布。

## 关键文件

- [ShellViewModel.cs](src/FlowPack.App/ShellViewModel.cs)：页面状态、语言与手动更新检查，安装命令仍禁用。
- [LocalizationService.cs](src/FlowPack.App/Services/LocalizationService.cs)：双语偏好和资源键。
- [NativePackageStagingService.cs](src/FlowPack.Infrastructure/NativePackageStagingService.cs)：普通 ZIP 私有 staging。
- [ResourceLibraryDatabase.cs](src/FlowPack.Infrastructure/ResourceLibraryDatabase.cs)：schema v6 与迁移备份。
- [DesktopAdapter.cs](src/FlowPack.ComfyUI/DesktopAdapter.cs)：只读适配和冻结计划。
- [FlowPack.iss](installer/FlowPack.iss)：安装器语言/目录/升级保留配置。
- [REQUIREMENTS_ACCEPTANCE.md](REQUIREMENTS_ACCEPTANCE.md)：完整验收表；未具备实机证据的条目不得关闭。

## 正式出口条件

只有完成隔离 Desktop 双实例、共享资源、节点修改、Python 冲突、Worker 中止恢复、L1–L4、修复/升级/卸载/迁移、离线包、干净 Windows 安装、升级/卸载/重装保留验证，且记录实际产物/日志/截图后，才可以关闭 M10 并称为正式版。

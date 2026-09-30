# ComfyUI FlowPack

ComfyUI FlowPack 是面向 Windows x64 与官方 ComfyUI Desktop 的工作流资源管理和打包工具。

## 0.0.5

- 首页、我的资源、导入安装三个主要入口；任务与设置作为全局工具，打包工具归入资源库。
- 工作流、模型、节点文件夹快捷入口，使用所选实例已登记的真实路径，支持多个目录选择。
- 深浅主题、中文/英文偏好、资源搜索与分类、独立导出预览，以及固定安装目标和摘要的导入布局。
- 持久 Worker、任务记录和状态动作；工作流、模型、节点资源扫描，原生 ZIP 与旧清单导入导出。
- 在线依赖来源、经 SHA-256 校验的暂存下载、实际内容复用核对，以及部署计划、恢复和安装后重新检查。
- GitHub Release 更新检查、下载安装器、校验及独立引导交接。

[发布与下载](https://github.com/LightyearXizIl/ComfyUI-FlowPack/releases) · [0.0.5 更新说明](release-notes/v0.0.5.md) · [页面规划](docs/UI_LAYOUT_REPLAN_2026-09-30.md)

## 使用范围与限制

正式 ComfyUI Desktop 自动部署按版本、布局和能力验收控制；目前生产资格表为空，实际环境中受门禁限制的安装保持不可执行。资源检测、整理、导入暂存和导出可继续使用。

完整软件自身升级、数据保留、干净机安装/卸载、系统多 DPI 和实际 Explorer 交互仍待实机验收。安装器未签名。专项测试需要明确提供隔离夹具，常规回归不会自动操作真实 ComfyUI 环境。

0.0.4 按原本地安装包补发，未重新构建；当时完整源码快照不可精确回溯，详见 [归档说明](release-notes/v0.0.4.md)。当前完整实现与重绘后的源码保存在 0.0.5。

## 构建

要求 Windows x64、.NET SDK 10.0.112；生成安装器还需 Inno Setup 6。

```powershell
dotnet restore .\FlowPack.sln --locked-mode
dotnet test .\FlowPack.sln -c Release --no-restore
.\build\Build-Release.ps1 -Version 0.0.5
```

安装器输出到 `artifacts/installer/`。发布脚本运行锁定还原、测试、自包含 App/Worker 发布、安装器编译，并生成三项 SHA-256 清单。标签工作流为常规版本重新构建；带 `.archive.json` 的历史原包版本仅核对归档声明，不替换旧安装包。

## 文档

- `HANDOFF.md`：当前证据、发布状态和待验收事项；
- `IMPLEMENTATION_PLAN.md`：实施阶段与安全边界；
- `REQUIREMENTS_ACCEPTANCE.md`：需求和验收追踪；
- `FRONTEND_REFERENCE.md`：界面视觉与交互基准；
- `VERSIONING.md`：满 10 进 1 的版本规则。

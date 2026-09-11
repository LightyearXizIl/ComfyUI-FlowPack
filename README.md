# ComfyUI FlowPack

ComfyUI FlowPack 是面向 Windows x64 与官方 ComfyUI Desktop 的工作流资源安装和打包工具。

## 0.0.3 本地候选状态

`0.0.3` 是未发布的本地候选，不是已验收的正式安装版。目前完成了：

- WPF 桌面壳层及首页、资源库、打包、安装、任务、设置六个区域；
- 简体中文/英文运行时切换与语言偏好保存；
- 作者 LightyearXizIl、GitHub 仓库和脱敏诊断摘要导出；
- schema v6 资源库、迁移前 SQLite 备份、主题配置、Worker IPC 和 SHA-256 校验的 staging 下载；
- `.cpack`/展开目录/原生 ZIP 识别、工作流原文保留、草稿交换及不完整工作流包导出；
- 资源包、工作流、模型与节点列表筛选，以及真实持久化任务显示；
- 受路径、链接、保留名、重复项、展开容量限制保护的 ZIP staging；
- 只读 Desktop 适配与不可执行的冻结安装计划；GitHub Release 更新元数据检查；
- Debug/Release 自动化测试、锁定 SDK/依赖和 Windows 安装器构建链路。

真实 ComfyUI Desktop 写入、配置备份/恢复、L1–L4 验证、离线完整包、暂停续传、升级卸载和安装器干净机生命周期仍待隔离实机验证。安装入口保持禁用，当前候选不能用于向真实 ComfyUI 环境部署资源。

## 构建

要求：

- Windows x64；
- .NET SDK 10.0.112；
- Inno Setup 6（只在生成安装器时需要）。

```powershell
dotnet restore .\FlowPack.sln --locked-mode
dotnet test .\FlowPack.sln -c Release --no-restore
.\build\Build-Release.ps1 -Version 0.0.3
```

安装器输出到 `artifacts/installer/`。

## 文档

- `IMPLEMENTATION_PLAN.md`：完整实施阶段与安全边界；
- `REQUIREMENTS_ACCEPTANCE.md`：需求和验收追踪；
- `FRONTEND_REFERENCE.md`：界面视觉与交互基准；
- `VERSIONING.md`：满 10 进 1 的版本规则。

## 已知限制

0.0.3 候选已在本机构建安装器并通过其 Release 测试步骤；未进行干净机安装/卸载验收、未打标签、未推送、未创建 GitHub Release，且没有代码签名。Windows 可能显示未知发布者提示。

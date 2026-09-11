# ComfyUI FlowPack

ComfyUI FlowPack 是面向 Windows x64 与官方 ComfyUI Desktop 的工作流资源安装和打包工具。

## 0.0.2 状态

`0.0.2` 是工程预览版，目前完成了：

- WPF 桌面壳层及九个独立页面 View；
- 作者 LightyearXizIl、GitHub 仓库和脱敏诊断摘要导出；
- 资源库 SQLite 持久化、主题配置、Worker IPC 和 SHA-256 校验的 staging 下载；
- `.cpack`/展开目录导入、工作流原文保留、草稿交换及不完整工作流包导出；
- 资源包、工作流、模型与节点列表筛选，以及真实持久化任务显示；
- Debug/Release 自动化测试、锁定 SDK/依赖和 Windows 安装器构建链路。

真实 ComfyUI Desktop 绑定、部署、恢复、离线完整包、试运行和安装生命周期仍待实机验证。安装入口保持禁用，当前版本不能用于向真实 ComfyUI 环境部署资源。

## 构建

要求：

- Windows x64；
- .NET SDK 10.0.112；
- Inno Setup 6（只在生成安装器时需要）。

```powershell
dotnet restore .\FlowPack.sln --locked-mode
dotnet test .\FlowPack.sln -c Release --no-restore
.\build\Build-Release.ps1 -Version 0.0.2
```

安装器输出到 `artifacts/installer/`。

## 文档

- `IMPLEMENTATION_PLAN.md`：完整实施阶段与安全边界；
- `REQUIREMENTS_ACCEPTANCE.md`：需求和验收追踪；
- `FRONTEND_REFERENCE.md`：界面视觉与交互基准；
- `VERSIONING.md`：满 10 进 1 的版本规则。

## 已知限制

0.0.2 已验证本机自动化、Worker IPC 和安装器构建链路；真实 ComfyUI Desktop、Python、模型、节点和离线安装仍未完成实机验证。安装包未进行代码签名，Windows 可能显示未知发布者提示。

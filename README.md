# ComfyUI FlowPack

ComfyUI FlowPack 是面向 Windows x64 与官方 ComfyUI Desktop 的工作流资源安装和打包工具。

## 0.0.1 状态

`0.0.1` 是首个工程预览版，目前完成了：

- WPF 桌面壳层及九个独立页面 View；
- 首页、资源包、工作流、任务和设置导航；
- 资源包下模型库、节点管理二级入口；
- 正式启动空状态，不加载虚假连接、资源或下载进度；
- Debug/Release 自动化测试、锁定 SDK/依赖和 Windows 安装器构建链路。

ComfyUI 实例绑定、资源库持久化、Worker 下载、真实安装、离线资源包、试运行、维护和恢复功能仍在后续阶段实现。当前版本不能用于向真实 ComfyUI 环境部署资源。

## 构建

要求：

- Windows x64；
- .NET SDK 10.0.112；
- Inno Setup 6（只在生成安装器时需要）。

```powershell
dotnet restore .\FlowPack.sln --locked-mode
dotnet test .\FlowPack.sln -c Release --no-restore
.\build\Build-Release.ps1 -Version 0.0.1
```

安装器输出到 `artifacts/installer/`。

## 文档

- `IMPLEMENTATION_PLAN.md`：完整实施阶段与安全边界；
- `REQUIREMENTS_ACCEPTANCE.md`：需求和验收追踪；
- `FRONTEND_REFERENCE.md`：界面视觉与交互基准；
- `VERSIONING.md`：满 10 进 1 的版本规则。

## 已知限制

0.0.1 只验证当前工程壳层和安装器生命周期。真实 ComfyUI Desktop、Python、模型、节点、下载和离线安装尚未完成验证。安装包未进行代码签名，Windows 可能显示未知发布者提示。

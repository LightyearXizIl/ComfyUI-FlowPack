# ComfyUI FlowPack

面向 Windows x64 与官方 ComfyUI Desktop 的资源管理和转移工具。查看当前实例的工作流、模型和节点包，分析依赖，按需打包，在目标电脑导入并核对资源。

**当前版本为 `0.0.6`。** 当前交付、测试证据和验证范围见[项目状态](docs/PROJECT_STATUS.md)。

## 开始使用

从 [GitHub Release](https://github.com/LightyearXizIl/ComfyUI-FlowPack/releases/tag/v0.0.6) 下载 `ComfyUI-FlowPack-0.0.6-Setup.exe` 和 `SHA256SUMS.txt`，核对校验后运行安装器。Windows x64 安装器包含 .NET 运行时，安装包未签名。

在首页确认实例，扫描后进入“资源库”；勾选资源后进入同一导出清单，或在“导入安装”选择来源并等待自动检查。完整操作说明见[使用指南](docs/USER_GUIDE.md)。

历史便携测试包及临时构建已清理；保留的便携/预览数据位于本机 `artifacts/user-data`，不进入 Git 或安装器。源码开发预览仍可按[开发指南](docs/DEVELOPMENT.md)启动。

## 主要功能

| 入口 | 用途 |
|---|---|
| 首页 | 选择实例、扫描资源、直达实例文件夹；版本、资源目录与分组状态在居中弹窗查看。 |
| 资源库 | 按真实目录查看工作流、模型和节点包；搜索名称、类别和路径；查看依赖和被引用关系。 |
| 导出资源 | 一份最终勾选清单，工作流自动带可确认依赖；取消的依赖保持取消。支持单独及混合导出。 |
| 导入安装 | 选择 ZIP、工作流、目录及旧格式后自动分析、匹配本地资源并生成安装计划；准备好后点击安装。 |
| 设置 | 界面偏好、Desktop 实例、存储位置、日志、关于作者与更新。 |

扫描、校验、复制、解压和下载显示阶段进度。已知总量显示当前阶段百分比，未知总量显示活动进度和已处理量。工作流的前端列表核对失败时保留“尚未核对”，不会据此判定已删除。

**当前安装范围：** 内置资格覆盖 Desktop `1.1.4`（本机产品版本 `1.1.4.0`）的原生目录与接管已有目录布局，包含文件及 Python 依赖安装；其他版本和未知布局继续阻止安装。实际安装、节点加载和工作流输出证据见 [Desktop 验收记录](docs/DESKTOP_1_1_4_TRANSFER_ACCEPTANCE.md)。

## 本地开发

需要 Windows x64 与 [.NET SDK 配置](global.json) 指定的 `10.0.112`（允许同补丁系列更新）。在源码根目录双击 `实时预览.cmd`，保存源码后会重新编译 App/Worker 并重启 WPF 窗口；双击 `停止实时预览.cmd` 停止预览。便携 ZIP 是固定快照，不会随源码刷新。

仅生成免安装包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build\Build-Portable.ps1
```

输出到 `artifacts/portable/`，此脚本不运行测试。测试、隔离构建、安装器构建与故障定位见 [开发指南](docs/DEVELOPMENT.md)。

## 项目文档

- [文档导航](docs/README.md)：按使用、开发、设计、验收和历史记录查找。
- [使用指南](docs/USER_GUIDE.md)：操作步骤、资源库含义、导入导出、日志和常见问题。
- [开发指南](docs/DEVELOPMENT.md)：环境、构建、测试、实时预览和打包。
- [架构说明](docs/ARCHITECTURE.md)：模块边界、数据流、Worker、存储与兼容性。
- [项目状态](docs/PROJECT_STATUS.md)：当前成果、交付物、证据和未完成事项。
- [界面规范](DESIGN.md) · [开发交接](HANDOFF.md) · [版本规则](VERSIONING.md)。

[GitHub 项目](https://github.com/LightyearXizIl/ComfyUI-FlowPack) · [发布下载入口](https://github.com/LightyearXizIl/ComfyUI-FlowPack/releases) · [第三方说明](THIRD-PARTY-NOTICES.md)

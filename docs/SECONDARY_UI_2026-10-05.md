# 二级界面与菜单统一 · 2026-10-05

> **布局已更新。** 下文记录 R4 的右侧面板验收；用户要求改为软件中间的弹窗后，当前布局以 `CENTERED_DETAILS_2026-10-05.md` 为准。菜单主题统一仍保留，R4 包与证据继续归档。

本轮针对截图中的默认文件夹菜单、首页长详情和所有共享下级控件完成源码改造。保留灰黑白风格、首页白底黑字按钮、此前导入导出与安装功能，源码版本仍为 `0.0.5`。没有提交、发布或 Figma 云端同步。

## 用户可见变化

- 多目录文件夹菜单采用圆角容器，没有无用的左侧图标空栏；目录名称和路径分行，正文浅色、路径灰色，长路径省略并提供完整提示。菜单中的“查看全部资源目录”进入实例详情。
- 首页“版本详情”和“资源目录”进入独立侧面板。使用“版本信息 / 资源目录”二级标签，分别显示版本字段或带复制、打开入口的目录行。滚动限制在面板内部，不再撑高首页。
- 当前状态“查看详情”进入独立状态面板，扫描提示按类别分组，保留全部条目和完整记录复制入口。
- 二级、三级菜单、下拉选项、提示框、标签和高级折叠区统一使用共享语义颜色、字体、卡片或控件圆角，并补齐悬停、键盘焦点、选中与禁用状态。
- 返回或 Esc 关闭详情，主导航切换也关闭详情；原资源勾选保留。实例变化后旧菜单关闭，旧目录行不能打开前一个实例的目录。

实现入口：`App.xaml`、`MainWindow.xaml/.cs`、`ShellViewModel.Secondary.cs`、`ShellViewModel.Home.cs`、`Views/HomeView.xaml`、`Views/SecondaryDetailsView.xaml`、`Views/FolderShortcutsView.xaml.cs`。使用与设计规则已同步到 `USER_GUIDE.md`、`PORTABLE_PREVIEW.md` 和根 `DESIGN.md`。

## 验证证据

| 范围 | 实际结果 |
|---|---|
| UI 与状态专项 | **10 通过、0 失败**，`artifacts/acceptance/secondary-ui-20261005/secondary-ui-final.trx`。首轮发现高对比度下拉项默认背景不一致，恢复共享 SurfaceBrush 后通过；首轮失败 TRX 保留。 |
| 标准 Release 全量 | **367 通过、0 失败、5 专项跳过**，总计 **372**；`secondary-release-final.trx`。跳过项没有合并算作通过。 |
| WPF 排版与控件 | 深浅主题分别覆盖 960×640、1280×800、1600×1000 DIP；非空目录、长路径、20 条状态提示、目录与版本标签、三级菜单、下拉项、禁用态与高对比度均检查。截图在 `artifacts/acceptance/secondary-ui-20261005/ui/`。 |
| 解压后的实际 App | 版本面板、目录标签、真实动态模型菜单、菜单进入完整目录、状态详情、Esc 返回、导航关闭详情通过；App/Worker 正常退出。运行 ID `e1b3064562ff448a88bdb2e059fbb040`。 |
| 解压后的实际导入导出 | 页面导出、自动依赖、手动取消保持、明确重新添加依赖、ZIP 写出、自动导入与复用计划通过；未执行安装。运行 ID `7f212d9ea6ca4b9190a7590002eacfc3`。 |
| 机械检查 | `git diff --check` 通过。impeccable 对三个目标 XAML 的静态扫描无发现；该扫描不替代 WPF 排版、主题与实际程序验证。 |

实际运行目录均位于前轮独立验收范围：`artifacts/acceptance/simplify-20261005/runtime-617bf9742aed42eeaab7376b15c5ec8a/portable-ui-runs/<运行 ID>/`，包含 `result.json`、动作记录、UIA 树、截图、二进制哈希、Worker 作业与 `process-cleanup.json`。

二级 UI 验收使用克隆的独立 Desktop 配置，仅在克隆的来源记录增加一个真实模型目录和一条不存在的额外模型配置引用，以产生真实扫描提示。没有启动 Desktop 或执行安装，没有改原配置或已有资源。脚本参数 `-SecondaryDetailsOnly` 与 `-Install` 互斥；自动化初期的 HWND 常量和 UIA 控件名称错误已修正，失败运行保留。

## R4 便携交付

[FlowPack-UI-Portable-Secondary-20261005-R4.zip](../artifacts/portable/FlowPack-UI-Portable-Secondary-20261005-R4.zip)：**106395034 字节**，SHA-256 `47B69FABA04FCD34D1820B533B1AF73AFEA90CA8475B6C7BEF78EEBACA8D3E4F`。

锁定依赖还原、Release 自包含 App/Worker 发布、ZIP 解压与启动通过。ZIP 共 **619** 项，包含 `portable.mode`、App 和 Worker，没有打入运行产生的 `Data`。完整解压后双击 `Open-FlowPack.cmd`，旧 R3 保留。

R4 本轮验证 UI、导出和自动导入预览；真实文件安装、Python 依赖与 Desktop 工作流输出继续引用[前轮 R3 安装证据](DESKTOP_1_1_4_TRANSFER_ACCEPTANCE.md)，不能记成 R4 新执行的安装或推理。操作系统多显示器/DPI 切换、ComfyUI 前端点击运行和正式安装器升级没有新增验收证据。

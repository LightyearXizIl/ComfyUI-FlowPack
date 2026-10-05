# FlowPack 界面与资源转移改版 · 2026-10-05

WPF 界面、资源独立导出、统一导入、阶段进度、实例版本及便携测试包已实现。保留原有未提交修改、资源和记录；本轮没有提交、推送或发布版本，没有向生产 ComfyUI 安装资源。

## 界面与行为

- 首页所有内容区按钮白底黑字，实例名称独立一行，扫描/关联放在标题右侧。Core 与 Desktop 版本分别显示，未知为“未确认”；运行与磁盘 Core 不同则放进详情。路径收进详情，文件夹直达只出现在首页。右侧状态固定，长提示在模块内滚动。
- 资源库的搜索位于标题右上角，按名称、类别和路径过滤，隐藏结果不会丢掉勾选。工作流、模型、节点显示实例目录树；共享模型来源独立标明，节点类型放在完整节点包之下。选中资源后，右侧显示依赖、位置、可用情况及工作流引用。扫描和导入入口留在首页，资源库不显示扫描数量页脚。
- 只有运行实例、用户上下文及用户数据列表都核对成功，缺席前端列表的工作流才进入底部独立区。离线、错误实例、多用户未确认、HTTP 失败与列表解析失败均保持“尚未核对前端列表”；不推断已删除。扫描排除索引及非工作流 JSON。
- 导出页将参考工作流 `IsReference` 与实际载荷 `IsSelected` 分开。可只选工作流、只选模型、只选节点包或混合。分析操作只勾选可唯一确认的依赖，之后可手动取消。实际导出以最终勾选及类别开关为准；预览不会再自动把取消的项目加回。共享包去重，完整节点包包含配套文件，内置节点提示无需迁移。引用警告留在依赖面板；实际导出计划检查选中的文件、模型分片、链接及路径，不让未选中的缺失模型阻止导出。
- 导入统一接受 ZIP、工作流、目录、旧 `.cpack` / `.cpack.json` / JSON 清单及展开旧包。文件、目录入口和拖放提示在中央；下方按类别/节点包分组，右侧固定安装摘要及主要操作。历史记录在底部折叠区，重新核对走同一流程，来源移走会明确提示。
- 保留已有设置导航、日志强度、7/10/15/30 天保留、一键清理，以及关于作者的 GitHub 和检查更新→下载按钮。
- Worker 记录增加可选阶段处理量/总量/单位，旧记录仍可反序列化。扫描、哈希、解压、导出、安装及下载使用真实阶段进度；已知总量显示当前阶段百分比，未知总量显示活动进度与已处理量。全局条跨页面保留，完成后短暂展示；失败保留原因。只有任务支持取消时才启用取消。长文件 IO 在后台运行，树分支按展开实现，列表虚拟化，扫描结果分批加入 UI。

## 源码对应

| 设计面 | WPF / 业务源 |
|---|---|
| 首页、状态、版本 | `Views/HomeView.xaml`、`ShellViewModel.Home.cs`、`InstanceVersionReader.cs` |
| 目录树、搜索、依赖面板 | `Views/ResourceBrowserView.xaml`、`ShellViewModel.Resources.cs` |
| 参考与实际导出选择 | `Views/PackageWizardView.xaml`、`ShellViewModel.Core.cs`、`ExportSelectionPolicy.cs` |
| 统一导入与历史 | `Views/InstallView.xaml`、`ResourceImportService.cs`、`PersistentWorkerService.cs` |
| 实例/前端列表确认 | `RuntimeNodeInspector.cs`、`FrontendWorkflowList.cs` |
| 全局进度与后台报告 | `MainWindow.xaml`、`ShellViewModel.Progress.cs`、`OperationProgress.cs`、`ProgressIO.cs` |
| 共享样式与主题 | `App.xaml`、`ThemeDefaults`、`ApplyThemeToResources`、根目录 `DESIGN.md` |

前端核对采用官方保存工作流使用的用户数据 API：`GET /userdata?dir=workflows&recurse=true&split=false&full_info=true`。使用前检查运行进程/端口及 Core、base、user 路径；请求前后复核同一运行端点。单用户默认目录可确认，多用户不猜用户 ID。官方参考：https://github.com/Comfy-Org/ComfyUI_frontend/blob/main/src/scripts/api.ts

## Skill 落实

| Skill | 本轮落实 |
|---|---|
| ui-skills-root | 选择 WPF 原生布局、控件与验证方法，网页框架要求不迁入项目。 |
| impeccable / improve-ui | 页面层级、内容精简、对齐、标题栏操作与长任务状态。 |
| baseline-ui / fixing-accessibility | 共享控件尺寸、焦点、自动化名称、可读错误及键盘焦点遍历；保持原生虚拟化。 |
| create-design-md | 从实际 WPF 主题、共享样式及用户要求建立 DESIGN.md；DTCG 导出验证。 |
| figma-use / figma-generate-design | 读取旧文件结构；准备可编辑设计源。云端写入受额度限制，未完成。 |
| figma-generate-library | 本地原生插件复用按钮组件、文字样式及颜色模式，尚未执行云端组件验收。 |
| figma-design-to-code | 沿现有稿和已确认结构对应到原生 XAML；保留以上映射，最终云端一致性待验收。 |

DESIGN.md 的 lint 结果为 0 错误、1 个命名约定警告（已有语义颜色命名没有 `primary`）。不虚构新的主色令牌来消除该警告。DTCG 输出已确认包含颜色、字号/字体和圆角；现行规范不支持主题模式，替代主题保存在 Themes 表。未保留导出文件。

## 自动验证

产物目录：`artifacts/acceptance/resource-transfer-20261005/`。

- 全量 Release：**333 通过、0 失败、5 跳过**，总计 338；`transfer-acceptance-final.trx`。
- 独立与混合导出、共享节点包去重、ZIP 清单/导入 roundtrip、旧普通 JSON 清单、未选中依赖、离线/错误/多用户及取消：`ResourceTransferTests`，最后包含在全量结果中。
- WPF 真窗口布局与截图：`ui-final/`。检查浅色/深色、960×640、1280×800、1600×900、长名称、展开详情、主要操作可见及高对比度色板。高对比度采用原生系统色板夹具，没有改用户系统设置。
- 慢速真实文件哈希期间，Dispatcher 心跳持续执行、可切换页面、出现中间阶段百分比、失败保留错误；包含在 UiShellTests。
- 补充真实 WPF 键盘焦点前移和可见/启用状态检查：`keyboard-native.trx`，1 通过。
- Worker 导入历史及可选进度字段经重启保留；取消、失败、恢复与旧任务记录已有回归覆盖。
- 5 个跳过项为真实 Desktop 推理、双官方 Core 转移推理、两项真实 Python wheel 安装/中断，以及实际大于 4 GiB ZIP64；本轮不执行生产实例安装，也未把这些条件当成通过。
- `git diff --check` 无差异格式错误。没有声称完成干净机器、系统级 DPI 或生产 ComfyUI 安装/推理验收。

## 测试交付

最新便携 ZIP：`artifacts/portable/FlowPack-UI-Portable-Resource-Transfer-20261005-R2.zip`。

- 103,035,139 字节，619 个 ZIP 条目；带 App、同版 Worker、.NET 运行时及 `Open-FlowPack.cmd`，不包含测试 Data。
- SHA-256：`5A235E22CC3A074BC21F61338B34ECFB0F7520003704FD07E416B5ABB08A6A1F`；旁边的 `.sha256` 可核对。
- 已从此 ZIP 重新解压，并运行启动入口。真实窗口响应正常，完成实例发现及资源扫描；默认配置与资源库写入本次解压目录。记录：`portable-verification.json`，运行日志在 `portable-extracted-R2/.../Data/Logs`。
- 只读扫描的本机实例显示 Core 0.38.0、Desktop 1.1.4.0；这是本机运行时验证，不保证其他电脑的版本。未修改 ComfyUI 资源或启动测试安装。
- 先前便携包及其测试数据全部保留，R2 为此次推荐测试包。便携版是打包时快照，本轮不改正式版本号或 Release。
- 根目录 `实时预览.cmd` 会启动真实 WPF 窗口。保存产品源码后，后台重建 App 与 Worker，成功后重启预览。已观察到源码变化后循环重建并启动新窗口；编译失败保持旧窗口。`停止实时预览.cmd` 按控制器身份和唯一构建目录停止自己的进程，保留 Data。
- 长任务测试期间不要修改源码：预览重启会中断预览目录的任务。普通便携包适合稳定地测试长任务。

## Figma 未完成项

现有文件：https://www.figma.com/design/ATifwFUoSDhcXPgeCRVgfS 。旧稿未改动。

Code Connect 接口提示账号缺少相应席位；库查询和直接编辑均返回 Starter MCP 调用额度耗尽。浏览器兜底打开文件后，页面读取也超时。因此 **没有创建云端改版页，没有完成云端图层/变量/自动布局与 WPF 的最终对齐验收**。

本地替代交付：`docs/design/resource-transfer-20261005/`，含 22 张可编辑文字/矢量 SVG、语义设计 JSON，以及可创建新页面的 Figma 开发插件。覆盖主要页面及加载、离线、空内容、错误状态；没有用整页截图充当设计图层。插件语法检查通过，但未在 Figma 运行，不能把它记为已同步或已通过原生图层验收。使用和重新生成说明见同目录 README。

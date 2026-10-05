# FlowPack 当前状态与交接摘要

## 当前版本 · 0.0.6

**2026-10-05**：导入安装页及展开后的按钮共用首页和弹窗的白底黑字圆角样式，资源输入、折叠区、目标选择和状态沿用共享主题。首页保持原排版，详情继续居中。资源库、自动依赖导出、自动导入计划、Python 能力判定、日志与更新改进一并进入本版本。

**[v0.0.6 已正式发布](https://github.com/LightyearXizIl/ComfyUI-FlowPack/releases/tag/v0.0.6)**，标签固定在 `14db5008e13389eada2196e08f1f5b08fe015a47`，远端 [Release 工作流](https://github.com/LightyearXizIl/ComfyUI-FlowPack/actions/runs/37331770027)与本地标准 Release 均为 **367 通过、0 失败、5 按需专项跳过**。正式 App/Worker 的真实页面导出、取消依赖保持、自动导入与本地复用预览通过，进程正常退出。本轮未重新运行 Desktop 推理；此前 Desktop 1.1.4 两布局安装及真实输出资格证据已提取到[资格记录](release-evidence/0.0.6/deployment-qualification.json)。资产与下载核验见 [release.json](release-evidence/0.0.6/release.json)。

本机 `artifacts/installer` 为下载并核验的正式安装器及校验文件。安装器 **74907419 字节**，SHA-256 `4FFC2991B5FE767BEEFF4B05FD8FA15F21F2D482676980004B1AB0F97800D2CB`，与远端资产及校验清单一致。旧便携包、历史构建、隔离环境和旧本地安装包副本已清理，原便携/预览数据保留在 `artifacts/user-data`。工作区外临时测试目录、本轮部分构建缓存及下载临时文件的删除被自动审批拒绝，这部分仍有残留。历史文档中的 `artifacts/acceptance` 与旧便携包路径只用于追溯，原产物现已清理；当前精简证据在[发布证据](release-evidence/0.0.6/README.md)。

## 前轮界面 · R7 弹窗风格统一

**2026-10-05**：详情弹窗标签与顶部导航共用透明底、字体、留白和选中下划线。关闭、复制、打开和保留的旧导出预览操作共用首页白底黑字圆角按钮；旧软件提示框也统一为居中提示窗口。首页排版保持 R4，详情继续居中。见[弹窗风格记录](DIALOG_STYLE_2026-10-05.md)。

推荐 [R7 便携包](../artifacts/portable/FlowPack-UI-Portable-DialogStyle-20261005-R7.zip)，103063011 字节，SHA-256 `6D9B2AC676970B5AE56CFBD50A74F3E9454D183D7F5C869B6F8204CBA65098C5`。最终 Release **367 通过、0 失败、5 专项跳过**（372 总计）。最新汇总 `artifacts/acceptance/dialog-style-20261005/final-acceptance.json`。本轮只验证 UI，未重新执行安装或 Desktop 推理；保留旧包、用户数据及未提交修改。

## 前轮界面 · 恢复首页与 R6 居中弹窗

**2026-10-05**：按用户要求撤回 R5 首页按钮与焦点改版，恢复 R4 首页排版。版本、目录与状态详情改为软件中间的弹窗，保留标签、分组、内部滚动，增加遮罩；背景操作禁用，关闭按钮、Esc 或点击遮罩关闭。见[当前记录](CENTERED_DETAILS_2026-10-05.md)。

推荐 [R6 便携包](../artifacts/portable/FlowPack-UI-Portable-Centered-20261005-R6.zip)，106394302 字节，SHA-256 `5DE71945F7CAFFAB44663C9F52AFC5F8FAE1362C9042D4C9C13054CBAB6461C3`。UI 专项 **10 通过**，最终 Release **367 通过、0 失败、5 专项跳过**（372 总计）。最新汇总：`artifacts/acceptance/centered-details-20261005/final-acceptance.json`。下方 R5 已撤回，R4 右侧面板已由居中弹窗替代；旧包和验收事实继续保留。

## 历史补充 · 首页按钮层级与 R5（已撤回）

**2026-10-05**：首页主要操作“导入资源 / 导出资源”移到最上方并放大；三个文件夹入口合并为“打开文件夹”，版本与目录合并为“实例详情”，扫描和关联目录收进“实例操作”。按钮分为 48 / 40 / 32 DIP 三档。点击普通文字或空白处释放旧焦点外框，资源勾选保持，键盘焦点显示保留。详见[本轮记录](HOME_PRIORITY_2026-10-05.md)。

最新推荐 [R5 便携包](../artifacts/portable/FlowPack-UI-Portable-HomePriority-20261005-R5.zip)，106400328 字节，SHA-256 `FBF1B41E2CD36C5B20C0668A925AB8FF4000ADC7389A2A9B1917ABA0EE8BF7DC`。UI 专项 **10 通过**，标准 Release **367 通过、0 失败、5 专项跳过**（372 总计）。本轮没有执行安装或推理；R4/R3 及其证据保留。汇总：`artifacts/acceptance/home-priority-20261005/final-acceptance.json`。

## 历史补充 · 二级界面统一与 R4（右侧面板已替换）

**2026-10-05**：文件夹菜单、子菜单、下拉框、提示框和折叠区已统一共享主题与圆角；首页版本、目录和分组状态改成独立侧面板，内部滚动，返回或 Esc 关闭。详见[二级界面记录](SECONDARY_UI_2026-10-05.md)。

最新推荐测试包为 [FlowPack-UI-Portable-Secondary-20261005-R4.zip](../artifacts/portable/FlowPack-UI-Portable-Secondary-20261005-R4.zip)，**106395034 字节**，SHA-256 `47B69FABA04FCD34D1820B533B1AF73AFEA90CA8475B6C7BEF78EEBACA8D3E4F`。本轮标准 Release **367 通过、0 失败、5 专项跳过**（372 总计）；二级 UI 专项 **10 通过**。解压后实际 App 的二级入口、菜单、Esc、导航关闭、页面导出与自动导入预览均通过，App/Worker 正常退出。

本轮是 UI 补充，没有重新安装或启动 Desktop 运行工作流；下方真实安装与输出证据仍对应 R3。源码仍为 `0.0.5`，旧包与数据保留，未提交、未正式发布。最新汇总为 `artifacts/acceptance/secondary-ui-20261005/final-acceptance.json`。

## 前轮简化与安装基础记录

核对日期：**2026-10-05**。本轮已执行界面简化、Worker 协议与安装能力修复、隔离资源准备、实际安装和真实工作流验证。源码仍为 **0.0.5**；保留现有未提交修改、用户数据、旧包与历史证据，没有正式发布或 Figma 云端同步。

## 当前实现

| 范围 | 已落到源码的行为 |
|---|---|
| 资源库 | 只有工作流、模型、节点三个分类；圆角矩形内容区；深色树正文为浅色、辅助文字为灰色；点击名称看详情，勾选准备导出。 |
| 导出 | 所有入口进入同一页面、同一最终清单。工作流自动加入能唯一确认的依赖；用户取消后，更新计划或返回页面不会重新补回，只有“重新添加依赖”才补选。模型、节点可单独导出。 |
| 导入安装 | 选择文件、文件夹或拖入后自动分析并生成安装计划；删除步骤编号；默认使用当前实例。问题集中在“需要处理”，右侧只保留目标实例、数量、空间、当前状态和安装按钮。 |
| 设置与状态 | 设置中的“资源库”改为“存储位置”；长路径与说明收进详情。来源、实例或选择改变后，旧分析和计划不能重新启用执行按钮。 |
| 安装能力 | Worker 统一计算 `RequiresPythonDependencies`，覆盖声明、`requirements.txt` 与 `pyproject.toml`；执行前重新核验。App/Worker 使用同一协议，旧计划须重新生成。显式 `--desktop-profile` 绑定发现范围和 Worker 会话，不回落默认配置。 |

界面与资源树专项见[界面简化记录](UI_SIMPLIFICATION_2026-10-05.md)，操作说明见[使用指南](USER_GUIDE.md)，样式规则见[设计规范](../DESIGN.md)。

## 当前验证与交付进度

| 项目 | 本轮实际结果 | 尚未完成或计数边界 |
|---|---|---|
| 内置安装资格 | 当前本机 Desktop ProductVersion **1.1.4.0** 归一化登记为 `1.1.4`；`standalone-native`、`standalone-adopted` 两布局已登记文件部署与 Python 依赖能力，EvidenceId 为 `desktop-1.1.4-transfer-20261005`。 | 其他版本、未知布局或缺少配置来源仍拒绝安装；资格只来自内置登记。 |
| 候选安装闭环 | 两布局完成页面导出、自动分析、实际部署与本地复用；实际安装 `humanize 4.16.0`，保留原有 **308** 个包的版本；冲突版本被阻断。 | 证据与失败记录保留，不能把候选闭环当作最终便携包复验。 |
| 官方 Desktop 真实输出 | 官方 Desktop 启动隔离 Core；两目标真实工作流全部节点无缓存，RealESRGAN 将 8×8 输入放大为 **16×16**，自定义节点实际加载新 Python 依赖并写出环境证明。 | 工作流通过 API 提交给官方 Desktop 启动的 Core；**未完成 ComfyUI 前端点击运行验收**。 |
| 正式 R3 App/Worker | 从最终 ZIP 解压：原生布局实际 App 页面完成导出、自动导入和一次点击安装；接管布局正式 Worker 完成真实安装、复用、版本冲突拒绝，实际 App 另验证自动导入复用计划。相关验收均退出 **0**。两布局安装后再次由官方 Desktop 启动 Core，生成真实无缓存 16×16 输出并加载新依赖。 | 接管安装命令由 WPF 验收宿主发往包内 Worker；实际 App 的接管复验只预览复用，未再次安装。输出通过 API 提交，不记为前端点击运行。 |
| 标准 Release 回归 | 最终全量 **363 通过、0 失败、5 专项跳过**，总计 **368**；证据 `artifacts/acceptance/simplify-20261005/theme-full-final.trx`。主题夹具避开前序 Application 关闭后的 Window 生命周期，真实非空树和禁用清单仍执行检查。 | 专项单独计数，不与全量结果相加。两个旧 opt-in（独立 Core 夹具、前端点击运行）仍未执行；本轮 Desktop 闭环另有独立实际证据。 |
| 单独实际专项 | 真实 Python 安装、取消与修复 **2 项通过**；实际 **大于 4 GiB ZIP64** 往返 **1 项通过**；文件部署 **3 个强制中断阶段**及恢复均通过。 | 默认跳过的专项另有实际执行证据；不能把 `NotExecuted` 改写成全量已执行。 |
| 最终 R3 便携包 | [FlowPack-UI-Portable-Simplified-20261005-R3.zip](../artifacts/portable/FlowPack-UI-Portable-Simplified-20261005-R3.zip)，**106383545 字节**，SHA-256 `145E270AB32E1382D7F7F56EE56E90441B8078758484C6F46FB3C32B8CCA2EDA`。构建、解压启动及上述正式流程已验证。 | 完整解压后双击 `Open-FlowPack.cmd`。旧包与失败证据继续保留；R3 App/Worker 二进制哈希见 `portable-R3-verification.json`。 |

安装资格、运行输出及验证边界见[Desktop 1.1.4 验收记录](DESKTOP_1_1_4_TRANSFER_ACCEPTANCE.md)。当前隔离范围位于 `artifacts/acceptance/simplify-20261005/runtime-617bf9742aed42eeaab7376b15c5ec8a/`，界面专项位于 `artifacts/acceptance/simplification-20261005/`；生产配置、模型、节点和数据没有作为安装目标。

本轮源码、便携包、实际安装与 API 真实输出已有上述完成证据。多显示器与操作系统 DPI 切换、ComfyUI 前端点击运行等边界继续保留，不能把它们记为已完成。

最终汇总见 `artifacts/acceptance/simplify-20261005/final-acceptance.json`。隔离核验确认原生产配置两文件哈希未变，本轮拥有的 App、Worker、Desktop 与 Python 进程全部退出；未提交、未正式发布。

## 接手时的边界

- 保留工作区未提交修改，不清理或替换用户数据、旧包及失败证据。此次交付为源码、验证证据和便携测试包，不包含正式 Release 或 Figma 云端同步。
- 正式流程证据：原生 App `portable-ui-runs/0fcc8178b5dc43239b3755b040ce4fa4/`，接管 Worker `transfer-runs/bbb2349c70bb49b7b6c651d0b683fbbf/`，接管 App `portable-ui-runs/8af3aa4b30e149cf90bde65eea4d98e7/`；相对路径均位于上述 runtime 范围。不要用早期失败运行或旧包覆盖最终记录。
- 真实 Windows 多显示器移动、系统 DPI 切换仍未完成；已有指定 DPI 的 WPF 位图渲染不等于操作系统层面的验收。前端点击运行、安装器完整升级、干净机器安装卸载也不能从当前证据推定通过。
- 继续开发先读本页、[界面简化记录](UI_SIMPLIFICATION_2026-10-05.md)、[Desktop 资格证据](DESKTOP_1_1_4_TRANSFER_ACCEPTANCE.md)和[根交接](../HANDOFF.md)，再核对当前源码。

---

## 历史归档 · 本轮实施前的文档整理快照

以下原文描述当时状态，仅供追溯。其“只整理文档”“资格为空”、旧导出流程、测试数量和推荐包均已由本页前面的本轮记录更新，不作为当前结论。

核对日期：**2026-10-05**。本次工作只整理项目文档，读取当前源码和已有本地证据，核对便携 ZIP 大小与 SHA-256；**没有重新构建、运行软件测试、安装或发布**。

## 当前结论

界面与资源转移改版已落到 WPF 源码，并有自包含便携测试包。当前还不能视为全部验收完成：Figma 云端新页未创建，生产自动安装资格表为空，真实环境完整转移等专项验收仍待完成。

- 源码版本仍为 **0.0.5**，由 `VERSION` 与 `Directory.Build.props` 一致确认；本轮改版没有增加版本号，也没有发布为新的正式版本。
- 工作区保留此前的已修改及未跟踪文件，尚未提交。整理文档没有清理、覆盖或回退这些改动，也没有修改用户资源及旧记录。
- [2026-09-30 发布核验记录](RELEASE_STATUS_2026-09-30.md)记载当时已发布的 0.0.5。该历史安装器不能代表本轮工作区代码；本次没有重新查询远端 Release、CI 或更新服务。
- 当前便携包适合免安装测试；生产安装仍受 Desktop 版本、布局和能力验收门禁限制。默认资格表为空，不能把生成安装计划当作已获准安装。

## 已确认计划的落实情况

| 计划项 | 当前实现与证据 | 边界或剩余工作 |
|---|---|---|
| 首页布局与按钮 | 白底黑字按钮；实例名独立行；版本和路径详情；文件夹直达仅在首页；右侧独立状态区 | 原生窗口尺寸检查已有记录，真实系统 DPI 仍待验收 |
| 资源库导航与搜索 | “我的资源”改为“资源库”；搜索位于右上角；移除扫描、导入按钮和数量页脚 | 扫描入口留在首页 |
| 资源层级与依赖 | 工作流、模型、节点按实例目录组织；共享模型来源单列；节点类型归属节点包；右侧显示位置、可用情况和工作流引用 | 依赖存在歧义时不自动猜测 |
| 前端列表与磁盘工作流 | 核实运行实例和用户上下文后查询前端用户数据；成功核对后才列入“仅在磁盘发现的工作流” | 离线、错误实例、用户不匹配或查询失败保持“尚未核对”，不推断已删除 |
| 工作流、模型、节点独立导出 | 参考工作流与实际载荷分开；仅节点、仅模型、仅工作流及混合导出均有回归记录；去重并核对 ZIP 清单 | 只校验实际选中资源；没有把未选中的缺失模型作为阻止节点导出的原因 |
| 统一导入 | 文件、目录及单来源拖放统一识别 ZIP、工作流和旧格式；内容、依赖、固定摘要与底部历史记录共用流程 | 历史来源需仍可读取；在线清单声明不等于实际载荷 |
| 实例版本 | 显示 Core 与 Desktop 版本；未确认值明确标记；运行与磁盘 Core 不同则在详情区分 | 在线信息须属于所选实例，不能沿用其他实例的版本 |
| 设置与日志 | 侧边导航、界面偏好、实例、存储资源库、日志、关于作者；日志强度及 7/10/15/30 天保留、一键清理 | 颜色逐项调整已从设置页面移除；日志清理不清理资源库 |
| 关于作者与更新 | GitHub 链接、复制链接及“检查更新 → 下载更新”单按钮状态 | 下载后仍走正式安装器；完整升级交接与数据保留验收未完成 |
| 实时进度与响应 | 扫描、哈希、解压、导出、安装及下载报告阶段；已知总量显示阶段百分比，未知总量显示活动条；全局跨页可见 | 取消仅在任务支持时可用；没有编造总百分比 |
| 大列表与后台工作 | 文件读取、哈希、分析、复制在后台执行；树按展开实现；列表虚拟化；扫描结果分批更新 | 慢速文件操作下窗口响应已有本地验证，不能替代所有真实大库测试 |
| 设计规范与 Skill | 已建立 [DESIGN.md](../DESIGN.md)，UI、可访问性及 Figma 技能的落实映射见[改版记录](RESOURCE_TRANSFER_2026-10-05.md) | Figma 相关技能已用于本地设计源准备，云端交付未完成 |
| 便携包与实时预览 | R2 ZIP 包含 App、Worker 和运行时；源码预览保存后编译并重启窗口，失败保留旧窗口 | 便携包是固定快照；预览重启会中断预览任务，不是窗口内热刷新 |

此表概述本轮计划，不替代或改写 [REQUIREMENTS_ACCEPTANCE.md](../REQUIREMENTS_ACCEPTANCE.md) 的长期验收编号与原有结论。

## 已有本地验证证据

证据目录：[artifacts/acceptance/resource-transfer-20261005](../artifacts/acceptance/resource-transfer-20261005/)。以下数字来自现有 TRX，本次只读取结果。

| 证据 | 已有结果 | 说明 |
|---|---|---|
| [transfer-acceptance-final.trx](../artifacts/acceptance/resource-transfer-20261005/transfer-acceptance-final.trx) | 总计 338；333 通过、0 失败、5 跳过 | 全量 Release 回归；跳过条目的实际结果为 `NotExecuted` |
| [keyboard-native.trx](../artifacts/acceptance/resource-transfer-20261005/keyboard-native.trx) | 1 通过、0 失败 | 后补的原生 WPF 键盘焦点检查，单独计数，不改写上一行全量结果 |
| [ui-final](../artifacts/acceptance/resource-transfer-20261005/ui-final/) | 原生 WPF 截图与布局验证记录 | 浅色、深色、960×640、1280×800、1600×900、长名称及详情展开；高对比度使用色板夹具 |
| [portable-verification.json](../artifacts/acceptance/resource-transfer-20261005/portable-verification.json) | App/Worker 依赖匹配、包含运行时、不包含测试 Data | ZIP 共 619 个条目 |
| [portable-launch.json](../artifacts/acceptance/resource-transfer-20261005/portable-launch.json) | 从 R2 ZIP 解压后启动，窗口响应、实例扫描完成 | 这是之前的真实启动记录，本次没有重新启动 |

全量回归包含独立与混合导出、ZIP 清单与导入往返、共享节点去重、未选中依赖、旧 JSON 清单、离线和错误实例分类、历史与进度字段重启保留、取消及恢复。WPF 验证还覆盖慢速真实哈希期间的 Dispatcher 心跳、页面导航、阶段中间进度及失败提示。

五个未执行专项分别为：

1. 真实 Desktop 启动、资源绑定及成功推理输出。
2. 两个隔离官方 Core 之间的导出、安装与真实模型/自定义节点运行。
3. 真实兼容 Python wheel 安装及已有版本替换限制。
4. 真实 wheel 部分安装中断、Worker 重启与修复流程。
5. 实际大于 4 GiB 的 ZIP64 导出、导入及哈希一致性。

截图和自动化通过不等于上述专项通过。现有高对比度夹具也不能等同于切换 Windows 系统高对比度后的完整人工验收。

## 便携测试交付

当前推荐包：[FlowPack-UI-Portable-Resource-Transfer-20261005-R2.zip](../artifacts/portable/FlowPack-UI-Portable-Resource-Transfer-20261005-R2.zip)。

| 项目 | 已核对值 |
|---|---|
| 仓库相对路径 | `artifacts/portable/FlowPack-UI-Portable-Resource-Transfer-20261005-R2.zip` |
| 大小 | **103035139 字节** |
| SHA-256 | `5A235E22CC3A074BC21F61338B34ECFB0F7520003704FD07E416B5ABB08A6A1F` |
| 启动方式 | 完整解压后双击 `Open-FlowPack.cmd`，无需另装 .NET |
| 默认数据 | 解压目录内的 `Data/Preferences`、`Data/Library`、`Data/Logs` |

本次重新读取 ZIP 大小并计算 SHA-256，结果与已有验证记录及旁边的 `.sha256` 文件一致。旧包和测试数据仍保留。源码实时预览的操作与停止入口见[便携测试与实时预览](PORTABLE_PREVIEW.md)。

## Figma 交付状态

[现有 Figma 文件](https://www.figma.com/design/ATifwFUoSDhcXPgeCRVgfS)的旧稿保留，**云端改版新页没有创建**。前轮记录显示 MCP Starter 额度及 Code Connect 席位限制，浏览器兜底读取也超时；本次文档整理没有重新调用 Figma。

本地 [设计源目录](design/resource-transfer-20261005/README.md)包含 22 张可编辑文字/矢量 SVG、`design-source.json` 及原生 Figma 开发插件。插件只做过语法检查，**尚未在 Figma 执行**。因此未完成云端原生图层、自动布局、变量、复用组件及与 WPF 的最终一致性验收，不能把本地源文件记为已同步。

## 下一步与完成条件

| 待办 | 完成条件 |
|---|---|
| Figma 云端落稿 | 在原文件新增改版页并保留旧稿；实际运行插件或使用可用编辑接口；核对可编辑文字、自动布局、组件、颜色模式及主要状态，并与 WPF 逐页对照 |
| 真实 Windows 界面验收 | 在目标系统实际切换 DPI、系统高对比度与主题；逐页检查键盘、长名称、展开详情及关键操作，保留窗口尺寸和系统设置证据 |
| 完整升级与数据保留 | 在隔离环境从旧版走“检查 → 下载 → 安装器 → 重启”，核对 App/Worker 版本、原配置、资源库和记录，另测失败或取消后可继续使用 |
| 干净机器安装与卸载 | 在干净 Windows x64 环境验证安装、首次启动、Explorer 交互、卸载与数据保留选择；记录运行时及签名提示，不能只依赖构建产物存在 |
| 实例完整转移与安装资格 | 使用隔离 Desktop/Core 实例完成导出、导入、计划、安装、节点加载及真实工作流输出；记录版本/布局和证据后，才登记相应能力资格 |
| 未执行的 wheel 与 ZIP64 专项 | 具备受控测试环境和资源后实际运行对应专项，保存结果；失败、中断和恢复须核对目标文件及 Python 环境，不仅检查任务状态 |
| 后续发布 | 获得发布任务后，从保留的工作区形成可追踪源码快照，按版本规则重新构建、验证、发布并核对远端资产；本次文档工作不触发发布 |

继续开发前先阅读[使用指南](USER_GUIDE.md)、[改版记录](RESOURCE_TRANSFER_2026-10-05.md)及[历史交接](../HANDOFF.md)，以当前源码和本页列出的证据区分“已实现”“已验证”和“待验收”。

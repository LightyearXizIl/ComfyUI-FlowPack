# FlowPack 资源转移改版设计源

本地 SVG 的文字与矢量可编辑；`design-source.json` 保存语义层级。`figma-plugin/` 是用于在现有 Figma 文件创建新页面的原生开发插件源文件，使用文本、自动布局、颜色变量与复用按钮组件，保留旧稿。

**云端状态：未同步。** 本轮 Figma MCP 返回 Starter 调用额度耗尽，直接编辑也失败。插件仅通过 JavaScript 语法检查，尚未在 Figma 运行，不能替代云端图层验收。按钮可编辑，但原生字体可能随 Figma 可用字体变化。画面里的资源、节点名称是设计示例，版本未确认不填虚构版本号。

## 使用插件

在 https://www.figma.com/design/ATifwFUoSDhcXPgeCRVgfS 打开现有文件，从 Figma 的开发插件菜单导入本目录 `figma-plugin/manifest.json`，运行后会新增“FlowPack · 资源转移改版 · 2026-10-05”页面。需要可用的 Microsoft YaHei UI、Microsoft YaHei 或 Noto Sans SC 字体。再次运行会再新增一页，旧稿不会被覆盖。

包含浅色、深色首页、工作流/模型/节点目录树、仅节点独立导出、统一导入、日志和关于作者，以及加载、离线、空内容、错误状态。WPF 的真实截图在 `artifacts/acceptance/resource-transfer-20261005/ui-final`，只作实现验证，未放入设计图层。

通过 `python build/Generate-TransferDesign.py` 可重新生成本地设计源。SVG 负责可编辑视觉参考；WPF 实现与 DESIGN.md 是产品行为和共享令牌的依据，源码映射见 `docs/RESOURCE_TRANSFER_2026-10-05.md`。

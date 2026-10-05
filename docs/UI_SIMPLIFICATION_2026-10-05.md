# 界面简化与资源树视觉验证

本页记录当前源码的界面简化及资源树专项证据。使用流程见[使用指南](USER_GUIDE.md)，样式规则见[设计规范](../DESIGN.md)；完整安装、便携包与整体交接结论由[项目状态](PROJECT_STATUS.md)单独记录。

## 当前界面

- 资源库只有工作流、模型、节点三个分类，采用共享圆角卡片和固定选择页脚。点击名称查看详情，勾选决定导出内容。
- 导出使用同一份清单，进入时按工作流自动加入能唯一确认的依赖；同一次选择中只自动补选一次，用户取消后不会因更新计划或返回页面恢复。“重新添加依赖”才重新补选。
- 导入来源后自动分析并检查安装计划，默认使用当前实例；页面取消步骤编号，问题集中在“需要处理”，路径与较长说明放入详情。
- 首页保留白底黑字按钮，主入口叫“导入资源”“导出资源”；设置中的数据目录区改为“存储位置”。

## 已执行的资源树专项

`ThemeResourceTests` 读取实际 `App.xaml` 共享资源和 `ResourceBrowserView.xaml` 的布局，使用独立原生窗口 `HwndSource` 承载 WPF 内容，填充目录、长名称模型和勾选框后执行检查。它也加载实际导出页面的非空清单。本轮单独运行结果为 **1 通过、0 失败、0 跳过**；完整 Release 为 **363 通过、0 失败、5 专项跳过**，专项执行和真实安装由项目状态单独记录。

覆盖内容：

- 浅色 → 深色 → 浅色动态切换，目录和资源正文、展开箭头使用对应文字色。
- 选中及失焦选中仍可读；键盘焦点边框显示与隐藏正确，禁用时文字和箭头使用辅助色，勾选框同步禁用。
- 长名称和勾选框在资源树可见范围内；共享控件圆角在资源更新后重新生效。
- 导出清单在浅色、深色、启用、禁用状态均保留当前主题；实际原生保存对话框打开期间，禁用清单保持深灰背景、可读名称和勾选状态。对话框所有者及自动取消均绑定测试自己的窗口，不操作其他窗口。
- 对同一 WPF 视觉树分别以 **96、120、144 DPI** 输出位图；像素宽高按 `DIP × DPI / 96` 计算，渲染器的像素尺寸与 DPI 均进行断言。

| 输出 DPI | 960×640 DIP 位图尺寸 | 深浅色截图 |
|---|---|---|
| 96 | 960×640 像素 | `ResourceTree-*-Populated-96Dpi.png` |
| 120 | 1200×800 像素 | `ResourceTree-*-Populated-120Dpi.png` |
| 144 | 1440×960 像素 | `ResourceTree-*-Populated-144Dpi.png` |

最终结果为 `artifacts/acceptance/simplify-20261005/theme-final-fix.trx`、`theme-ui-final.trx` 与 `theme-full-final.trx`。最终资源树及清单截图在该目录的 `ui-theme-final/`，其中 `ExportList-Dark-NativeSaveDialog-96Dpi.png` 记录实际对话框期间的清单。旧专项记录与六张树截图继续保留在 `artifacts/acceptance/simplification-20261005/`。PNG 保存时 DPI 元数据会有小数舍入，120 和 144 DPI 文件解码后分别约为 119.990 与 143.993 DPI；位图像素尺寸已核对。

曾在完整套件中发现主题夹具的空视觉树：其他测试已关闭全局 WPF Application，随后 `Window.Show` 没有生成内容。改为测试自己的 `HwndSource`、显式布局和 Dispatcher 空闲处理后，全套通过；没有跳过断言或使用空列表截图。

这组证据是 **WPF 按指定 DPI 输出**。窗口布局仍使用测试所在显示器的 DPI，未修改 Windows 全局缩放，也未进行真实多显示器切换；它不能替代真实屏幕缩放、跨屏移动和系统 DPI 切换验收。

## 复现命令

在项目根目录使用 PowerShell：

```powershell
$env:FLOWPACK_UI_ARTIFACTS = Join-Path (Get-Location) 'artifacts/acceptance/simplification-20261005/ui-theme'
dotnet test 'src/FlowPack.Tests/FlowPack.Tests.csproj' --configuration Release --filter 'FullyQualifiedName~ThemeResourceTests' --artifacts-path 'artifacts/build/theme-agent' --logger 'trx;LogFileName=theme-resource.trx' --results-directory 'artifacts/acceptance/simplification-20261005'
```

完整 Release 回归会包含此测试；这里保留单独结果，避免将专项证据与其他测试数量相加或重复计数。

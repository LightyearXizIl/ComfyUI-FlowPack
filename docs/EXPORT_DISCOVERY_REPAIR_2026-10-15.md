# 导出选择与实例发现修复记录

当前工作区已实现用户确认的修复计划；下方记录的是版本升级前的便携测试包。随后按用户要求同步发布源码为 0.0.8，App/Worker 协议为 7，正式构建与远端发布状态见[项目状态](PROJECT_STATUS.md)。

## 已实现行为

- 导出页只展示当前勾选资源。唯一依赖自动加入并标记来源；取消后立即移除，只有“重新添加依赖”会主动恢复排除项。
- 普通导出不再从旧草稿恢复选择，也不写回旧草稿。同实例重扫保留当前路径选择，切换实例清理原选择。
- 勾选只更新清单和统计，不触发文件核算。首次进入、“更新导出预览”和导出前核算当前文件；取消旧任务并丢弃旧输入结果。
- 未改变的路径、大小及修改时间复用进程内哈希；写 ZIP 时仍逐字节校验，源变化时删除本次临时 ZIP。预览标为资源总字节数。
- 实例发现返回配置根、检查来源、跳过原因与错误。补充已知目录、Desktop 进程、自定义 user-data 配置；未登记候选保持只读。坏设置、旧配置或单条登记不隐藏其他实例。
- 跨 profile 相同 ID、不同目录时保留先发现项并显示冲突诊断，避免 Worker 按 ID 查找时抛异常。显式 profile 不混入其他配置。
- 首页“检测详情”使用现有居中弹窗，完整记录可复制。旧协议后台给出明确版本不兼容提示。

## 验证结果

| 检查 | 本轮结果 | 证据与边界 |
|---|---|---|
| 全量 Release 回归 | 380 通过、0 失败、5 跳过 | `artifacts/verification/export-discovery-tests/flowpack-repair.trx`；5 项为按需 Desktop、独立 Core、Python 与 ZIP64 专项。 |
| 最终边界回归 | 23 通过、0 失败 | `final-boundaries.trx`，含重复 ID 与同实例重扫选择保持；与全量有重叠，不相加。 |
| Release 解决方案构建 | 0 警告、0 错误 | `dotnet build FlowPack.sln -c Release --no-restore`。 |
| WPF 窗口回归 | 全量中的 `UiShellTests` 通过 | 检查页面布局、共享样式、命令绑定、焦点与新预览入口；不等同外部点击验收。 |
| 便携包解压启动 | 通过 | 从 ZIP 重新完整解压，实际 App 完成隔离实例发现和资源扫描，日志在验收目录。 |
| 包内 Worker 端到端 | 通过 | 实际包内 Worker 发现实例并给出 cloud 跳过原因；只选择一个工作流，实际 ZIP 与 manifest 均只有这一项载荷。`artifacts/verification/export-discovery-ui/packaged-worker-result.json`。 |
| computer-use 点击验收 | 未完成 | 返回 `window id ... no longer belongs to ...; current owner is ...`，重新列举并绑定后仍失败；未绕过工具故障使用 PowerShell UIA。 |
| 另一台电脑现场验证 | 待完成 | 使用本轮新包检查下拉框与“检测详情”；当前没有该电脑配置和运行用户的证据，不能判断其唯一根因。 |

测试命令：

```powershell
dotnet test FlowPack.sln -c Release --no-restore --logger "trx;LogFileName=flowpack-repair.trx" --results-directory artifacts\verification\export-discovery-tests
dotnet build FlowPack.sln -c Release --no-restore
dotnet test src\FlowPack.Tests\FlowPack.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~TransferFlowTests|FullyQualifiedName~DesktopInstanceDiscoveryTests|FullyQualifiedName~DiscoveryPresentationTests" --logger "trx;LogFileName=final-boundaries.trx" --results-directory artifacts\verification\export-discovery-tests
```

## 便携测试包

[FlowPack-ExportDiscovery-20261015.zip](../artifacts/portable/FlowPack-ExportDiscovery-20261015.zip)，106451461 字节。

SHA-256：`5509B44917D7C197FB8D7DAB5D635E7CF6D7B6441B36D5D99D4C95FF8FDEAAD8`；与 `.sha256` 文件一致。完整解压后运行 `Open-FlowPack.cmd`，使用解压根目录下独立的 Data。

包内 App/Worker 与解压副本哈希一致。测试 App 使用独立夹具、资源库和配置，未操作用户现有资源；测试 Worker 已通过空闲退出命令结束。

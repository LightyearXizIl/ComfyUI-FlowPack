# 0.0.4 石墨色界面与核心收尾记录

2026-09-12 最新状态：用户确认“安装”后，隔离页面实际提交被 Worker 以“安装计划不是本 Worker 生成的原始计划”拒绝；四个目标载荷均未写入。计划比较已改为 JSON 语义一致性校验，仍要求 Worker 留存的完整计划及当前部署资格；新增格式差异/内容篡改/版本变化回归，Release 285通过、4专项跳过（artifacts/acceptance/core-completion-audit/plan-equality-regression.trx）。修正版隔离宿主已启动，但用户随后按 Esc 停止界面操作，尚未重新安装。需要用户明确恢复操作后继续真实闭环。264E…安装器不包含此次比较修正，未重新打包，生产安装资格仍为空。下文相关“当前源码已全部打包”“安装尚未执行/待首次确认”为历史状态。

日期：2026-09-12。状态：本地候选、未提交/推送/发布；本记录补充而不覆盖此前核心专项证据。

## 高对比度选中状态

此前追加的界面修正：SelectionBrush/SelectionTextBrush独立表示下拉选中/高亮与表格选中。普通主题沿用石墨软底和正文色；高对比度由HighContrastPalette使用系统WindowText/Window反转，避免AccentSoft与窗口背景合并后选中不可见。apple-design用于对比度与状态反馈判断；截图contrast-selection-ui/HighContrast-Dark.png及HighContrast-Light.png为实际WPF独立控件夹具，未切换Windows设置，不宣称全页面高对比度通过。常规282/4，当前721B…安装器尚未包含。

## 实际复制中途终止

RecoveryCrashAcceptance 新增 during-copy：流式生成512MiB合成源，不给生产安装服务增加暂停钩子；父进程监测目标目录实际*.tmp文件长度介于0与源长之间，读取Running/Intent日志后终止已持有的测试子进程。退出后再次要求暂存严格小于源文件，否则测试失败，不把错过复制窗口当通过。

实测部分53,739,520字节，SHA-256 `1ACFCF4C665AFA4AC2780143B7BFDB25532E3B29A1B57169D95CE90F2F68CCD8`；完整源SHA-256 `BB97CD6830C862B9CCEDD9CBBA9C9EAE0B2F99373273FA772C5423EEB8DAF400`。恢复后部分哈希不变，正式目标不存在，NeedsReview；重复恢复稳定、Worker初始化成功。提交前/后两个检查点也重跑通过，共3/3。三份JSON在 artifacts/acceptance/crash-recovery-copy-stage，原始临时文件保留在各报告root中。不是断电或Python实际安装失败测试，正式Desktop页面安装仍待动作时确认。

## 实际安装服务子进程的提交边界中断

新增源码专用 RecoveryCrashAcceptance，不进入产品安装器。父进程新建 FlowPack-crash-GUID 临时目录并写范围标记；子进程只接受此临时目录/阶段，使用实际安装服务写256KiB确定字节。标记经flush和原子rename后父进程读取检查点journal，只终止自己启动的Process对象，不枚举或结束其他应用。

- before-commit：终止前 Running/Intent，复制文件已校验但尚未rename；恢复 NotPresent，将暂存保留到恢复目录，哈希31A1F9DEA0169551092D05E8BF4A446228C8C3EB4C9B713C66ADCB7FD53C89BE。
- after-commit：终止前 InstallingPython/Committed，目标文件已提交，Python回调尚未执行任何Python；恢复 VerifiedPresent，目标哈希相同。
- 两个子进程退出码均-1；重复恢复JSON一致，PersistentWorker初始化成功；状态保持NeedsReview，没有声称Python已回滚。
- 证据：artifacts/acceptance/crash-recovery-checkpoints/before-commit.json 与 after-commit.json；对应原始临时目录保留在结果root字段，未删除。
- 重跑入口：先构建 FlowPack.Smoke 到独立目录，再执行 `FlowPack.Smoke.exe --acceptance-crash-recovery`。本轮两场景通过，不是图形界面安装、复制中途或真实Python安装进程强杀证据。

## 无原任务的 Python 恢复问题

Python 恢复日志可能保留而旧 Worker 任务行缺失。当前初始化为每项 PythonRecoveryIssue 建立稳定 python-recovery-* 的 install.recovery 记录，NeedsReview、持久错误、不可直接重试；重复启动保持同一任务 ID/创建时间。原任务存在时仍保持其修复状态，不能用问题记录代替环境恢复。4类坏日志测试同时包含有效中断记录，验证两个问题均可通过数据库/任务列表读取且重启不重复；与部署坏日志共14/14，完整常规262/4、diff检查通过。未进行实际 Python 子进程强杀或人工修复 UI 验收，安装器未包含此后续改动。

## 真实页面导出与导入已完成，安装待确认

重新枚举确认 current-v5-fixed 窗口461628及另存为仍在，默认目录 D:\16054\Documents 无同名 ZIP。未重试键盘输入，而使用当次模态截图坐标点击保存；页面显示 ZIP 已导出。生成文件67,063,432字节，SHA-256 `69B226D6231F13940054910EE75A45BAB22EA3E00D9541B51A9792C768B9EB9F`。将其复制到原隔离fixture的 `app-runs/045e9cd955e04692ac7ea75aff75b563/gui-roundtrip.zip`，D盘原产物保留。

完整读取 ZIP 的工作流417字节、节点318字节、模型67,061,725字节及输入PNG170字节；四项 SHA-256 与页面导出计划一致，另含634字节 flowpack-manifest.json。节点源码未绑定运行信息的 Unresolved 说明仍保留，未伪称已经加载。

随后在真实页面关闭导出预览，选择 FlowPack acceptance adopted target；核对描述为E盘target核心、C盘Acceptance/adopted-4cc5e414数据及Python。扫描后工作流列表为空。点击导入文件，在默认D盘目录双击刚导出的ZIP；页面进入导入安装，四项已识别且勾选，工作流依赖显示已暂存待安装，预览新增4项/复用0/63.96MB。当前尚未点击安装，需要 computer-use 动作时确认。宿主为早先 current-v5-fixed，证据不可冒充最新候选安装器生命周期测试。原生产实例未操作，生产资格仍为空。

## 设置页与默认按钮主题补齐

采用 ui-skills-root 路由及 apple-design：未显式指定的按钮也继承 SecondaryButton 的语义配色/焦点/反馈，保留显式主按钮和图标按钮。更新操作内容宽度、可换行；实例管理展示当前实例并接重新检测/手动关联命令。WPF 全路由渲染测试增加设置按钮颜色、焦点、宽度、实例命令和空态断言，浅/深截图均已查看。首轮截图发现新提示颜色键不存在及空绑定提示缺失，修复后再次渲染查看通过。完整常规262/4，最终修正后 UI1/1，diff检查通过；证据目录 `artifacts/acceptance/settings-graphite-ui`。未宣称 DPI、高对比度、图形界面安装或更新生命周期通过。

## 后选实例的本地依赖复用

先导入再选择实例，以及恢复导入会话时，使用当前扫描索引执行本地候选校验复用，重新生成依赖和已选资源预览。切换实例清空旧索引与依赖，扫描结果检查选择身份；本地复用还要求索引实例 ID/配置指纹符合当前实例。不会因缺少下载 URL 跳过本地检查，也不会直接发起安装。

ImportInstanceRefreshTests 通过实际本地 Named Pipe 和 PersistentWorker 驱动 ShellViewModel：无 URL 清单先保持待补全，扫描后复用本地工作流、哈希一致，未创建下载/安装任务，取消实例清除旧索引。测试直接调用扫描方法避免 fire-and-forget 测试竞争，不是 UI 点击实测。常规 **262/4**，diff 检查通过；安装器及生产资格未改变。

## 安装坏日志隔离与写入阻断

安装 journal 读取现在逐份容错。校验文件名/计划 ID、一致的暂存路径、非空文件记录、允许状态及重复目标；先验证整份记录，不能处理到第二个坏条目时已经移动第一个暂存文件。即使标为 FilesDeployed，也必须结构有效。失败时保留日志与未处理暂存、返回 NeedsRepair，并继续其他日志；Worker 建立稳定 ID 的 install.recovery 问题任务，重复启动不重复创建，原因常驻且不能直接重试。新安装取得资源锁后重新检查日志，无法核验则在写入目标和创建新 journal 前拒绝。

新增 10 类损坏记录回归，连同备份冲突专项 12/12；完整常规 **261 通过 / 4 专项默认跳过**。首次测试因测试夹具重复使用被 Worker 释放的数据库实例失败，改为每次启动新数据库对象后通过；这不是生产数据库故障。未进行实际进程强杀、人工修复界面或 Desktop 页面安装验收，生产资格与旧安装器不变。

## 重复文件恢复的备份冲突

恢复目录已有备份且目标侧仍有暂存时，旧实现 File.Move(overwrite:false) 抛出异常并阻断启动。当前保留两份文件，记录路径、保持 NeedsReview，不比较后擅自删除任一副本；也处理检查后备份出现的竞争。再次恢复保持相同提示，两个相同/不同内容场景及 Worker 初始化回归通过。常规251/4，diff检查通过。未声称物理断电、跨盘移动中断矩阵或人工修复入口已完成。

## Python 坏日志恢复

逐条恢复日志时隔离 JSON/结构/状态及文件读取错误，保留原始坏文件；未知状态返回修复问题而不是忽略，归属未知时阻止所有后续 Python 修改，仍允许 Worker 启动及只读检查。有效 Installing 记录继续转 NeedsRepair，不进行自动卸载或回滚声明。4 类回归同时验证其他记录不被遮蔽、再次恢复、Worker 初始化与安装入口在调用解释器前被拒绝。常规249/4，专项4/4；不是物理断电或安装子进程强杀验收。损坏部署文件 journal 的独立容错与完整修复入口仍需继续。

## 新会话真实启动检查

保存焦点复核：重新枚举当前窗口 461628，激活后观察到另存为仍在，ZIP 不存在；使用当次返回的 dialog screenshot ID 点击文件名显示插入光标，但 `press_key(Control_L+a)` 后对话框失焦，文字未选中；结构化焦点仍报告 SearchEditBox。未发送路径文本、未保存、未点击安装。不能仅用文件名视觉光标推断后续键盘输入仍送达该对话框，停止重试该路径，请用户完成一次另存为；不把这一动作误记作验收完成。

修复后的 `artifacts/acceptance/hosts/current-v5-fixed` 新会话 `045e9cd955e04692ac7ea75aff75b563` 已真实启动成功并选择 source 扫描 1/1/1。页面勾选工作流生成4文件预览：upscale.json、FlowPackAcceptanceNode/__init__.py、RealESRGAN_x2plus.pth、fixture.png，63.96 MB；节点 Unresolved，不能冒充已加载。已选择带缺失说明导出，停留另存为：计划输出 `<fixture>/app-runs/045e9cd955e04692ac7ea75aff75b563/gui-roundtrip.zip`，尚未输入或保存。模态截图 ID 坐标点击可用（不是猜窗口句柄）；文件名焦点返回搜索框，因矛盾停止文本输入。此轮实际进展为启动、实例选择、勾选和关联预览，不是 ZIP 完成或安装闭环。

新宿主 `artifacts/acceptance/hosts/current-v5/FlowPack.Smoke.exe` 构建成功。run ID `9d388e600dcd401b8645d9c018b69ec5`，UI 启动 PID 42656，首次窗口句柄 14617896（继续前必须重新枚举）。旧会话 `9722589e372245ec868ceaa6dc9d283b` 只剩 Worker PID 26688，未结束或覆盖。computer-use 实际读取新首页错误“Worker 响应为空”，定位到首次会话为空的协议处理：可选导入会话调用显式允许空返回，其他调用继续严格拒绝。真实管道两条回归通过。运行中的新宿主仍为修复前构建，后续须换构建验证后再推进导出/安装。没有新增安装成功证据。

## 紧凑任务面板

任务数据更新追加：初始化后后台串行轮询 `job.list`（2 秒间隔），不进入业务忙碌状态；窗口关闭取消轮询，不停止 Worker；本窗口更新期间跳过。增量集合协调保留未变化对象与顺序、更新新增/移除/进度/失败；轮询请求期间若收到直接任务进度则丢弃旧快照。失败不清空记录，并报告连接原因；恢复清除对应连接提示。两条集合回归通过，常规 234 通过、4 专项默认跳过；仍未以实际多窗口重连证明完整恢复。

右侧 440 DIP 纵向列表已替代任务表格，保留原页面、关闭/Esc/焦点返回；状态中文化，错误不折叠，只有允许的控制操作可见，任务 ID/操作/时间折叠。字节进度绑定 Worker 实值，未知总量隐藏进度条，实色语义模板避免系统默认蓝色/绿色。WPF 真实控件在 960×640 深浅主题下验证布局、512/1024 进度、失败原因及仅“重试”可见，截图含标注测试数据，见 `artifacts/acceptance/task-panel-ui/`。完整常规 232 通过、4 专项默认跳过；不替代多 DPI/高对比度或真实下载验收。旧内部 Tasks 路由仍兼容。

## 导入会话重开恢复

- 共享模型候选追加：`OnlineLocalResourceMatcher` 使用库存类别和完整相对路径，不依据文件所在默认写入目录。类别别名归一化，类型/路径越界拒绝；App 收集有效索引文件并按绝对路径去重，只在唯一候选时自动调用 Worker 核验。多个不同候选不擅自挑选；现有安装计划服务可在有效模型搜索路径中按哈希复用，但本轮没有增加真实共享目录安装证据。7 条匹配回归通过，常规 245/4，GUI ZIP 仍待人工保存。
- 本地复用入口前移：统一导入后遍历待补全声明，即使没有来源 URL，也先按索引的精确目标文件尝试 `resource.materialize-local`，内容检查继续由 Worker 执行。无唯一候选不处理，错误保留待补全原因，不把文件夹存在等同节点已可用，已有下载 ID 不被替换。新增无 URL 成功和哈希不符失败的实际 Worker 回归，确认没有创建下载任务；常规 238/4。共享搜索路径、目录载荷及真实 UI 仍需后续验收。
- 派生计划追加修复：不只保留同一计划中的新下载，还通过保留的 Worker 转换任务输入追溯祖先，保留转换后仍待补全的关联。客户端不能自报祖先；另一次独立导入即使清单 ID 相同也不继承关联。新增实际 Worker 本地载荷转换与独立重新导入两条回归，专项 14/14，常规 **232 通过 / 4 专项默认跳过**。这是关联连续性回归，不是进程强杀、完整编辑快照或页面闭环验收。
- 并发保存增量：会话增加 Revision（旧记录缺省 0），保存与 `online.download` 共用 Worker `_control`。保存校验调用方版本，只接受当前版本，并返回新会话；同一计划中入队后添加的关联不能被旧列表清空，冲突任务关联拒绝。App 保存/恢复同步 Revision，冲突常驻提示要求重新打开恢复。不把修复扩大为完整多窗口编辑/跨计划恢复验收。保存响应发生变化，IPC 升为 v5；当前常规 **230 通过 / 4 专项默认跳过**。
- 当前额外专项：ZIP64 实际源 4,295,098,368 字节、ZIP 4,295,098,976 字节，导出及导入哈希 `137668DD4B444E0945C4426CB6CDD4AB4AA2C06E5A6503858F76529DC4DD4C61` 一致，TRX：`artifacts/acceptance/zip64-current-results/zip64-current.trx`。这是合成字节大文件归档验证，不是有效模型推理。
- 当前额外专项：隔离 venv 实际下载/安装 `six==1.17.0`，既有包版本未变，兼容要求无重复安装，`six==1.16.0` 替换要求被拒绝。TRX：`artifacts/acceptance/python-current-results/python-current.trx`；结果：`artifacts/acceptance/python-current/python-integration-8eb63fef06304dc8b272c856fe76e536/result.json`。只读借用 uv 基础 Python 创建测试 venv，不修改原 Desktop 环境。两项均 1/1，生成的大文件及 venv 已由专项清理，结果记录保留。
- 原子入队增量：`online.download` 接收计划/资源/文件名，URL 与哈希由原清单读取。Worker 在 `_control` 下检查幂等、当前会话和已有任务，调用数据库单事务同时保存 WorkerJobs 与 import_session_v1，然后才启动执行；App 已切换到该入口，不再执行入队后的第二次会话保存。任务记录保留 OnlineOrigin 用于重连幂等核验。
- 新增数据库事务成功和第二步写入拒绝两条回归，后者用 SQLite trigger 故障注入，再重开数据库验证队列写入被回滚且旧会话保留。测试连接最初启用池导致临时目录清理失败，改为不使用连接池后完整 **229 通过 / 4 专项跳过**。这证明事务边界，不是实际进程强杀或真实下载页面闭环。
- 下方所述“下载入队/保存关联间隙”已在新入口中修复；多 App 会话覆盖、用途映射/额外依赖的持久保存仍需继续。
- 新增 `ImportSessionState`：只保存原导入计划 ID、待补全资源到下载任务 ID 的映射及格式版本，不保存安装计划/能力。利用现有 SchemaInfo 可选元数据键保存，不改数据库表结构。
- Worker `import.session.save/load` 验证计划由本 Worker 生成且保留，下载任务类型、资源身份、来源 URL 和声明哈希一致。未知版本/伪造引用拒绝且保留上一次会话。
- App 导入成功、在线下载入队、在线载荷转换后保存；启动后恢复内容和下载任务状态。不沿用旧安装计划，载荷默认不勾选并显示重新核对提示，避免在陈旧路径或旧资格下直接安装。
- 新增关闭/重开真实数据库和 Worker 的恢复测试，以及不存在的计划/下载任务、未来会话版本不覆盖旧记录测试。不是 Windows 进程强制结束/UI 重启的完整实测。
- 明确缺口：用户修改的用途和额外追加依赖不在当前会话快照内；下载入队和 UI 保存关联之间仍有崩溃窗口；多 App 同库采用最后一次保存的会话，未做多窗口冲突提示。这些需继续补齐，不能宣称完整恢复已验收。

## 下载跳转与网页伪装防护

- Worker 和 Python wheel 的下载 HttpClient 关闭自动跳转；VerifiedDownloadService 逐跳验证 HTTPS、无凭据/片段，最多跟随 8 次，保留续传 Range/If-Range。最终响应仍有兜底检查。
- 响应 Content-Type 不区分大小写检查 HTML/XHTML；写入前读取至多 512 字节，识别 UTF-8 BOM/空白后常见 HTML 头。续传组合已有文件前缀和新响应检查，网页拒绝不会覆盖或追加到原暂存。此检查不是所有恶意内容检测或模型格式完整验证。
- 新增 7 项回归：三类不安全跳转不发下一请求、相对 HTTPS 跳转续传、两种伪装二进制 HTML 保留旧文件、跨续传边界 HTML 拒绝。下载专项 11/11；整体 **225 通过 / 4 专项默认跳过**。测试为响应夹具，不替代真实网络、安装器更新或 Desktop 闭环。

## 在线旧清单：统一计划与下载结果转换

- App 接通增量：移除在线清单旧入口分支；新增 `OnlineResourceRow` 与导入页“清单待补全资源”，逐项/批量下载、选择本地载荷和永久错误文案。来源/哈希在折叠详情中，沿用语义深浅色，不增加动画。`WorkerLibraryClient.WaitForJobAsync` 允许当前条目复用已提交下载任务，暂停/失败后通过任务区恢复后再转换。
- 下载前只尝试当前实例索引中与声明部署目标精确相同且类别匹配的唯一文件；调用 `resource.materialize-local` 重新验证大小/声明哈希后才复用，不在 App 直接写业务库。缺失来源仍可选本地文件；本轮未增加在线 URL 编辑/候选来源选择，不能宣称所有来源补全已完成。
- 来源按钮校验拒绝非 HTTPS、凭据、页面扩展、blob/tree 页面；节点归档需固定 GitHub 提交或清单来源哈希。特殊下载 API/重定向与更多归档类型需要后续矩阵，不将该规则视为全部网站来源适配。
- 新增 9 项 URL/固定节点来源回归，WPF 全页测试追加待补全条目、真实命令绑定和错误文本常驻断言；常规 **218 通过 / 4 专项跳过**，Worker 构建和 diff 检查通过。尚未完成真实网络下载、本地复用的实机页面闭环、导入会话重启恢复。下方旧“待接 App”条目为历史记录。
- `ResourceImportService` 识别 `.cpack.json`，解析格式 1 清单进入 `ImportPlan.OnlineManifest/PendingDownloads`。不下载、不创建暂存目录、不将远程声明加入可安装文件；旧序列化计划默认为无在线内容。
- `OnlineImportMaterializer` 重新校验真实下载文件的大小及声明 SHA-256，保持工作流原文；单文件利用既有 LegacyImportMapping 核验部署用途。归档按内容识别保留内部路径，归档哈希不当作成员文件哈希，跨类型项目要求确认，目标同名异内容明确报告。
- Worker 新增 `resource.materialize(PlanId, ResourceId, DownloadJobId)`：原计划必须来自本 Worker 完成的导入/转换任务；下载任务必须完成且来源与原声明一致，载荷必须位于本资源库下载暂存区。重新解析的工作流沿用持久快照写入路径。
- 6 项专项通过：在线计划序列化/空本地载荷、未知格式拒绝、旧计划兼容、工作流原文与来源哈希再次核验、Worker 已存下载来源匹配/不匹配。Worker 下载结果在测试中使用明确的已完成夹具，不是实际网络下载验收。
- **待续：App 待下载列表及逐项/批量按钮、来源补全、本地匹配复用和档案类型矩阵。App `.cpack.json` 重定向旧入口尚未移除，当前仅后端接通，不关闭 C03/C08。** 尚未重新构建安装器或发布。

## 跨盘接管与隔离验收宿主

- 页面推进：已实际启动 Smoke UI 与独立 Worker 宿主，run ID `9722589e372245ec868ceaa6dc9d283b`；首页发现两个隔离实例，通过页面选择 source 后扫描得到 1 工作流、1 模型、1 节点。进入资源列表、点击导出后发现两个真实问题：ComboBox 选中项显示 record 内部字符串；DataGrid 勾选首击只聚焦，导致空选择生成 0 文件预览且保存启用。
- 修复：ComboBox 模板补齐选中项模板选择器和格式；三类列表采用可直接操作的 CheckBox 模板列并提供资源名辅助标签。导出命令按类别检查已选资源，执行前再次阻断空选择，保存要求非空计划。按 apple-design 的即时反馈原则处理，不增加动画或装饰。WPF 测试新增深浅主题下对象显示文本、实际 Toggle 控件绑定及分类空选择断言，完整回归 **203 通过 / 4 专项跳过**。
- 现场边界：旧版本模态导出窗口未能由 computer-use 正确关闭（命中被报告为非目标 owned window，刷新并改用 Escape 后仍存在）。已请用户关闭该旧测试弹窗和 FlowPack 测试主窗；未要求关闭 Desktop。当前页面证据证明扫描和打开导出预览，**尚未证明修复后单击勾选、实际保存 ZIP 或导入安装闭环**。持久 Worker 不因 UI 关闭而退出，可复用上述 run ID。下文“尚未运行宿主”为此前记录。
- 测试根目录仍为 `artifacts/acceptance/core-implementation/official-core-4cc5e414738f431388fcc8aa50e5f158`；新配置 `adopted-desktop-profile`，范围说明 `acceptance-scope.json`。未改正在运行的旧测试 profile，尚需关闭旧测试 Desktop 后启动新 profile，避免源实例重复运行。
- 从既有隔离 target 的 `.venv` 复制到 `C:\Users\16054\AppData\Local\ComfyUI FlowPack\Acceptance\adopted-4cc5e414\.venv`。Robocopy 50,928 文件、5,002,151,871 字节、失败 0；该副本保留供下一步验收。Python `sys.prefix` 已指向 C 盘副本；基础解释器仍是已有 uv Python，仅作为只读基础运行时，不将它计为完整复制的独立解释器，也不对其全局安装包。
- `FlowPack.Smoke --acceptance-inspect <scope>`：两个实例的配置、路径、版本及测试范围检查通过。实际 Desktop 的 `FileVersion=260908ensm0r3cr`，`ProductVersion=1.0.47.0`；能力解析现在优先产品版本，不从未知产品版本回退继承资格，非零第四段保留精确区分。
- `FlowPack.Smoke --acceptance-ui <scope> <32位GUID>`：使用真实 MainWindow / ShellViewModel，并启动独立进程的 `PersistentWorkerService` + Named Pipe 宿主；同一个 GUID 重开同一测试资源库。偏好、数据库、任务会话都在测试根 `app-runs/<GUID>` 内。此入口仅在 Smoke 测试程序，产品 App/Worker 不接受该参数，安装器不包含 Smoke。
- 宿主要求所有实例资源路径（含额外模型路径、输入和虚拟环境）在两处指定测试根内，拒绝同名前缀、链接和非白名单实例；推理输出必须关闭共享，显式输出覆盖也核验范围。试验权限标为 `acceptance-in-progress-not-production-qualified`，不能作为生产验收证据。App 只连接预先启动的验收会话，不可切换库，失联后不启动生产 Worker。
- 构建和 scope 8 项、版本 7 项、只附着会话 1 项新增回归通过；整体 **203 通过 / 4 专项默认跳过**，`git diff --check` 通过。只读检查不是 UI/IPC 安装闭环；**新宿主 UI 尚未运行，接管实例尚未通过 Desktop 推理、真实 Worker 进程崩溃恢复及安装器升级仍未验收**。
- 原 Desktop `settings.json` / `installations.json` SHA-256 分别仍为 `40DEF31C964E367BB054B4AEE24169C514C292FACDB5105C2AE3EF182FB23C6D` / `E60E869401278B3E9FBA058425157E462DD6CA9D403540C71A82A054D4F009CD`。未重建安装器，未提交或发布。

## 任务可执行操作与取消状态

- `WorkerJob.AvailableActions` 根据阶段状态计算，App 命令和 Worker 检查共用，未知状态不授予操作；输入 JSON 中旧操作列表不参与授权。
- 暂停状态只允许继续/取消，失败或取消只允许重试，安装任务继续/重试要求重新预览。请求取消后再暂停被拒绝，重复取消保持幂等；完成任务不再接受无效控制并假称成功。
- 安装失败阶段明确显示“请重新预览并检查失败原因”，原错误仍在任务中，不以通知替代。
- 新增状态矩阵、反序列化权限及真实 Worker 取消/暂停交错测试；相关专项 17/17。该改变只完成任务操作规则，任务侧面板布局、实机恢复和整体闭环仍未完成。

## 导出预览窗口

- 按 apple-design 技能的上下文、层级和明确退出原则，将资源列表导出改成所属窗口的 `ExportPreviewWindow`；旧打包页/草稿兼容入口保留，但正常多选导出不再跳页。
- 保留相同 ViewModel、选中项和筛选。窗口在异步计划命令释放忙碌状态后显示，避免模态循环导致保存按钮一直禁用；重复更新激活已有窗口。
- 关联依赖改变后旧计划失效；更新预览不重置单类导出范围。保存复用 Worker 导出，关闭不取消后台任务。详情默认折叠，无位移动画，浅深色语义一致。
- WPF 窗口回归覆盖深浅色、详情折叠、关闭保留页面/选择和路径列宽。渲染发现空表星号列宽塌缩，已补最小列宽并增加断言。截图存于 `artifacts/acceptance/export-preview-ui/`，目前是空态，不是资源导出成功截图。
- 完整常规 176 通过 / 4 专项默认跳过，最后列宽修改后窗口专项再次通过。实际窗口保存 ZIP、键盘完整路径、DPI 和任务侧面板仍需继续验收；安装器未重建。

## 依赖补全文件选择修复

- 原补全循环会把导入包中全部成员重命名到同一个模型目标。现改为按类别、相对引用和工作流哈希唯一选择；用户显式选择的单文件仍须为模型类型并满足要求哈希，不把普通配置 JSON 变成模型。
- `AnalyzedDependency.RequiredSha256` 保留工作流哈希依据，下载与本地补全共用验证。目录型依赖保持配置与权重成员路径，目录成员不齐时拒绝补全。
- 节点 ZIP 验证所需类型和版本后只选取匹配包内文件，避免同时加入包里无关的其他节点包。
- 四项专项通过：混合包唯一选择、单文件类型/哈希、跨类别/歧义/越界、目录成员保留与缺件拒绝。此处只证明选择逻辑和页面接线编译，不替代真实下载安装闭环。

## 离线旧格式统一资源计划

- `.cpack` 与 format-1 展开目录经旧格式校验后进入同一个 `ImportPlan`，保留工作流原文及明确的部署路径，不再把 `manifest.json` 当普通待安装文件。
- `deploymentPurpose` 只有匹配类型的 `models/类别/文件`、`workflows/文件`、`custom_nodes/包/文件` 或 `input/文件` 才可直接映射；普通用途说明要求确认。工作流仍需内容结构验证，模型仍需匹配扩展名。Python wheel 不直接映射到资源目录。
- 重复声明同一载荷、非法路径、声明大小或哈希不符拒绝。展开包在读取前核对链接；构造计划时再次比较识别出的大小/哈希。未提供来源哈希时明确说明，不把计算哈希当来源验证。
- 新增归档/目录往返、用途说明及越界/源文件变化四项回归；完整常规 172 通过、4 专项默认跳过。在线 `.cpack.json` 的下载与新预览尚未统一；本节不关闭旧格式整体兼容要求。

## 按版本与布局的部署能力

- 删除 App/Worker 全局 `qualified` 布尔值。`DeploymentCapability` 绑定实例 ID、配置指纹、Desktop 文件版本、登记布局、文件及 Python 能力、验收证据标识和拒绝原因。
- App 从安装预览能力显示具体原因、决定按钮状态，不再从 Worker 全局状态获得写入许可；切换实例或选择失效时能力提示同步清空。
- Worker 只执行自身持久化的原始计划，并在执行前、取得目标锁后的环境检查及提交检查中重新计算能力。计划中的 `CanInstallFiles` 不是执行授权；Desktop 版本改变、布局不支持、缺失配置依据均拒绝。
- IPC 升到 v4；缺少能力快照的旧安装计划需要重新检查。生产资格表保持为空，不能用本次单测或此前来源 GUI 运行直接赋予写入能力。
- 新增三项回归：默认/旧记录拒绝、版本/布局/Python 范围、真实 Worker 原计划篡改及预览后版本变化拒绝。最新完整常规 168 通过、4 专项默认跳过；不是实际安装验收。候选安装器尚未重建。

## Desktop GUI 来源实例实测

- 使用 computer-use，在预先复制的官方核心环境与独立 `desktop-profile` 下启动官方 Desktop v1.0.47。所有输入、输出和节点位于项目验收目录；未操作原实例。独立 profile 的共享目录设置去除了原共享目录，实例共享开关关闭。
- 在 Desktop 页面选择 `FlowPack acceptance source`，Ctrl+O 打开该来源的 `user/default/workflows/upscale.json`，点击运行。任务 `fe1509e8-e49d-4c45-92d0-cac360128cc0` 的 `comfy_usage_source=comfyui-frontend`、执行状态 `success`；8×8 测试图经 `FlowPackAcceptancePass` 与实际 `RealESRGAN_x2plus.pth` 输出 16×16 PNG。
- 首次 FlowPack 运行时核验实测失败，发现官方端口锁记录 venv 启动器，监听者却是其 CPython 子进程。修复后要求：启动器路径匹配登记 Python、端口锁时间有效、直接父子关系、子进程启动时间、`pyvenv.cfg` 唯一绝对 home 与真实基础解释器路径一致。重复查询还核对监听者 PID/启动时间，不扩大为“任意 Python 占用端口即可”。
- `InstanceDescriptor.ConfigurationRoot` 保存登记来源；显式 profile 的发现不会混入用户默认旧配置。配置根加入指纹，Worker 运行时检查采用重新发现的配置根。
- 新增 `DesktopRuntimeAcceptanceTests` 只读取已由 GUI 启动的测试实例和明确 prompt_id，并验证发现、节点已加载、依赖 Present、实际输出尺寸/哈希。专项与相关回归 12/12；完整常规 165 通过、4 专项默认跳过（新增 GUI 专项默认不启动或操作任何实例）。
- 证据目录：`artifacts/acceptance/core-implementation/official-core-4cc5e414738f431388fcc8aa50e5f158/desktop-gui-evidence/source-01538cbcbf494e898bc96e8f7cfe8522/fe1509e8-e49d-4c45-92d0-cac360128cc0/`，包含 `history.json`、`runtime.json` 与 `desktop-source-run.png` 实际窗口截图。
- 原 Desktop `settings.json` / `installations.json` 哈希再次核对与下方基线一致。venv 副本仍使用已有 uv CPython 基础解释器，不是整套基础解释器独立复制；未进行原环境 pip 安装或资源部署。
- **未完成边界**：目标实例尚未改成隔离接管布局；本次没有经过 FlowPack App 执行导出、下载、安装；不因此开启生产门禁。旧 `OfficialCoreRoundtripTests` 的停机后静态节点 Present 断言需要按新的运行时证据语义更新后重跑，不能沿用旧通过记录。安装器未重建，旧数据升级和 UI 交互矩阵继续待验。

## 本轮实际改动

- 苹果式简洁规则适配到 WPF：三主入口、右侧实例/任务/设置、石墨深浅色板、系统字体，未引入苹果资产或玻璃内容卡片。目录、来源、用途和高级设置折叠。复选框、展开项、滚动条采用语义颜色，键盘焦点保留；高对比颜色接入，尚未完成实机高对比验证。
- 导入支持拖入单个文件或目录，自动生成安装计划；修改选择/用途会使旧计划失效。取消选择的工作流不再阻断其他资源的安装计划。
- 工作流普通文本和图片/音频输入不误报模型；带版本的普通 JSON 不因 `nodes` 字段直接成为工作流。节点类型按大小写区分，版本冲突不会被后续结果覆盖；同一工作流中的多个节点版本冲突也会保留。
- 模型哈希按引用路径、类别和所属工作流/节点上下文匹配；不再按整个选择集的 basename 全局混用。有哈希但类别不明确时保留冲突，不声称已匹配。
- 已禁用节点目录和测试/示例目录不提供运行类型证据。静态声明仍不等于运行时加载成功，后续必须接入绑定实例的只读运行信息。
- 按官方 v1.0.47 `paths.ts`、`settings.ts`、standalone 启动代码及 `sessionActions/launch.ts` 实现默认共享路径：异盘安装根、系统盘位置标记/旧足迹、共享输入覆盖参数。单个实例解析失败保留问题记录，其他实例继续扫描。
- Named Pipe 客户端超时覆盖连接、写入和响应读取；响应校验请求 ID。服务端对连接后不发送请求的客户端增加读取超时。
- 安装日志在复制前登记临时路径，重启只处理日志明确登记的临时文件，保留到恢复目录，不扫删用户目录。安装任务恢复为需要检查，而不是继续显示运行。
- Python `Installing` 遗留标记转为 `NeedsRepair`，保留前后清单与计划；后续节点修改受阻，不能把文件恢复说成 Python 已回滚。尚无自动修复入口，不自动卸载用户包。
- 实例、共享目标、Python 互斥锁发生占用时可取消等待，其他 I/O 错误不进入无限等待。

## 验证

- `build/Build-Release.ps1 -Version 0.0.4`：锁定还原、Release 测试 **147 通过、3 跳过**，App/Worker 自包含发布及 Inno 安装器构建。
- 三项耗时专项本轮未重跑；既有 ZIP64、官方核心运行、Python wheel 专项记录不能替代新版本 Desktop GUI 闭环。
- `dotnet run --project src/FlowPack.Smoke/FlowPack.Smoke.csproj -c Release --no-restore -- --inspect-desktop`：本机只读发现实例 `inst-1788535232018`；核心 E 盘、接管数据及 Python C 盘，32 个工作流、54 个模型文件、18 个节点目录；一个节点包的静态类型不足，需运行信息。数量为本轮本机扫描快照，不是普遍数据或运行可用性证明。
- 截图：`artifacts/acceptance/graphite-ui/Light-Home.png`、`Dark-Install.png` 及其他页面。由真实 WPF 控件渲染，导入示例是测试数据；不是安装成功截图。检查过深浅色和有内容的导入状态。
- 原 Desktop `settings.json` SHA-256：`40DEF31C964E367BB054B4AEE24169C514C292FACDB5105C2AE3EF182FB23C6D`；`installations.json`：`E60E869401278B3E9FBA058425157E462DD6CA9D403540C71A82A054D4F009CD`。与此前记录一致。本轮未写原实例模型、节点或 Python。
- 旧候选安装器保留于 `artifacts/archive/v0.0.4-before-graphite/installer/`；构建脚本重建了生成用 publish/installer 目录，没有删除源代码或用户资源。

## 不能标为完成的部分

独立更新引导追加进展：`UpdateBootstrap` / `UpdateBootstrapLauncher` 和 Worker `--install-update` 入口已接入 App 更新操作。运行副本位于 LocalAppData/ComfyUI FlowPack/updates/独立 ID；更新期间保持全局接收门禁及安装器不可写/不可删除句柄，主窗口确认退出后再次哈希核对。处理父进程提前消失、重复更新、安装器非零退出并持久记录状态。新增哈希不符零启动、锁保留到退出、缺失父进程取消等回归，常规 163 通过、3 专项跳过。测试使用启动替身，没有执行真实安装器；真实旧版本升级、多个 App/Worker 的退出及数据保留仍为未完成验收。

更新协调追加进展：`GlobalWorkCoordinator` 提供跨实例共享活动任务租约与独占更新意图；Worker 将租约从请求接收保留到任务完成及状态保存。测试覆盖两个资源库 Worker、更新等待拒绝新任务、现有任务取消、等待取消恢复接收、重复更新互斥；常规 159 通过，3 专项跳过。当前只完成协调层与 Worker 接入，App 更新按钮还需独立引导进程承接，不能把这些测试当作实际安装器升级通过。

目录模型追加进展：`ModelDirectory` 统一配置/词表/权重成员规则、分片索引与组件缺失检查；扫描、导出、第三方导入、暂存依赖和安装计划已接通。`DiffusersLoader` 目录引用按本机官方核心 `nodes.py` 的 `model_path` 规则处理。嵌套目录 ZIP→导入→部署后逐文件哈希相等，未选齐词表及缺失/越界分片阻断测试通过；常规 156 通过、3 专项跳过。自定义代码 `auto_map` 明示需要确认，不假定目录权重都可直接运行。仍缺实际目录模型推理与广泛第三方布局矩阵。

运行时追加进展：已新增 `RuntimeNodeInspector` 和 `DesktopRuntimeEndpointResolver`，按官方 v1.0.47 `process.ts` 的端口锁格式，只读核对实例名称、Python 进程路径、创建时间、TCP 端口 PID 以及 `/system_stats` 的目录参数后读取 `/object_info`。再次核对进程，避免读取期间重启造成串用；同名节点目录无法凭模块名区分时不自动复用。Worker 每次依赖分析重新获取索引和运行信息，静态源码与已加载类型分开。真实 Windows 监听端口归属与 mock HTTP 绑定/错实例/重启测试均通过；仍需官方 Desktop 两实例实际启动验证，不能将 mock 节点响应冒充真实加载结果。

持续目标追加进展：单文件节点已接通扫描 → 导出 → 再导入 → 安装，新增完整字节往返与普通脚本不误判回归，149 项常规测试通过。该回归使用隔离目录的文件部署，不代表 Desktop 实际加载验证；下方运行时节点与 Desktop 闭环仍须继续。

1. 两个隔离官方 Desktop（含接管布局）经 FlowPack 页面及 Worker 完成导出、导入、下载、安装并实际运行。用户已允许可见测试窗口；不是权限待确认，而是测试尚未完成。
2. `DeploymentCapability` 按官方版本与实例布局授权。目前仍是全局关闭门禁，不可通过改为 `true` 冒充验收。
3. 绑定所选实例的运行时节点信息，区分源码、加载失败和可用；复杂动态节点、目录型模型和单文件节点完整支持。
4. 跨多个资源库/Worker 的更新阻止新任务及安全退出，旧安装器实际升级保留数据，强杀与空间耗尽完整矩阵。
5. 全部旧格式合入统一安装流程，导出独立预览窗口、任务侧面板和按状态提供操作，以及全部 DPI、键盘和高对比实机检查。

因此只交付可检查的候选版本，不声明所有功能可用。八项需求仍按 `REQUIREMENTS_ACCEPTANCE.md` 的部分通过/未完成状态登记。

# 0.0.4 核心完成度复核


2026-09-12 工作流格式说明补充（源码）：ResourceImportService将API与UI格式的识别依据分开，现有“详情与用途”展示API执行图与UI画布数据的区别；对侧栏空白仅提示可尝试文件打开入口或向提供者索取UI格式，不承诺该动作必定成功，不改写或自动转换原JSON。apple-design指导把格式限制靠近资源详情而非新增弹窗。官方格式参考https://docs.comfy.org/development/cloud/overview仅用于API图定义，不作为Desktop侧栏行为证明。新增API/UI两项回归，验证源文件、暂存文件、WorkflowDocument.RawJson及哈希保持一致；完整Release292通过/5专项跳过，artifacts/acceptance/core-completion-audit/workflow-format-evidence.trx，diff通过。本轮是使用说明补充，不是侧栏兼容性修复或实际打开验证；未加入36EA…候选，未执行安装。


2026-09-12 真实发布Worker错误哈希阻断验收：复制36EA…候选安装器到artifacts/acceptance/update-invalid-hash-20260912-v1，使用发布目录中的ComfyUI.FlowPack.Worker.exe --install-update读取预期SHA256全零的测试请求，无启动回调替代。PID39268退出1，实际update-status.json为Failed/“安装器 SHA-256 校验失败，未启动安装器。”复制文件实测哈希仍36EADCD2B83D841C82E0E07957E5BA9462EC602A61425DCF8D1A0A195C38F7A0。首次手写测试ID长度错误被请求校验提前拒绝，首次状态另存invalid-request-first-status.json，不计哈希验收；修正ID后才取得上述结果。此测试未经过App页面下载或运行安装器，不代表完整更新/升级通过。产品源码与安装器不变，实际安装验收仍等待明确确认。


2026-09-12 安装验收前置核对更正：卸载登记及默认Programs目录未检出FlowPack，但进程核对发现E:/Software/ComfyUI FlowPack已有0.0.4程序，Worker PID39128使用C:/Users/16054/AppData/Local/ComfyUI FlowPack/Data。该安装的App/Worker基础DLL哈希均154F9E8B89FDC7AFD8AFF67140A628E801FBF993282B99A3D8D6B43220C39140，与36EA…候选配套DLL不同，不能凭exe版本相同认为已升级。Roaming/ComfyUI FlowPack/library-binding.json确实指向上述Data；源码默认主题/绑定按用户保存，不随安装目录隔离。故此前“未发现正式安装”只是登记扫描结果，不能当成本机没有程序。归档0.0.3安装器74387576字节，SHA256175DFC33E1DC6B9FDF63F6013EDC4762F1018E25D607BCBC5E29249974FEEB62，产品版本0.0.3。未启动安装器、未停止任何Worker、未改设置或资源库；旧版升级测试须先保护既有FlowPack数据并取得明确安装确认，不能仅换目录就宣称完整隔离。


2026-09-12 跨进程更新协调验收：源码专用Smoke新增--acceptance-update-coordination，在每个独立临时范围启动两个真实PersistentWorkerService子进程/独立资源库。正常完成场景PID37928/12912：两边均拒绝更新期间的新任务，首个任务结束后仍等待第二个，模拟安装期间保持协调锁及安装文件写保护，结束后恢复接收任务。取消场景PID5152/40232：取消更新不取消原任务，两子进程正常完成，接收任务能力恢复。两场景通过；结果及拒绝响应在artifacts/acceptance/update-process-coordination/，更新/协调回归7通过（update-coordination-regression.trx）。安装器启动和App退出握手仍用回调替代，未运行真正安装器，不能视为App界面或旧版本升级验收。默认Smoke输出因既有PID26688锁定而构建失败，未终止该进程；改为独立artifacts/acceptance/hosts/update-coordination-current输出后构建0警告0错误并完成测试。本轮仅新增测试宿主，不改产品业务代码或36EA…安装器；常规完整基线仍290/5，生产资格不变。


2026-09-12 最新0.0.4候选重建：artifacts/installer/ComfyUI-FlowPack-0.0.4-Setup.exe，74844683字节，SHA256 36EADCD2B83D841C82E0E07957E5BA9462EC602A61425DCF8D1A0A195C38F7A0，NotSigned。已包含安装后预览刷新、本地同内容核对、来源重新预览保留及文件/依赖统计修复；锁定还原、Release290通过/5专项跳过、自包含发布、Inno编译及三项校验清单核验通过，App/Worker基础DLL哈希均E9EC038A9D4C90A608E6DA400453192E73EF7BFADCFCDE53062C9701174895E4，Smoke未打入产品。旧800A…安装器/publish/release.trx保留在artifacts/candidate-history/before-post-install-3f11a7feb28d416782795ddfa8e8f69f。未运行新安装器、未推送/标签/Release；生产资格仍为空。实际新宿主刷新、升级/UI完整矩阵、API JSON侧栏打开兼容性等仍待完成，不能声明全部功能可用。以artifacts/installer/CANDIDATE-NOTES.md为当前包说明；下方“修复未打包/800A最新”为历史状态。


2026-09-12 安装完成后预览刷新修复（源码）：ExecuteDeploymentAsync在持久Worker完成后作废旧计划，重新扫描当前实例并生成新预览；检查失败时明确显示“文件已部署，但重新检查失败”，清除旧依赖行和执行计划，不误报安装失败。Worker依赖分析/安装计划使用MergeVerifiedAsync：仅唯一同类别/相对路径本地候选且实际内容哈希相同才移除重复暂存候选；目录逐项核对运行文件集合和哈希，保留本地节点真实加载状态，同名异内容、多候选、跨类别或多余运行文件不自动替代。增加3项合并回归和2项真实NamedPipe/Worker/VM部署回归（成功刷新、部署成功后扫描失败）；完整Release290通过/5专项跳过，artifacts/acceptance/core-completion-audit/post-install-full-final.trx，diff检查通过。测试模型为临时夹具字节，不是实际模型推理；尚未在新宿主进行安装完成后的页面截图验收、尚未重建800A…安装器。原Desktop和生产资格不变，完整升级/UI矩阵仍待完成。


2026-09-12 新下载模型实际推理与页面复用通过：computer-use在现有隔离Desktop的upscale (2)测试画布把模型切换为realesr-animevideov3.pth并点击运行，prompt f8f1749a-892a-485c-bd23-35d0e3894f65为success/completed，来源comfyui-frontend；缓存仅2/3（输入及测试节点），模型1、放大4、保存5未缓存。输出C盘隔离output/flowpack_acceptance_00003_.png为32×32，SHA256 2B119E0FBAEB467E25B01725D17CA0B38DFA96AAD1612FB6D5A53A7A32EE11F5。DesktopRuntimeAcceptanceTests新增明确downloaded-model场景，核对已安装FlowPack-download-workflow.json依赖均Present、模型/节点存在、实例绑定及真实输出；最终专项1通过0跳过，artifacts/acceptance/core-completion-audit/downloaded-model-runtime-final.trx。history/runtime证据位于fixture/desktop-gui-evidence/target-7d2c0a595981405a959cccdae3535231/上述prompt目录。此运行由既有画布改模型完成，不冒称从侧栏成功打开已安装API JSON；文件原文未改，输出前缀沿用旧画布。

同一download-current FlowPack窗口重新通过文件对话框打开D:/16054/Documents/FlowPack-download-workflow.json，页面将新模型、FlowPackAcceptancePass和fixture.png全部列为本地可复用；预览新增0/复用1/0B，点击下载全部反馈“没有已确认来源的缺失依赖。”未再次安装或下载。另发现待修问题：安装成功后原预览不自动刷新，仍显示旧新增计数和已暂存状态，重新导入才正确；不能把本次重新导入复用通过视为该状态刷新缺陷已修复。生产资格及原Desktop不变；完整升级、UI矩阵等仍未完成。


2026-09-12 实际 pip 部分安装取消验收：新增 PythonInterruptedInstallIntegrationTests，在全新独立 venv 中离线安装本地无安装钩子的测试 wheel，检测到真实 pip 解包后取消。计划2277ac2475c84a40960a956b46636a94保留36/2049个文件，原 pip 25.0.1版本不变，Python日志为NeedsRepair；Worker重启两次均生成NeedsReview修复任务、无直接重试动作，后续安装被阻断。专项1通过0跳过（artifacts/acceptance/core-completion-audit/python-interrupted.trx），常规285通过/5专项跳过（python-interrupted-regression.trx）。夹具和部分环境保留在artifacts/acceptance/core-completion-audit/python-interrupted-b18e0e19ecb9414d947890105fa79f56/result.json所指位置。这是主动取消真实pip解包，不是强制终止宿主、断电或Desktop节点Python依赖闭环，不能替代其余恢复验收。未修改原Desktop或生产资格，未重建安装器。

运行记录复核：用户最新“运行了”对应prompt 840f75e4-2f53-4792-9351-1e2bd11c8486，success且无缓存，但使用的是旧RealESRGAN_x2plus.pth，输出flowpack_acceptance_00002_.png已存在。不能计为新realesr-animevideov3.pth运行通过；新模型下载/部署已完成，实际推理仍待验证。

2026-09-12 用户“允许”后新模型隔离安装完成：关闭已运行的adopted Desktop并确认8188不再监听后，在download-current页面点击安装。run=287abede53084f33869ee0962258e451，journal edcc5f13146a4cf688e79bfaba2e2ae2为FilesDeployed，两项Committed、Error=null。C盘隔离目标工作流SHA256 69D6265A11C72E5D1D05135606238F8A73657419564F34AD42143C867F89DF26，模型SHA256 B8A8376811077954D82CA3FCF476F1AC3DA3E8A68A4F4D71363008000A18B75D，与载荷一致。官方Desktop以同一adopted-desktop-profile重启并从页面选择target；进程链40732→49024(C盘venv)→22220，8188 object_info列出realesr-animevideov3.pth及原x2模型，FlowPackAcceptancePass来自测试节点。侧栏打开API JSON后画布为空，尚未运行；Ctrl+O文件选择框索引失效，坐标输入报非目标窗口，激活刷新重试仍失败，按computer-use停止输入。需人工在已打开对话框选择C盘测试user/default/workflows/FlowPack-download-workflow.json并运行；模型可见不等于推理通过。此轮未改源码/生产资格/原Desktop，800A…产品候选不含后续文案与来源保留修复。此前新模型“待安装确认/仅暂存”为历史状态。

2026-09-12 手工依赖来源重新预览保留：PopulateDependenciesAsync在相同导入ID、实例身份/配置指纹、工作流原文与分析模式下保留来源输入，并逐项核对类别、引用、包身份/版本/要求哈希；歧义结果不继承。新分析保留新的依赖状态/候选，已暂存状态不从旧行复制；改过URL不继承旧ResolvedSource元数据。重新导入不沿用旧输入。真实NamedPipe/Worker/VM回归覆盖手工链接/哈希保留、新导入重置及旧来源元数据隔离；夹具声明核心节点类型，避免该回归为解析模型元数据访问公共Manager。完整Release285通过/4专项跳过，TRX artifacts/acceptance/core-completion-audit/dependency-source-replan.trx，diff通过。此处仅同一运行会话重新预览保留，不是重启持久恢复或全部异步交互验收；尚未加入800A…安装器。新模型安装仍等待用户确认，未点击安装，原Desktop及生产资格不变。

2026-09-12 安装预览统计文案修正：DeploymentSummary 分开显示“安装文件：新增/已有同内容/空间”和“工作流依赖：本地可复用/已暂存待安装/待解决”，避免文件计划复用0被误读为依赖未复用。已暂存候选不计为本地可复用，保留全部阻断原因；无依赖时不显示额外统计行。apple-design用于具体标签及信息分组。真实IPC/VM导入回归追加文件计数、混合依赖状态和阻断原因断言，Release285通过/4专项跳过，TRX artifacts/acceptance/core-completion-audit/preview-reuse-counts.trx，diff检查通过。此文案尚未在新宿主截图验收、未加入800A…安装器；此前已完成真实下载的新模型仍仅暂存，未点击新的安装动作，待用户确认。生产资格及原Desktop不变。

2026-09-12 下载修复后候选重建完成：artifacts/installer/ComfyUI-FlowPack-0.0.4-Setup.exe，74865455字节，SHA256 800AC4CBA34154CA820EE82C77FC853220579169854159DD1F799DDA4D751373，NotSigned。锁定还原、Release285通过/4专项跳过、自包含发布及Inno编译成功；三行校验清单重新核验通过，App/Worker基础DLL一致且Smoke未进入发布。已包含完整计划JSON语义比较和首次导入保留下载来源修复。旧264E…安装器、publish和原release.trx保留在artifacts/candidate-history/before-download-preview-906ec66bd5794766a998e93283f0608c。真实缺失模型页面下载证据见artifacts/acceptance/core-completion-audit/missing-model-ui-download.md；新模型仍仅暂存，待用户对新安装动作确认。未运行产品安装器、未推送或发布，生产DeploymentCapability仍为空，不能视为全部功能验收完成。此前“264E最新/新修复未打包”为历史记录。

2026-09-12 真实缺失模型页面下载通过：重建download-current隔离宿主（run=287abede53084f33869ee0962258e451），选择adopted target并经文件对话框导入D:\16054\Documents\FlowPack-download-workflow.json。页面将既有FlowPackAcceptancePass和fixture.png列为本地可复用，新模型realesr-animevideov3.pth列为缺失，安装按钮禁用；点击“下载全部已确认项”后持久Worker完成真实网络下载，页面变为“已暂存，待安装”，新增2项约2.39MB。来源为作者https://github.com/xinntao/Real-ESRGAN/releases/tag/v0.2.5.0页面模型链接。暂存模型2504012字节，计算SHA256 B8A8376811077954D82CA3FCF476F1AC3DA3E8A68A4F4D71363008000A18B75D；工作流未提供来源哈希，不能称来源哈希校验通过。当前目标models/upscale_models/realesr-animevideov3.pth仍不存在，未点击安装、未更改原Desktop。computer-use要求新的安装动作当时确认，待确认后停止隔离目标再安装、运行验证。产品安装器仍未重建，生产资格不变。

2026-09-12 缺失来源预览修复：ImportNativeAsync 改为先生成安装预览、再解析缺失来源，避免 PrepareDeploymentAsync 重建依赖行后清空自动解析的 URL/哈希。真实 Named Pipe、Worker 和 ShellViewModel 回归确认工作流元数据中的模型地址保留在最终依赖行；测试 URL 仅作解析夹具，没有请求或下载。专项1通过，完整 Release 285通过、4专项跳过、0失败，TRX：artifacts/acceptance/core-completion-audit/dependency-source-preview.trx。此修复只覆盖首次导入调用顺序，不代表所有重新分析均保留手工来源。当前运行的 install-current 宿主及264E…安装器尚未包含此修复；真实缺失下载/安装、升级及完整UI矩阵仍待验收，生产资格不变。

2026-09-12 单工作流页面复用验收：在install-current隔离宿主（run=f160eecd425c492aa7e1284ddd6f0f0f）通过文件选择导入D:\16054\Documents\FlowPack-acceptance-workflow.json（417字节，原已运行API工作流的无修改副本，SHA256 DC938635DA44737F12E6A9C44DB733B486B2AA1911B26C8E90FCCE65324AAC90）。目标保持运行中的adopted target。页面自动将FlowPackAcceptancePass、RealESRGAN_x2plus.pth和fixture.png全部列为“本地可复用”；预览仅新增工作流1项417B，没有加入模型/节点部署项。点击“下载全部已确认项”反馈“没有已确认来源的缺失依赖。”未点击安装、未修改既有资源。副本保留在上述Documents目录便于复验。此证据仅覆盖已有资源复用分支，不代替缺失依赖的真实网络下载/安装验收。文件对话框焦点反馈不可靠，因此按computer-use要求未盲输路径，改从可见列表选择副本。

2026-09-12 接管目标真实运行通过：用户在官方 Desktop 页面打开已安装工作流并点击运行，prompt_id=3fde9dca-0c75-4f45-a4ce-312373f0887a，history为success/completed=true，comfy_usage_source=comfyui-frontend且execution_cached为空。真实使用FlowPackAcceptancePass、RealESRGAN_x2plus.pth与fixture.png，输出C盘隔离output/flowpack_acceptance_00001_.png，16×16，SHA256 EE0EB484B99A8990783D73A47C5A41A1D1D6684BE5890398E362FA71C272263E。DesktopRuntimeAcceptanceTests对该真实prompt的实例发现/端口身份/节点已加载/依赖全部Present/输出校验1通过0跳过；TRX为artifacts/acceptance/core-completion-audit/adopted-desktop-runtime.trx，history/runtime JSON位于fixture/desktop-gui-evidence/target-7d2c0a595981405a959cccdae3535231/上述prompt_id目录。此前页面导出→页面导入→持久Worker部署→官方接管实例运行的该包链路已取得证据；最后打开和运行由用户完成。仍未完成页面缺失下载、全部故障恢复、更新升级与完整UI矩阵，不据此宣布全部可用或开放生产资格。264E…安装器仍未包含后续计划比较修正。

2026-09-12 跨盘运行验收脚本接通：DesktopRuntimeAcceptanceTests 可通过 FLOWPACK_DESKTOP_TEST_SCOPE 指向既有 acceptance-scope.json，复用测试宿主的根目录、实例白名单、有效路径、Python及输出范围验证；未指定时保持旧的同盘目录限制。专项8通过、真实GUI运行专项1跳过。当前PID33796的8188服务仍在线，但history和运行/待执行队列均为空，未取得推理结果；这不是运行验收通过。

2026-09-12 接管目标运行时核对：官方 Desktop 使用 adopted-desktop-profile 启动，经页面选择 FlowPack acceptance adopted target。首次EPIPE日志管道弹窗确认后同一进程继续启动成功，未重启或修改官方源码。Desktop PID38036 → C盘测试venv启动器43532 → CPython33796，监听127.0.0.1:8188；启动参数明确指定隔离C盘base/user/database。只读object_info确认FlowPackAcceptancePass来自custom_nodes.FlowPackAcceptanceNode，UpscaleModelLoader的COMBO options含RealESRGAN_x2plus.pth；日志确认加载C盘节点目录。尚无history记录、未运行工作流。Ctrl+O已打开文件选择框，但computer-use索引失效且激活刷新后的坐标重试仍报非目标窗口，按技能停止继续输入；需要人工打开C盘测试user/default/workflows/upscale.json并点击运行后继续验证。不能把节点加载/模型列出当作推理成功，生产资格仍为空。

2026-09-12 隔离安装重试成功：用户明确“继续安装”后，通过 install-current 隔离页面选择 adopted target、打开此前页面导出的 D:\16054\Documents\ComfyUI-resources.zip 并点击安装。当前 run=f160eecd425c492aa7e1284ddd6f0f0f，journal 计划38500f15ffe14380986ad62fa572be61为FilesDeployed，4项均Committed、Error=null；页面任务显示已完成并要求启动目标检查节点与模型。C:\Users\16054\AppData\Local\ComfyUI FlowPack\Acceptance\adopted-4cc5e414 下工作流、RealESRGAN模型、节点源码、输入图片实测哈希全部与导出载荷一致。未在原Desktop部署；未执行Python依赖安装。上文/下文“等待恢复操作、尚未重新安装”为此前状态，已解除。仍未完成该目标的官方Desktop启动/节点加载/模型及工作流运行验证，不能据此开放生产资格或宣布八项全通过；264E…安装器仍不含此次计划比较修正。

2026-09-12 最新状态：用户确认“安装”后，隔离页面实际提交被 Worker 以“安装计划不是本 Worker 生成的原始计划”拒绝；四个目标载荷均未写入。计划比较已改为 JSON 语义一致性校验，仍要求 Worker 留存的完整计划及当前部署资格；新增格式差异/内容篡改/版本变化回归，Release 285通过、4专项跳过（artifacts/acceptance/core-completion-audit/plan-equality-regression.trx）。修正版隔离宿主已启动，但用户随后按 Esc 停止界面操作，尚未重新安装。需要用户明确恢复操作后继续真实闭环。264E…安装器不包含此次比较修正，未重新打包，生产安装资格仍为空。下文相关“当前源码已全部打包”“安装尚未执行/待首次确认”为历史状态。

复核日期：2026-09-12。目标仍为全部八项核心需求、本地候选安装器及规定的真实验收；不扩展到旧规划的迁移/批量清理。本记录是证据检查，不是完成声明。

## 本轮重新核实的事实

- 当前源码重新运行：284通过、4专项默认跳过。可追溯结果为 `artifacts/acceptance/core-completion-audit/current-source.trx`。
- 复核后的重建已完成：当前安装器Release记录 `artifacts/acceptance/release-0.0.4/local/release.trx` 为284通过，总288；安装器SHA256为 `264E49FA460E869C4E1CD74452266E58787C7E670B6A3D55D943E54B706464D5`。此前274/4的721B…产物已移入历史目录，不再是当前交付物。
- 真实ZIP64、独立Python专项记录分别为1/1通过：`zip64-current-results/zip64-current.trx`、`python-current-results/python-current.trx`。没有在本轮重新执行这两个专项，不扩大它们覆盖的范围。
- 复制中途强制终止报告仍记录 `pythonExecuted:false`，恢复NeedsReview/NotPresent；不能称为实际Python安装中断验收。
- 页面导出后保存的 `core-implementation/official-core-4cc5e414738f431388fcc8aa50e5f158/app-runs/045e9cd955e04692ac7ea75aff75b563/gui-roundtrip.zip` 重新计算SHA256为 `69B226D6231F13940054910EE75A45BAB22EA3E00D9541B51A9792C768B9EB9F`。
- 隔离目标 `C:\Users\16054\AppData\Local\ComfyUI FlowPack\Acceptance\adopted-4cc5e414` 下预期的 `models/upscale_models/RealESRGAN_x2plus.pth`、`custom_nodes/FlowPackAcceptanceNode/__init__.py`、`user/default/workflows/upscale.json`、`input/fixture.png` 均不存在。不是等待已启动安装任务，而是尚未执行安装。
- `current-v5-fixed/FlowPack.Smoke.exe` 的进程37192、4732本轮仍存在。这只证明进程存活，不证明页面当前状态；继续操作前仍要重新读取窗口。
- 生产DeploymentCapability默认资格仍为空。不能凭常规测试和夹具结果开放正式实例写入。

上述未带完整前缀的验收路径均位于 `artifacts/acceptance/`。

## 八项需求的剩余证明

| 需求 | 现有证据边界 | 关闭该项仍需取得的证据 |
| --- | --- | --- |
| C01 首页发现 | 有发现服务回归及隔离宿主发现记录 | 新建/接管/共享/参数覆盖等布局完整矩阵，当前源码页面选择与路径快照一致 |
| C02 已安装资源ZIP | 有页面导出ZIP和独立真实ZIP64专项 | 大文件导出强杀、空间不足、目录型模型配套文件矩阵；当前源码往返 |
| C03 第三方ZIP安装 | 有识别/预览/服务回归，隔离目标尚无四项载荷 | FlowPack页面→Worker→隔离官方Desktop的实际安装、节点加载、模型可见及工作流运行 |
| C04 更新 | 有检查/校验/协调实现与测试 | 旧安装器实际升级且数据保留，活动任务/错误哈希/多App与Worker时的实机行为 |
| C05 主题 | 有深浅页面渲染、独立高对比选中控件夹具 | 实际系统事件切换、全页面/弹窗/下拉/禁用与错误状态、DPI和键盘操作 |
| C06 关联导出 | 有依赖分析/多选/去重回归 | 复杂子图、动态节点、模型目录分片和实际多工作流完整运行 |
| C07 单类导出 | 有各类计划/归档及原文保留回归 | 目录/单文件节点及第三方内容完整矩阵，敏感文件边界和真实运行 |
| C08 缺失补全 | 有来源编辑、下载服务、本地匹配和兼容wheel专项 | 页面真实下载/复用→安装→重新检查闭环，鉴权失败与未知来源实际处理体验 |

没有一项因为“未找到TODO”而被判定完成；没有充分证据的分支继续开放。

## 下一步顺序与授权边界

1. 优先完成已经准备好的隔离安装闭环，而不是继续用小型回归替代它。当前需要用户明确确认页面安装操作；自动目标续跑消息不是该确认。原Desktop实例不得改动。
2. 使用同一隔离范围验证实际下载、复用和运行；记录当前宿主版本，不能把旧宿主结果直接当作最新源码验收。
3. 独立安排真正Python安装中断、升级生命周期、全页面DPI/高对比/键盘矩阵。尚缺证据不等于实现必然有错；测试发现问题后再修复。
4. 会话恢复、过期预览、哈希匹配/进度、导出保护及高对比选择等修改已进入本次重建候选，哈希和随包说明已更新。之后若根据实际验收再次修改源码，需要再次打包核验；目前不把候选构建当作实际安装验收。
5. 不自动推送、打标签或发布Release；完整目标保持未完成。

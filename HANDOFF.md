# ComfyUI FlowPack 开发交接

2026-09-30 发布准备：用户授权推送新版本并补发原 0.0.4。重绘后的源码版本升为 0.0.5，以便现有 0.0.4 安装检测到新版。锁定还原、完整 Release 297通过/5专项跳过、自包含 App/Worker 发布及 Inno 编译通过；本地 0.0.5 安装器 74,858,151 字节，SHA256 A7354D85F370FB9F8A2D0CB200F50E6F7D79C474BA33842436A49E92BB2A524C。GitHub CI 将在源码标签重新构建，最终公开资产哈希以远程包核验为准。原 0.0.4 当前保留包为 74,855,949 字节/AE06D3C1CA8101CD826868C56E1494E682E4CE71AB8AD06C7834ABC3AFFDCB3F，三项原校验均匹配；不同于下方历史36EA候选。该包原产品版本指向3aa5528基础提交并含未提交工作，缺少完整精确源码快照；v0.0.4将用独立归档说明标签补发原字节，不重建，不把自动Source code附件冒充完整原包源码。旧输出已移至artifacts/candidate-history/before-v005-2851977b989b4f0c91e2e683eb762ca4。本轮不执行本机安装，不改现有资源库，不放开生产部署资格。提交、远程发布、CI与下载核验状态待后续记录。

2026-09-30 UI 与功能层级重规划（源码）：按用户要求重绘全部主要页面并生成 [Figma 可编辑稿](https://www.figma.com/design/ATifwFUoSDhcXPgeCRVgfS)。顶层保留首页/我的资源/导入安装；打包工具归属资源库，任务/设置为全局工具。首页和资源页新增工作流、模型、节点文件夹直达，使用所选实例的已登记路径；多目录选择、路径去重、缺失原因、切换实例后旧菜单失效均已接入。资源分类路由选中正确页签并记忆；导入左列表独立滚动，右目标/摘要/操作常驻；资源列表改为有限视口；旧清单详情恢复可达，旧安装按钮仍禁用。Release 全量 **297通过/5专项跳过**，随后最小窗口排版修正的 WPF 专项通过，diff检查通过。深浅/960 DIP截图及TRX在 `artifacts/acceptance/ui-redesign-20260930/`；截图为测试夹具，不是安装成功。Figma最后文字自动高度修正有结构回读，但Starter调用额度耗尽，无法再截图复验。未重建安装器、执行Desktop安装、提交或发布；真实Explorer打开与系统多DPI完整交互仍未验收。功能归属、页面树与边界见 [本轮规划](docs/UI_LAYOUT_REPLAN_2026-09-30.md)。保留此前所有未提交后端工作及生产安装门禁。


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

2026-09-12 运行验收准备：只读复核隔离服务PID33796/8188仍在线，但history为空、queue_running/queue_pending为空。DesktopRuntimeAcceptanceTests新增可选FLOWPACK_DESKTOP_TEST_SCOPE，复用AcceptanceScope校验以支持已登记的C/E跨盘测试（未提供则保持同盘限制）；专项8通过、GUI专项1跳过，尚无真实推理结果。人工打开并运行C盘已安装upscale.json后，使用adopted-desktop-profile、target-7d2c0a595981405a959cccdae3535231及真实prompt_id执行只读运行验收；不能使用合成prompt_id代替。

2026-09-12 接管目标运行时核对：官方 Desktop 使用 adopted-desktop-profile 启动，经页面选择 FlowPack acceptance adopted target。首次EPIPE日志管道弹窗确认后同一进程继续启动成功，未重启或修改官方源码。Desktop PID38036 → C盘测试venv启动器43532 → CPython33796，监听127.0.0.1:8188；启动参数明确指定隔离C盘base/user/database。只读object_info确认FlowPackAcceptancePass来自custom_nodes.FlowPackAcceptanceNode，UpscaleModelLoader的COMBO options含RealESRGAN_x2plus.pth；日志确认加载C盘节点目录。尚无history记录、未运行工作流。Ctrl+O已打开文件选择框，但computer-use索引失效且激活刷新后的坐标重试仍报非目标窗口，按技能停止继续输入；需要人工打开C盘测试user/default/workflows/upscale.json并点击运行后继续验证。不能把节点加载/模型列出当作推理成功，生产资格仍为空。

2026-09-12 隔离安装重试成功：用户明确“继续安装”后，通过 install-current 隔离页面选择 adopted target、打开此前页面导出的 D:\16054\Documents\ComfyUI-resources.zip 并点击安装。当前 run=f160eecd425c492aa7e1284ddd6f0f0f，journal 计划38500f15ffe14380986ad62fa572be61为FilesDeployed，4项均Committed、Error=null；页面任务显示已完成并要求启动目标检查节点与模型。C:\Users\16054\AppData\Local\ComfyUI FlowPack\Acceptance\adopted-4cc5e414 下工作流、RealESRGAN模型、节点源码、输入图片实测哈希全部与导出载荷一致。未在原Desktop部署；未执行Python依赖安装。上文/下文“等待恢复操作、尚未重新安装”为此前状态，已解除。仍未完成该目标的官方Desktop启动/节点加载/模型及工作流运行验证，不能据此开放生产资格或宣布八项全通过；264E…安装器仍不含此次计划比较修正。

2026-09-12 最新状态：用户确认“安装”后，隔离页面实际提交被 Worker 以“安装计划不是本 Worker 生成的原始计划”拒绝；四个目标载荷均未写入。计划比较已改为 JSON 语义一致性校验，仍要求 Worker 留存的完整计划及当前部署资格；新增格式差异/内容篡改/版本变化回归，Release 285通过、4专项跳过（artifacts/acceptance/core-completion-audit/plan-equality-regression.trx）。修正版隔离宿主已启动，但用户随后按 Esc 停止界面操作，尚未重新安装。需要用户明确恢复操作后继续真实闭环。264E…安装器不包含此次比较修正，未重新打包，生产安装资格仍为空。下文相关“当前源码已全部打包”“安装尚未执行/待首次确认”为历史状态。

最后更新：2026-09-12｜工作分支：`main`｜当前源码版本：`0.0.4`（本地候选，未提交/推送/发布）｜最新已发布：`0.0.3`

## 结论

当前源码已全部进入最新候选：artifacts/installer/ComfyUI-FlowPack-0.0.4-Setup.exe，74,843,074字节，SHA256 264E49FA460E869C4E1CD74452266E58787C7E670B6A3D55D943E54B706464D5，NotSigned。锁定还原/Release284通过4专项跳过/自包含发布/Inno编译完成；三行哈希通过、App/Worker基础DLL一致、Smoke排除。旧721B…产物完整保留于before-final-core-f9e0000fd0414006ab41f6bff0e35cf3历史目录。未运行安装器或发布，生产资格仍为空，实际隔离安装/升级/完整验收待用户确认及后续执行。下方“尚未打入安装器”均为历史状态。

完成度复核已整理到 docs/CORE_COMPLETION_AUDIT_0.0.4.md：重新确认当前源码284/4并保存独立TRX，现有安装器Release仍为274/4；页面ZIP哈希一致，但C盘隔离目标四个预期载荷仍全部不存在。当前不是安装任务运行中，而是安装尚未执行。八项逐项列出缺少的实机/矩阵证据；下一优先为已准备的隔离安装闭环，需用户明确动作时确认，不能用自动续跑代替。未改生产资格或宣布完成。

本地模型候选哈希任务进度补齐：流式读取按当前文件上报候选序号/文件名/字节，Worker复用任务进度字段并限频更新；没有来源哈希仍不读取候选。新增512KiB夹具正常完成和读取中途取消回归，取消后独占打开源文件验证句柄释放，大小不变。完整284通过/4跳过，最终专项4通过、diff检查通过。不是实际大模型页面取消验收；721B…安装器未包含本轮源码，真实隔离安装仍待确认。

高对比度选中状态修复：原AccentSoft在高对比度被映射为窗口背景，下拉/表格选择难区分；新增Selection/SelectionText语义色，高对比度使用系统文字/背景反转，普通主题继续石墨灰。HighContrastPalette由真实主题应用路径调用；WPF黑白两套控件夹具验证并目视复核截图 artifacts/acceptance/contrast-selection-ui/HighContrast-{Dark,Light}.png。常规282通过/4跳过、diff通过。apple-design指导对比度适配；未切换系统设置，不代表全页面/真实系统高对比度验收。721B…安装器未包含追加。

导出安全恢复补充：ExportAsync仅清理本次成功创建的临时文件，CreateNew因重名失败不再误删既有暂存。Worker export.execute要求与已完成export.plan完整一致，不能执行篡改的预览。新增临时冲突保留、同大小异内容/大小变化/取消后无输出4项，以及真实Worker正常导出/归档路径篡改拒绝2项；完整282通过/4跳过，diff检查通过。未覆盖本轮真实大模型ZIP64或页面闭环；721B…安装器未包含此追加，实际隔离安装仍待确认。

多候选模型来源哈希复用已接通：App现有类别/相对路径候选过滤后，多个文件且清单有SHA-256时交由持久Worker resource.match-local逐项核对大小/哈希，命中后仍走resource.materialize-local内容核验；无哈希保持歧义、节点不套用模型规则。切换实例/导入期间返回的匹配或载荷结果不应用。新增同大小异内容/缺失/无哈希/类别/尺寸回归和Worker命令覆盖；完整276通过/4跳过、diff检查通过。夹具字节不是实际模型运行证据，真实共享目录页面复用仍待验收。721B…安装器未包含本轮源码，隔离安装仍待确认。

安装预览异步防过期补充：PrepareDeploymentAsync捕获资源变更序号、实例对象和导入计划ID，依赖分析及安装计划返回后分别核对，已过期结果不写入_deployment；忙碌期间累积资源变更保存后重新检查计划。真实NamedPipe测试延迟install.plan响应，在其返回前取消勾选，确认旧计划不恢复到VM。专项1通过、完整274通过/4跳过、diff检查通过；不是实际Desktop页面安装证据。721B…安装器尚未包含此处和上轮会话选择更新，隔离安装仍待确认。

导入会话选择恢复新增：ImportSessionState兼容增加ResourceChoices（资源ID+源路径、勾选、目标草稿），Worker拒绝不属于原计划及重复记录；UI变更自动保存，忙碌期间累积变更在业务操作结束后保存，首次导入在建立列表后保存默认选择。恢复保留勾选与路径，修改目标仍NeedsConfirmation且不恢复执行计划。实际NamedPipe+Worker+ShellViewModel覆盖取消/重新勾选、改路径及新VM恢复；专项19通过，常规274通过/4跳过，最后强化勾选断言专项1通过。未覆盖全部真实页面多窗口交互；当前721B…安装器尚未包含本次源码追加，实际隔离安装仍待确认。

最新候选已重建：artifacts/installer/ComfyUI-FlowPack-0.0.4-Setup.exe，74,841,593字节，SHA256 721B2266BD5A85539D735FD8DB15CD580CE57A80A3CE8C3378D6834E0AF6D15E，NotSigned。Release274通过/4专项跳过；三行校验清单重算通过，App/Worker配套Infrastructure.dll一致，Smoke未进入发布。包含下方近期所有源码修正；旧893F…安装器及publish完整移至 artifacts/candidate-history/before-source-recovery-f5595fdddf184a82843e6c4cfe56bff7。未运行安装器、未发布；生产资格仍为空。完整Desktop闭环和升级等验收未完成，实际隔离页面安装仍待确认。下方“安装器未重建”均为历史状态，当前以随包CANDIDATE-NOTES.md为准。

来源编辑恢复核验：修复旧online.source请求重试返回无关新导入会话的问题，现要求当前计划属于该已完成修改的派生链，否则返回import-session-conflict。新增Worker销毁/重建后读取来源和其他资源下载关联的回归，并验证关联下载不可换源；专项18通过、常规274通过/4专项跳过、diff检查通过。未发生网络下载或Desktop安装，不等同真实多窗口页面闭环。安装器未重建，隔离页面安装仍待确认。

在线清单来源补全已接通：导入页“来源与校验”内增加HTTPS链接与“保存来源”，由Worker online.source在同一事务保存不可变resource.source计划与会话Revision；保留原大小/哈希/部署目标，不自动下载或安装。拒绝旧会话覆盖、已关联下载的资源换源、网页/非HTTPS/携带凭据或片段链接；未知来源节点ZIP要求固定提交。新派生计划可被恢复及后续materialize识别，原计划保留。常规273通过/4专项跳过、diff检查通过；深浅WPF截图 artifacts/acceptance/online-source-ui 已目视复核（测试夹具，不是真实下载）。apple-design指导详情分组和常驻反馈。当前893F…安装器未包含此更新；实际隔离安装仍待明确确认，完整计划未完成。

Python 子进程取消补充：RunAsync 在启动前检查取消；运行中取消时终止进程树，并等待所启动进程退出、收完输出后才返回取消，避免立即释放上层执行范围。新增独立 PowerShell 测试进程验证退出等待及预取消不启动，2/2；完整常规264通过/4专项跳过，diff检查通过。此验证不安装Python包、不修改Desktop，不能代表pip部分安装回滚或所有后代进程退出验收。现有893F…安装器尚未包含此源码更新。真实页面安装仍待动作时确认，完整目标未完成。

复制中途真实终止已验证：RecoveryCrashAcceptance 增加 during-copy，生成512MiB合成源，监测实际暂存文件增长并终止自己的子进程；退出后暂存53,739,520字节，恢复前后部分文件哈希一致，目标NotPresent、journal NeedsReview、重复恢复和Worker启动通过。与提交前/后共3/3，Smoke构建和diff检查通过；常规基线仍为上一轮262/4，本轮未重跑。证据位于 artifacts/acceptance/crash-recovery-copy-stage。测试文件保留在结果root临时目录，原Desktop未动；不代表物理断电或Python实际安装中断通过。实际页面安装仍待确认。

真实进程中断验收追加：源码专用 Smoke `--acceptance-crash-recovery` 在新建临时目录启动自己的子进程，使用实际 ResourceInstallationService 写文件/journal；分别在复制校验完毕但未提交（Running/Intent）、已提交但未执行Python（InstallingPython/Committed）时由父进程终止该子进程。两场景通过，恢复分别为 NotPresent+保留暂存、VerifiedPresent+目标哈希相同，均NeedsReview；重复恢复不变、Worker启动成功。中断前后日志和退出码证据在 `artifacts/acceptance/crash-recovery-checkpoints/`。不是复制中途、物理断电或真实Python子进程失败验收；不接触Desktop实例，生产资格和安装器不变。

Python 恢复问题可见性已补齐：无原 install.execute 记录时也创建稳定 ID 的 install.recovery 任务，状态 NeedsReview、错误原因常驻、无直接重试动作；重复启动保留创建时间且不重复任务。有效中断与坏日志均覆盖，恢复专项14/14、完整常规262/4、diff检查通过。此为源码追加，现有893F…安装器尚未包含；真实隔离安装仍等待明确动作时确认，未点击安装。

真实页面发现的文件名显示问题已修复：导入 CheckBox 不再把文件名字符串交给 AccessText，改为原文 TextBlock 并明确设置自动化名称；`RealESRGAN_x2plus.pth` 和 `__init__.py` 的下划线完整保留，操作按钮快捷键不变。浅/深 WPF 控件回归1/1通过，截图在 artifacts/acceptance/literal-filename-ui，修复仅在源码，现有893F…候选安装器尚未包含此处后续修正。实际隔离安装仍等待动作时确认，未点击安装。

真实页面推进：会话 `045e9cd955e04692ac7ea75aff75b563` 的保存框直接点击默认保存成功，不再等待人工输入路径。ZIP 位于 `D:\16054\Documents\ComfyUI-resources.zip`，67,063,432 字节，SHA-256 `69B226D6231F13940054910EE75A45BAB22EA3E00D9541B51A9792C768B9EB9F`，已复制到该 app-runs 会话目录的 gui-roundtrip.zip；四项载荷哈希与导出预览一致。随后实际页面切换 adopted target、打开该 ZIP，四项全部识别，预览新增4/复用0/63.96MB。尚未点击安装：computer-use 技能要求在 Windows UI 安装前动作时确认。当前使用 current-v5-fixed 隔离宿主，不是最新安装器 UI；不授予生产资格。下方“ZIP未保存”均为历史状态。

当前交付物已重建：`artifacts/installer/ComfyUI-FlowPack-0.0.4-Setup.exe`，74,837,995 字节，SHA-256 `893F172A3D988486F7F5FA3C6430ABD058D0A0B4628A8113FD205EB6E625D351`，未签名。构建流程重新跑常规262/4并发布自包含 App/Worker，Smoke 不进入发布目录。旧安装器/发布目录完整保留到 `artifacts/candidate-history/before-current-0b89583b39e9444aa8713f523b222238`。修复 Build-Release 校验清单表达式漏掉 App/Worker 的问题；本次三行清单已补全核验。生产资格仍为空，安装器生命周期、Desktop 页面闭环等仍待验收，详见随包 CANDIDATE-NOTES.md。下方“安装器未重建”均为历史状态，不代表当前。

界面追加：通用 Button 默认继承语义次要按钮，设置页更新操作收紧为可换行内容宽度按钮，默认层补充真实实例检测/关联命令。按 apple-design 的分组与即时反馈原则处理；浅/深色 WPF 设置截图已目视复核，发现并修复空实例绑定提示缺失和错误颜色键。常规262/4，最后两处视觉修正后 UI 专项1/1，diff检查通过。截图在 `artifacts/acceptance/settings-graphite-ui/`；不是全页面 DPI/高对比度或 Desktop 安装闭环验收，安装器仍未重建。

实例选择后的导入刷新已接通：扫描完成及导入会话恢复后，尝试本地唯一候选的 Worker 校验复用并重新分析依赖；切换实例立即清除旧索引和依赖结果，扫描期间切换时丢弃过期结果。真实 Named Pipe + PersistentWorker + ShellViewModel 回归覆盖先导入无 URL 清单、后扫描实例，校验复用成功且无下载/安装任务。当前常规 **262 通过 / 4 专项默认跳过**，diff 检查通过。这不是图形界面点击验收；双 Desktop 页面闭环、完整 UI/升级与新版安装器仍未完成。

最新基线：常规 **261 通过 / 4 专项默认跳过**。安装日志逐份容错，完整校验计划身份、状态、文件列表和暂存路径后才恢复；坏日志不再阻断 Worker 启动，任务面板持久显示修复原因，新安装在写入前被阻止。新增 10 类损坏日志回归，验证原始日志/暂存保留、其他记录恢复、重复启动与安装拒绝。此前重复备份冲突的两条回归继续通过。不是实际进程强杀验收；GUI ZIP 仍停在另存为，实际双实例闭环、更新升级和安装器重建仍未完成。生产资格为空，不声明所有功能可用。下方保留历史增量，最新状态以本段与收尾记录为准。

Python 恢复日志容错追加：逐个读取并检查链接/JSON/状态，坏日志不再阻断整个 Worker 启动，未知状态不忽略；原文件保留，返回归属未知的修复问题并阻止后续 Python 安装。其他正常中断日志继续转为 NeedsRepair。4 类回归覆盖截断、错误根类型、错误状态类型、未知状态，并验证再次恢复、Worker 启动和写入前阻断；常规 **249 通过 / 4 专项默认跳过**，追加阻断断言后专项 4/4，diff 检查通过。不是实际安装进程强杀验收；坏日志需人工检查，不宣称环境已回滚。页面另存为、安装闭环和安装器仍待完成。

共享模型候选增量：在线导入本地复用不再只查默认写入目标，也匹配当前实例资源索引中的类别+完整相对路径，支持 clip/text_encoders、unet/diffusion_models 别名；重复索引按绝对文件路径去重，多个不同文件候选保持待补全，不按 basename 猜测。匹配后仍由 Worker 检查大小/哈希/内容。新增 7 条类别/子目录/别名/越界/类型回归，常规 **245 通过 / 4 专项默认跳过**，diff 检查通过。共享目录真实页面复用尚未实测，目录型载荷和多候选来源选择仍需继续。目标 GUI ZIP 本轮检查仍不存在，未操作保存窗口；生产资格和安装器不变。

无来源清单本地复用增量：导入后立即尝试所选实例索引中“同类型、唯一精确目标、文件存在”的候选，不依赖下载 URL/按钮是否可用；复用仍经过 Worker 原计划、大小/哈希/内容核验。失败条目保留原因；已有下载任务不另行替换。新增无来源本地工作流复用成功及哈希不符拒绝回归，均无下载任务，常规 **238 通过 / 4 专项默认跳过**，diff 检查通过。仅精确默认目标文件，不代表任意共享模型路径/目录节点/页面闭环已完成。人工另存为尚未完成（目标 ZIP 本轮只读检查不存在），原窗口未再输入；安装器/生产资格不变。

修复后真实启动已验证：`current-v5-fixed` 源码宿主会话 `045e9cd955e04692ac7ea75aff75b563`（启动 UI PID 37192，窗口 461628，继续必须重新枚举）首次启动不再报空响应，发现两实例；页面选择 source 扫描出 1 工作流/1 模型/1 节点，直接勾选并生成 4 文件 63.96 MB 导出预览。节点因配置来源未绑定当前运行实例而 Unresolved，保留说明，不作为运行通过。已勾选允许带缺失说明导出并打开另存为，尚未保存 ZIP。自动化返回的模态元素索引无效，但使用返回的模态截图 ID + 局部坐标可点击；保存文件名焦点仍报告搜索框，与画面矛盾，已停止文本输入等待人工正确填写。当前无安装操作，原 Desktop 未动。

真实隔离 UI 继续验收：重新检查确认旧 Smoke UI 已关闭、旧 Worker 仍在；新建 `artifacts/acceptance/hosts/current-v5` 源码宿主及会话 `9d388e600dcd401b8645d9c018b69ec5`，实际启动窗口发现首次使用报“Worker 响应为空”。原因是没有历史导入会话的合法空响应被通用客户端拒绝；现增加显式可选响应参数，仅会话加载启用。真实 Named Pipe 回归 2/2，默认严格接口仍拒绝空响应。当前运行的测试宿主尚未包含此修复，需重建并重开验证，不能声明首次启动实测修复完成。原 Desktop 未操作，安装闭环、生产资格与安装器状态不变。

任务恢复刷新增量：工作区初始化后每 2 秒串行只读查询 Worker 任务，关闭窗口取消轮询、不取消任务；更新期间不轮询。失败保留任务记录并显示原因，恢复后继续；不占用 CoreReady。快照按 ID 增量协调，未变化的行不重建，避免无变化刷新折叠详情；直接进度回调发生后丢弃较旧轮询结果。新增集合身份/顺序/进度/失败状态回归，完整常规 **234 通过 / 4 专项默认跳过**。尚未完成真实多窗口重连和进程中断矩阵，安装器及生产资格不变。

任务面板紧凑布局已接入：宽度 440 DIP，纵向任务列表替代大表格，中文状态、字节进度、常驻错误、按可执行状态显示控制按钮，技术信息折叠。apple-design 用于信息层级与无位移动画的即时反馈。实际 WPF 深浅色、960×640 最小窗口渲染及按钮/错误/进度绑定回归通过；截图 `artifacts/acceptance/task-panel-ui/{Light,Dark}-TaskPanel.png` 包含明确测试夹具，不是下载成功证据。常规 **232 通过 / 4 专项默认跳过**。多 DPI/高对比度、真实任务页面闭环仍待验收；生产资格和安装器未改变。

任务 UI 增量：顶部任务按钮打开右侧面板，不切换主页面；关闭按钮/Esc 返回，恢复先前焦点。面板复用现有任务控制和常驻错误列表，未新增位移动画。WPF 页面回归 1/1，覆盖开关不改变路由；紧凑列表、实际深浅主题/DPI与键盘全验收尚未完成，不能将面板入口等同整项 UI 完成。安装器未重建。

最新补齐派生导入计划的关联保留：Worker 按已完成 `resource.materialize` / `resource.materialize-local` 任务追溯原计划，保存转换结果时保留其他待补全资源的下载关联；独立重新导入不因清单 ID 相同继承旧任务。两种情况回归通过，常规 **232 通过 / 4 专项默认跳过**，diff 检查通过。没有新增实际页面安装/升级验收证据，整体目标、生产资格与安装器状态不变。

当前增量：导入会话保存增加 Revision 和 Worker 控制锁，陈旧窗口保存返回明确冲突，不覆盖新会话；同一计划中并发登记的下载关联合并保留，冲突任务映射拒绝。保存响应改为完整会话，IPC 升为 **v5**，旧 App/Worker 必须重新启动到一致版本。常规 **230 通过 / 4 专项默认跳过**；本轮单独重跑真实超过 4 GiB ZIP64 往返、隔离 Python 兼容 wheel 安装，各 **1/1 通过**。ZIP 源文件 4,295,098,368 字节，归档 4,295,098,976 字节，往返哈希一致。测试临时大文件与临时 venv 已清理，TRX 和 Python result.json 保留。真实双 Desktop 页面安装闭环、完整多窗口恢复、升级生命周期及 UI 剩余验收仍未完成，生产资格和旧安装器不变。下方增量数值与 v4 均为历史记录。

在线下载接收与会话关联已改为 Worker 单事务提交：新增 `online.download`，来源/哈希从保留清单读取，接收前检查当前会话和资源身份；数据库同时写入队列任务及会话映射后才启动任务。App 不再先入队再单独保存关联；同一原请求重连保持幂等，同一资源已有任务则复用。SQLite 第二步写入故障注入与数据库重开验证通过，常规 **229 通过 / 4 专项跳过**。这补上单次接收的存储间隙，不等于所有多 App 会话覆盖、进程强杀或真实安装恢复已验收。整体目标仍未完成，安装器/生产资格未改变。

导入会话恢复继续接通：Worker 在现有 SchemaInfo 中保存版本化导入计划引用及在线下载任务映射，保存/加载均核对原任务与来源哈希，错误引用不覆盖旧会话。App 启动恢复已导入内容和下载状态，强制清空安装计划并默认不勾选本地载荷，要求重新核对实例/资源；没有恢复安装资格。新增真实数据库关闭/Worker 重建与坏引用回归通过；完整结果见本轮输出。**仍未覆盖用户改过的用途映射、额外追加依赖选择以及“下载入队后、会话保存前”强制中止窗口，不声明完整崩溃恢复。** 整体闭环、安装器与生产资格继续未完成。

下载安全继续补齐：资源/更新下载及 Python wheel 下载关闭自动重定向，由 VerifiedDownloadService 逐跳检查 HTTPS、凭据和片段并限制跳转次数；续传 Range/If-Range 保留。内容头和文件首部共同拒绝常见 HTML 网页，即使伪装为二进制、或 HTML 标记跨越已有暂存和续传响应，也不转为完成载荷；拒绝网页不覆盖旧暂存。下载专项 11/11，常规 **225 通过 / 4 专项跳过**。这些使用可控响应夹具，不是实际网络/安装闭环验收；生产资格、候选安装器与发布状态不变。

在线清单页面已接入：移除 `.cpack.json` 跳转旧入口，统一展示待补全资源；逐项/批量下载、本地文件补全接持久 Worker，下载前尝试复用当前实例索引中精确目标文件并由 Worker 重新校验。下载任务 ID 在当前条目中保留，暂停/失败后可在任务区继续/重试再完成转换；不是重启后的完整导入会话恢复。失败原因常驻，来源/哈希折叠；网页/blob/tree/不安全 URL 和未固定的无哈希节点 ZIP 不启用下载。完整常规 **218 通过 / 4 专项跳过**，Worker 构建、diff 检查通过。**未完成实际网络/跨盘安装页面闭环；链接补全、重启恢复和归档矩阵仍需验收。** 安装器、生产资格和发布状态未改变。下方“App 仍走旧入口”为此前状态，已被本段替代。

在线旧清单后端继续接通：`ImportPlan` 保留 `OnlineManifest` 与 `PendingDownloads`，声明不会成为本地安装载荷。新增 `resource.materialize`，只引用 Worker 已保存的原导入计划及完成下载任务，核对声明来源、下载暂存范围，并重新检查大小/哈希，再按内容识别转换为统一资源；工作流原文进入持久快照。新增专项 6/6，整体结果见本轮输出。**App 的 `.cpack.json` 仍暂走旧入口，待下载列表/按钮尚未接入；不要声明在线清单闭环完成。** 原旧测试 UI 的关闭问题不影响本轮后端进展，整体目标仍在进行，未重建安装器或发布。

本轮已实际启动隔离 FlowPack UI / Worker，页面选择 source 并扫描出 1 工作流、1 模型、1 节点。页面验收发现并修复实例 ComboBox 显示内部对象、资源勾选首击只聚焦、空选择仍可打开并保存导出预览的问题；三类资源改为直接勾选模板列，导出与保存补齐非空条件。新增控件回归已通过，整体 **203 通过 / 4 专项跳过**。旧模态预览自动化关闭失败，已请用户关闭旧测试 FlowPack 窗口（不关闭 Desktop），尚未在新版窗口验证实际 ZIP 保存；不能声称页面闭环通过。测试 run ID `9722589e372245ec868ceaa6dc9d283b`，详见收尾记录。生产资格、安装器与发布状态不变；下方为此前阶段记录。

最新常规回归 **203 通过 / 4 专项默认跳过**。已准备 C 盘隔离接管数据目录和 5,002,151,871 字节的虚拟环境副本（仅复制原有测试副本），E 盘官方核心保持不变；新 `adopted-desktop-profile` 的发现结果正确。`FlowPack.Smoke` 新增源码工作区专用的隔离 UI / 独立 Worker 宿主，按实例白名单、配置来源和全部有效路径检查测试范围；共享输出必须关闭，脱离会话不会回退启动生产 Worker。该宿主不进入安装器，试验权限不是生产资格。实测修复 Desktop 的 FileVersion 为构建标识、ProductVersion 才为 1.0.47.0 的兼容问题。入口已构建且只读检查通过，**尚未启动新 UI 或完成跨盘页面安装运行闭环**。原 Desktop 配置哈希未变，生产资格仍为空，安装器未重建。下方为此前增量记录，数值不代表最新基线。

任务控制规则已统一：`WorkerJob.AvailableActions` 从当前状态计算，App 的暂停/继续/取消/重试按钮与 Worker 共用规则；旧序列化操作列表不能授予权限。已取消请求不能被暂停覆盖，完成态不接受取消，安装恢复必须重新预览，失败原因继续持久保留。任务专项 17/17（含真实 Worker 取消/暂停交错回归），整体回归结果见本轮输出；任务侧面板布局尚未完成。生产资格仍为空，实际安装闭环、在线清单及安装器升级仍需继续。

导出入口已改为独立 `ExportPreviewWindow`：资源列表生成计划后弹出预览，不切走当前页面；关闭保留筛选和选择。修改关联依赖使旧计划失效，更新预览保留单类导出范围；保存按钮接现有 Worker ZIP 执行。按 apple-design 的层级/上下文原则，主表只显示路径与大小，来源和哈希折叠，沿用语义深浅色且无位移动画。常规 176 通过 / 4 专项跳过；新增窗口渲染、关闭保留状态与浅/深色断言已通过，空态截图在 `artifacts/acceptance/export-preview-ui/`。未宣称实际页面保存 ZIP 闭环通过，任务侧面板、在线清单与隔离安装仍待继续；安装器未重建。

依赖补全继续修复：新增 `DependencyImportSelection`，模型 ZIP 只按类别和相对引用选择唯一模型；显式单文件也核对工作流指定 SHA-256，普通 JSON 不可借补全入口改名为模型。目录模型保留成员路径并要求完整，不能把多个文件覆盖映射到同一目标。节点补全只保留匹配包的源码，不带入其他节点包。专项 4/4；常规回归见最新工具记录，生产隔离闭环和在线清单仍待完成，安装器未重建。

最新追加离线旧包转换：`.cpack` 和 format-1 展开目录校验后，将清单明确的 `deploymentPurpose` 路径接入统一 `ImportPlan`；说明文字不作为部署路径，普通 JSON 不因清单声明成为工作流，重复载荷/越界/内容不符拒绝。展开目录读取前检查链接，识别后再次核对声明大小/哈希。新增 4 项回归，完整常规 **172 通过 / 4 专项默认跳过**。**在线 `.cpack.json` 仍走旧入口，尚需统一下载及预览，不能声明旧格式全部完成。** 生产资格为空、安装器未重建，整体目标继续。

最新源码已用 `DeploymentCapability` 替换 App/Worker 的全局 `qualified` 开关，IPC 升为 **v4**。安装计划携带版本、布局、文件/Python 能力、证据标识和具体拒绝原因；Worker 执行前及每次环境复检重新计算能力，不信任计划中自带的准入结果。旧计划缺少能力依据要求重新检查，篡改原计划拒绝。当前生产资格表仍为空：尚未完成双隔离实例 App/Worker 页面闭环，不启用生产写入。新增资格范围/版本变化/伪造计划回归，最新 **168 常规通过 / 4 专项默认跳过**，安装器未重建。下一步继续隔离验收入口和目标接管布局，而不是把资格表直接放开。

最新推进到 **165 常规测试通过 / 4 专项默认跳过**，其中新增 Desktop 运行专项已单独通过（连同发现/运行时回归共 12/12）。通过 computer-use 在项目内独立配置的官方 Desktop v1.0.47 启动来源实例、打开 `upscale.json` 并点击运行，真实 RealESRGAN 与自定义测试节点生成 16×16 输出。实测发现并修复 Windows venv 启动器 PID 与监听子进程 PID 不同导致的核验误拒；现在核对直接父子关系、启动时间和该 venv 的 `pyvenv.cfg` 基础解释器路径。实例描述保留配置来源，显式配置扫描不混入默认旧实例。证据见 [收尾记录](docs/GRAPHITE_UI_CORE_FOLLOWUP_0.0.4.md)“Desktop GUI 来源实例实测”。**这只是来源实例运行及 FlowPack 只读分析通过，不是 App/Worker 导出安装闭环资格；目标接管实例、能力门禁、更新生命周期及 UI 剩余项继续进行。安装器未重建。** 原 Desktop 两个配置文件哈希复查未变。

独立更新引导已接入：App 下载后复制同版本 Worker 运行文件与安装器到用户暂存目录，启动 `--install-update`。引导进程检查哈希、取得全局更新租约、等待主窗口确认退出、再次校验，再启动安装器；锁与只读文件句柄保留到安装器退出，结果写 `update-status.json`。主窗口提前退出可取消等待，不同协调实现的运行中旧 App/Worker 会要求先结束任务并退出。最新 **163 常规测试通过 / 3 专项跳过**；Worker 构建通过。测试中的安装器启动使用替身回调，**尚未完成真实安装器升级、多 App 退出和旧数据保留验收**。安装器仍未重建，目标继续。

更新协调追加：新增每用户跨进程 `GlobalWorkCoordinator`（内核文件租约，不用易过期的布尔标记），Worker 初始化、数据库写入及异步任务全生命周期纳入接收门禁。更新等待拒绝新任务、保留暂停/取消操作，等所有资源库已接收任务及最终状态落库结束后才取得独占更新租约；更新等待取消后恢复接收。常规回归 **159 通过 / 3 专项跳过**。**App 的安装器启动尚未接入独立更新引导进程，因此不能声明多 App 更新闭环完成**；下一步实现复制到暂存区的引导进程，持有门禁跨越 App 退出和安装器运行，并验证升级。下方旧安装器仍未重建。

持续目标“全部做完”已启动，尚未完成，不能以以下候选安装器作为目标完成依据。最新源码追加单文件节点链路：静态声明识别、资源扫描、独立 ZIP 导出/导入、Worker 安装计划和根目录部署；普通脚本保持未知。常规测试 **149 通过 / 3 专项跳过**。本次源码尚未重新打包，下方安装器仍对应此前 147 项基线。

最新追加运行时节点核验：`RuntimeNodeInspector` 读取官方端口锁并核对进程、Python 路径、实际 TCP 监听者、启动参数；仅查询本机回环地址，重复名称/共享 Python 无法唯一对应时不借用结果。Worker 扫描和依赖/安装计划已接入，未加载的本地源码不再自动满足节点依赖；待验证的完整源码仍可随带说明的导出包导出。运行时专项 5/5（包括真实 Windows TCP 监听者归属），不是 Desktop GUI 实机验收。正式能力门禁继续关闭；目录模型、更新多进程协调、Desktop 页面闭环等仍需继续。

运行时核验接入后的完整常规回归：**154 通过 / 3 专项跳过**，`git diff --check` 通过；尚未重建安装器。

目录型模型继续推进：新增描述文件/分片索引/组件文件检查；目录资源与各权重文件保留索引，导出共用目录成员规则，嵌套词表/配置往返保留。支持官方 `DiffusersLoader.model_path` 目录引用，未选齐成员或缺失/越界分片会阻断完整导出及安装预览；不再因邻近 `config.json` 打包全部无关文件。目录字节往返和不完整阻断回归后 **156 通过 / 3 专项跳过**。这不是实际目录模型推理验收，特殊自定义模型代码仍要求补全；安装器尚未重新构建。

本轮继续实现 `0.0.4` 本地候选：界面改为首页 / 我的资源 / 导入安装三个主入口，黑白灰石墨强调色；详情、高级设置折叠，拖入后自动分析。新增普通 JSON/图片/文本误判、跨类别哈希、版本冲突、失效实例隔离、共享路径规则、IPC 超时、可取消互斥等待、暂存恢复和 Python 需要修复状态。**整项计划尚未完成，不能宣称八项全部完成或所有功能可用。** 生产安装门禁仍关闭，版本/布局能力替代门禁、运行时节点核验和真实 Desktop/App/Worker 闭环等仍待实现/验收。

此前真实超过 4 GiB ZIP64 往返、两份隔离官方核心的模型/节点运行、独立 Python 兼容 wheel 安装曾单独通过；本次界面收尾未重跑这些专项。它们不等于官方 Desktop GUI 或安装器生命周期验收。**用户已允许显示隔离测试窗口，无须重复询问该权限。** 两个 Desktop/App/Worker 页面闭环仍未通过，不能沿用此前隐藏窗口的权限询问作为当前阻塞原因。

最新改动、证据及明确缺口见 [石墨 UI 与核心收尾记录](docs/GRAPHITE_UI_CORE_FOLLOWUP_0.0.4.md)，此前专项见 [核心实施记录](docs/CORE_IMPLEMENTATION_0.0.4.md)。当前界面规范见 [FRONTEND_REFERENCE.md](FRONTEND_REFERENCE.md) 首节，旧蓝色图仅为历史参考。

新本地候选安装器：`artifacts/installer/ComfyUI-FlowPack-0.0.4-Setup.exe`，74,747,349 字节，SHA-256 `EF83729ED3FEB2D7039061C13D7515E16F4D76292BE1A043671ACC08CFDBC329`，`NotSigned`。锁定还原、Release 常规 **147 通过 / 3 专项跳过**，App/Worker 自包含构建、WPF smoke 通过；安装器未做实际升级验收。深浅主题截图位于 `artifacts/acceptance/graphite-ui/`，包含测试夹具，不是安装成功证据。前一版 0.0.4 安装器已保留到 `artifacts/archive/v0.0.4-before-graphite/installer/`；0.0.3 历史归档保留。未提交、推送、打标签或发布。

不要把本地候选构建、自动化单测或页面文案当成正式发布/实机验证证据。

## Git 与提交边界

- A–F 后端基础已独立提交：`7c539f4 feat: add ComfyUI resource foundations`。
- v0.0.3 源码候选基线为 `ac82558`，版本号修正为 `7b664ab`，安装器证据修正为 `3e8041a`；发布后交接记录为 `2fc6de4`。已发布标签 `v0.0.3` 固定指向 `3e8041a`，不得移动。
- `.workbuddy/` 已在 `.gitignore`，不得提交。
- `main` 已推送至 `origin`；发布标签 `v0.0.3` 与 [GitHub Release](https://github.com/LightyearXizIl/ComfyUI-FlowPack/releases/tag/v0.0.3) 已于北京时间 2026-09-12 11:41 发布，均非草稿/预发布；安装器为 `NotSigned`，不能声称已签名。

## v0.0.3 已发布基线（历史记录）

| 区域 | 已实现 | 明确边界 |
| --- | --- | --- |
| 壳层与语言 | `Home / Library / Packaging / Install / Tasks / Settings`；资源库聚合工作流、模型、节点；安装聚合包和预览；`ILocalizationService` 支持系统、简中、英文即时切换并保存偏好 | 历史视图中的所有遗留中文尚未全部资源键化；需补 UI 自动化与高对比验证 |
| 包与导出 | `.cpack`、`.cpack.json`、展开目录和原生 ZIP 识别；原生 ZIP 私有 staging；ZIP64 大模型不压缩导出 | `.cpack` 三格式统一预览、全部依赖证据和 4 GB 实体回归未完成 |
| staging 安全 | 拒绝 traversal、绝对/盘符/UNC/ADS、保留名、重复路径、链接和超量展开；失败删除 `.incoming-*` | 未做真实 4 GB、空间不足/强制中止的完整矩阵 |
| 资源库 | schema v6；v5 迁移前 `VACUUM INTO` 备份；新增资源、引用、安装、缓存、尝试、journal、备份、验证报告表 | App 仍有历史直接数据库访问；尚未完全收口为 Worker 唯一写者；journal 没有恢复执行器 |
| Worker | 协议 v2；持久化下载、状态与任务读取；安装计划/验证命令明确返回 `safety-gate-not-met` | 暂停/继续/取消/重试、每实例串行、下载并发 3、重连与写锁尚未实现 |
| Desktop | `OfficialComfyDesktopAdapter` 从只读探测生成目标指纹和冻结计划；默认 `AllowsWriteExecution=false` | 未知/普通真实实例禁止写；未实现配置受管段、实例锁、停机、Python wheel 策略或写前复检 |
| 更新与安装器 | GitHub latest release 检查要求 HTTPS setup 资产及同名 `SHA256SUMS.txt` 行；设置页可手动检查；Inno 开启语言选择/安装目录/保留语言和目录 | 不下载、不启动安装器、无 24h 冷却开关；中文 `.isl` 是最小覆盖层，发布前须替换为固定的完整 MIT 上游翻译 |

## 安全不变量

1. 不修改 Desktop 的 `resource/ComfyUI`。
2. 不对真实 Desktop、Python、模型或节点做写入；`StartInstallCommand` 必须继续禁用，直到隔离实例实测通过。
3. 所有下载仅写 FlowPack staging；不允许任意 HTTP 或第三方脚本自动执行。
4. 计划执行必须重新核对目标指纹、磁盘空间、冲突和备份；同名异哈希、本地修改、外部节点和 Python 替换均应阻断。
5. 未来 schema 拒写；迁移失败不能清空旧库。

## v0.0.3 发布证据（历史记录）

- 锁定还原、Debug 与 Release 均为 **101/101**，`git diff --check` 通过。
- 本地安装器：`artifacts/installer/ComfyUI-FlowPack-0.0.3-Setup.exe`，74,387,576 字节，SHA-256 `175DFC33E1DC6B9FDF63F6013EDC4762F1018E25D607BCBC5E29249974FEEB62`，Authenticode `NotSigned`。仅验证生成，未做干净机安装/卸载。
- 远程资产：[ComfyUI-FlowPack-0.0.3-Setup.exe](https://github.com/LightyearXizIl/ComfyUI-FlowPack/releases/download/v0.0.3/ComfyUI-FlowPack-0.0.3-Setup.exe)（74,387,576 字节）与 [SHA256SUMS.txt](https://github.com/LightyearXizIl/ComfyUI-FlowPack/releases/download/v0.0.3/SHA256SUMS.txt)（99 字节）。已于 2026-09-12 复读远程 `SHA256SUMS.txt`，内容与上述 SHA-256 和文件名一致；未重新下载完整 EXE 做端到端哈希。
- 本机只读确认过 Desktop 配置和实例路径；未进行写入、停机、配置修改或真实生成。

## v0.0.4 后续验收（按安全依赖顺序）

1. 用户已允许可见窗口，继续项目内两个隔离 Desktop 实例验收（含一个接管布局）；原实例数据及 Python 不得用于写测。不能只把 Worker 的 `qualified` 改为 true。
2. 补齐受支持版本的默认共享目录规则、运行实例只读类型信息、复杂动态节点与多文件模型的识别矩阵；不确定项继续阻断或要求明确补全。
3. 完成 Desktop 发起启动后的节点加载、模型可见和工作流实际运行，以及 Worker/App 全按钮闭环。只跟踪测试自己的 `prompt_id`。
4. 继续强制中止/重启恢复、磁盘不足、共享 Python 互斥、鉴权、断网及安装器生命周期测试。已完成的自动测试见新记录，不重复当作待实现后端。
5. 完成全页键盘、下拉弹窗、系统主题事件、真实 150%/200% DPI 验收；当前 WPF 离屏截图只证明页面及色板。
6. 资源迁移、批量清理等旧扩展规划保留后续。源码推送、标签和 Release 等待后续指令，已发布 `v0.0.3` 标签不得移动。

## 关键文件

- [ShellViewModel.cs](src/FlowPack.App/ShellViewModel.cs)：页面状态、语言与手动更新检查，安装命令仍禁用。
- [LocalizationService.cs](src/FlowPack.App/Services/LocalizationService.cs)：双语偏好和资源键。
- [NativePackageStagingService.cs](src/FlowPack.Infrastructure/NativePackageStagingService.cs)：普通 ZIP 私有 staging。
- [ResourceLibraryDatabase.cs](src/FlowPack.Infrastructure/ResourceLibraryDatabase.cs)：schema v6 与迁移备份。
- [DesktopAdapter.cs](src/FlowPack.ComfyUI/DesktopAdapter.cs)：只读适配和冻结计划。
- [FlowPack.iss](installer/FlowPack.iss)：安装器语言/目录/升级保留配置。
- [REQUIREMENTS_ACCEPTANCE.md](REQUIREMENTS_ACCEPTANCE.md)：完整验收表；未具备实机证据的条目不得关闭。

## 正式出口条件

只有完成隔离 Desktop 双实例、共享资源、节点修改、Python 冲突、Worker 中止恢复、L1–L4、修复/升级/卸载/迁移、离线包、干净 Windows 安装、升级/卸载/重装保留验证，且记录实际产物/日志/截图后，才可以关闭 M10 并称为正式版。

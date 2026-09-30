# 2026-09-30 发布与更新核验

0.0.4 原包归档补发和 0.0.5 新版均已推送并公开发布。GitHub `releases/latest` 指向 0.0.5，两版均非草稿、非预发布。

| 项目 | 0.0.4 原包补发 | 0.0.5 重绘版 |
|---|---|---|
| 发布页 | https://github.com/LightyearXizIl/ComfyUI-FlowPack/releases/tag/v0.0.4 | https://github.com/LightyearXizIl/ComfyUI-FlowPack/releases/tag/v0.0.5 |
| 标签提交 | f8d4d50d1547bd69774f0439a547f4b0faf19d50，原包溯源说明 | 27bf8709454da27de0dd7eabdfcd16a3e86f811e，完整新版源码 |
| 公开安装器字节数 | 74,855,949 | 74,858,130 |
| SHA-256 | AE06D3C1CA8101CD826868C56E1494E682E4CE71AB8AD06C7834ABC3AFFDCB3F | 23F6150727418CA893897FE9B9BC93BE071E032FF3959EA62A6880522650CD5C |
| 构建来源 | 原本地安装器保留原始字节，不重建 | GitHub Windows CI 自包含 App/Worker 与 Inno Setup |
| 工作流 | 36674374967 成功；仅核对归档声明，跳过重建 | 36674404050 成功；297测试通过、5专项跳过 |
| 完整公开 EXE 回下载 | 已下载完整字节并核对长度、SHA-256和版本 | 已下载完整字节并核对长度、SHA-256和版本 |
| 签名 | NotSigned | NotSigned |

0.0.4 原 App 产品版本包含构建基础提交 3aa5528691bea25c2d9f31b9125b74533865df8d，但原构建包含当时未提交改动，缺少能精确对应原二进制的完整源码快照。v0.0.4 是归档说明标签，其自动 Source code 压缩包不能当作原安装器完整源码；当前完整实现保存在 v0.0.5。

本地发布前构建同样通过 297/5，安装器为 74,858,151 字节/A7354D85F370FB9F8A2D0CB200F50E6F7D79C474BA33842436A49E92BB2A524C。该包在提交前生成，产品信息中的基础提交仍是 3aa5528；公开交付使用 CI 从 27bf870 标签构建的包，二者不混作同一安装器。远程安装器与 GitHub 资产 digest、CI日志和公开 SHA256SUMS.txt 中的安装器条目均一致。

## 更新检查

- 使用实际 `GitHubReleaseUpdateService` 源码访问公开接口：当前版本设为 0.0.3 或 0.0.4 时返回 0.0.5、同名安装器和上述匹配哈希；当前版本 0.0.5 时不提供更新。
- 另从 `E:/Software/ComfyUI FlowPack/ComfyUI.FlowPack.dll` 加载本机已安装的实际更新检查组件（AssemblyVersion 0.0.4.0），调用只读 `CheckAsync`；实测同样返回 0.0.5 与公开安装器哈希。
- 此核验没有经过设置页面点击、Worker 下载或安装器交接；不能代替完整一键升级、数据保留及重启验收。用户可在设置页手动检查更新，手动检查不受启动时24小时冷却限制。

## 证据与保留事项

- `artifacts/acceptance/release-0.0.5/local/release.trx`：本地完整回归；`github-ci.log`：远程完整构建日志。
- `artifacts/acceptance/release-0.0.5/live-update-probe.json`、`installed-update-probe.log`：公开更新元数据与已安装组件检查。
- `artifacts/acceptance/release-0.0.5/remote/`、`release-0.0.4/remote-public/`：公开安装器完整字节、分段下载证据与核验 JSON。下载拼接后对整个 EXE 计算 SHA-256，不以文件名或HTTP头代替内容校验。
- 原 0.0.4 安装器与原 App/Worker 发布文件保留在 `artifacts/candidate-history/before-v005-2851977b989b4f0c91e2e683eb762ca4/`，原三项校验均匹配。
- 本轮未执行本机安装或升级，未修改现有资源库与 Desktop 配置。生产部署资格表仍为空；完整升级/数据保留、干净机安装卸载、多 DPI 与实际 Explorer 交互仍待验收。

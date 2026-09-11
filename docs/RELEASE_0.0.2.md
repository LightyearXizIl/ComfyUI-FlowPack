# ComfyUI FlowPack 0.0.2 发布验证

发布日期：2026-09-11  
目标：Windows x64 自包含安装包。

## 本地构建验证

- 锁定还原：通过。
- Debug 自动化测试：53/53 通过。
- Release 自动化测试：53/53 通过；TRX 位于 `artifacts/acceptance/release-0.0.2/local/release.trx`。
- App 与 Worker 自包含发布：通过；两者文件版本为 `0.0.2.0`，产品版本包含源码提交 `4e3a95cd5ebea5b5edff8b4806dbffa435369264`。
- Inno Setup 6.7.3 编译：通过。
- 本地安装包：`artifacts/installer/ComfyUI-FlowPack-0.0.2-Setup.exe`，74,275,707 字节。
- 本地安装包 SHA-256：`E34524FBD4CDE48C64AE82A754408566FA20717A5BB10FDB9CFF901FE8197317`。
- Authenticode：`NotSigned`。

未执行安装、启动、卸载或真实 ComfyUI Desktop 测试；这些由用户后续在隔离环境中验证。

## GitHub 发布验证

- 源码提交与标签：`4e3a95cd5ebea5b5edff8b4806dbffa435369264`，标签 `v0.0.2`。
- GitHub Actions：[运行 34579299090](https://github.com/LightyearXizIl/ComfyUI-FlowPack/actions/runs/34579299090) 成功，完成构建、测试、打包、工作流产物上传与 Release 创建。
- GitHub Release：[v0.0.2](https://github.com/LightyearXizIl/ComfyUI-FlowPack/releases/tag/v0.0.2)，非草稿、非预发布。
- 远程安装包：[ComfyUI-FlowPack-0.0.2-Setup.exe](https://github.com/LightyearXizIl/ComfyUI-FlowPack/releases/download/v0.0.2/ComfyUI-FlowPack-0.0.2-Setup.exe)，74,284,159 字节。
- GitHub 服务器摘要：`DD4498F0938290AC4F9E5DD41DED367D12E51FC39B0712F73372F521ED2A75A3`。
- 远程 `SHA256SUMS.txt` 已读取，内容与该服务器摘要一致。

本地与远程安装包分别由不同 Windows 环境构建，大小和 SHA-256 不同；不能用本地哈希验证远程资产。未完整下载远程安装包并进行本机安装，因此没有“远程下载后本机安装”通过的证据。

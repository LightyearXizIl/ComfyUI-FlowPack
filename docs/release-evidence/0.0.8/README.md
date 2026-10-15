# 0.0.8 发布证据

- 发布源码：`e2a5ff0de0a638818b1fdff717d77fc4d11ef922`，标签 `v0.0.8`。
- [GitHub Actions](https://github.com/LightyearXizIl/ComfyUI-FlowPack/actions/runs/38735877841) 成功；本地与 CI 标准 Release 均为 **381 通过、0 失败、5 按需专项跳过**，386 总计。
- [正式 Release](https://github.com/LightyearXizIl/ComfyUI-FlowPack/releases/tag/v0.0.8) 已发布，包含 Windows x64 自包含安装器和 `SHA256SUMS.txt`。
- 远端下载安装器 **74951332 字节**，SHA-256 为 `58E9BB196495CD8542C31884CF6E442D17FCF25BA04008867A3069EAD501743A`，同时匹配校验文件与 GitHub API digest；产品版本为 0.0.8。
- 本地构建安装器 **74962941 字节**，SHA-256 为 `AA551055184F305007A2EC7D98DEBCEE4778397C7877998CA0CAFBE2A84B6525`，保留在 `artifacts/archive/release-0.0.8-local`。本地和 CI 构建环境及提交版本元数据不同，分别记录。
- `artifacts/installer` 现在保存已从远端下载并核验的正式安装器和校验文件。
- `local-tests.json` 保存本地测试总数与跳过项；`release.json` 保存 CI、资产、下载核验和本地独立哈希。

本版本没有新增干净机器安装、第二台电脑实例发现或真实 Desktop 推理验收。修复阶段的 WPF 自动窗口与包内 Worker 往返验证见[修复记录](../../EXPORT_DISCOVERY_REPAIR_2026-10-15.md)；外部点击验收因 computer-use 窗口绑定故障未完成。

App/Worker 协议为 7。优先从旧版应用内更新，以安全退出旧 Worker；手动安装后遇协议不兼容提示时，先确认旧任务结束，再重新启动 Windows。仅关闭旧界面不保证旧后台结束。

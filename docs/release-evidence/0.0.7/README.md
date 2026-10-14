# 0.0.7 发布证据

- `local-tests.json`：本地标准 Release 的 368 通过、0 失败、5 按需专项跳过（373 总计）。
- `release.json`：提交、标签、远端 Actions、GitHub Release 资产、远端下载校验，以及本地构建包的独立哈希。
- 远端安装器 74947639 字节，SHA-256 为 `DCE74221F86EF1695362B445917BE7AF2CCDBD9B32C0285C74EDE9EE995C7A27`，与远端 `SHA256SUMS.txt` 一致。
- 本地构建安装器 74893235 字节，SHA-256 为 `3E60936E65A66EA9CF7A1B28AE48842695F057BF556B95B3234D882112965FDB`。两者来自不同构建环境，分别保留，不能互相替代。
- 本版本完成源码、Release 回归、CI 和远端安装器校验；没有新增 Desktop 推理或干净机器验收。

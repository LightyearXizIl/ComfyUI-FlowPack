# 0.0.6 发布证据

- `local-tests.json`：本地正式构建的完整 Release 统计及五个按需跳过项。
- `release-app-result.json` 与 `release-app-process-cleanup.json`：正式 App/Worker 的实际页面导出、自动导入复用预览与正常退出；不包含重新安装或推理。
- `import-install.png`：正式二进制导入预览截图。
- `deployment-qualification.json`：此前 R3 的 Desktop 1.1.4 两布局安装、Python 版本保护、真实输出和恢复证据，保留原始文件 SHA-256，机器路径已脱敏。
- `cleanup.json`：项目内清理范围、原产物大小和保留数据；工作区外临时目录、本轮部分构建缓存及下载临时文件的删除被自动审批拒绝，仍有残留。
- `release.json`：标签、远端 CI、正式 Release 资产及下载哈希的最终记录。

按用户要求，大型测试环境、旧便携包和历史构建已经从工作区删除。历史文档仍记录当时版本与结果，当前证据以这里的精简记录为准。源码实时预览和按需便携构建脚本保留，现有测试包不再作为交付入口。

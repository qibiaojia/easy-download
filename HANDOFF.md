# 交接

- 仓库为 `https://github.com/qibiaojia/easy-download`，经用户确认保持公开。应用原有 29 个文件完整保留；额外添加 README 与本交接说明。
- 桌面 `.lnk` 仅是本机快捷方式，不属于应用分发文件。运行时必须保留 EXE 旁的 `engine` 文件夹，以及随附的源码、许可和第三方说明。
- `source/test.mjs` 内含原机器绝对路径；`自检结果.json` 是 2026-11-02 的历史记录，不能视为本次重新测试。实网验证尚未通过，具体原因见该 JSON。
- `ChatGPT Codex Local Connector` 必须先安装到 GitHub 账号并获目标仓库授权；仅出现在“授权的 GitHub 应用”列表不足以证明有仓库写入权限。用户安装后，README 写入成功。
- 连接的文件工具仅支持文本；此次通过一次性 Actions 解包流程导入完整程序，之后移除临时 Base64 文件和工作流。交付应按 Git blob SHA 校验原始字节。

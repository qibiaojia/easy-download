# 轻松下载

中文 Windows 桌面下载器，基于 aria2，支持 HTTP/HTTPS 文件直链和最多 8 路连接。

## 下载

[**下载轻松下载 Windows 完整包（ZIP）**](https://github.com/qibiaojia/easy-download/archive/refs/heads/main.zip)

完整解压后，打开 `easy-download-main` 文件夹，双击 `轻松下载.exe`。无需编译，保留旁边的 `engine` 文件夹。

## 使用

1. 下载本仓库 ZIP 并完整解压。
2. 双击根目录的 `轻松下载.exe`，粘贴文件下载直链后开始下载。
3. 保留 EXE 旁的 `engine` 文件夹。详细操作见 `使用说明.html`。

运行环境：64 位 Windows、.NET Framework 4.8。无需管理员权限。

## 源码与构建

界面、下载逻辑和测试源码位于 `source/`。在项目根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\source\build.ps1 -OutputDirectory .\work\dist
```

构建脚本默认使用仓库内的 `engine/aria2c.exe`。原有测试脚本包含本机路径，其他电脑运行前需要调整路径。

## 现有验证记录

`自检结果.json` 保存了 2026-11-02 的验证结果：14 项本地测试通过；独立用户会话的联网下载效果及实际提速尚未确认。本次上传保留原程序和原验证记录，没有重新执行这些功能测试。

## 许可

应用包装源码按 GNU GPL v2 或更新版本发布。许可见 `LICENSE.txt`，第三方来源见 `THIRD-PARTY-NOTICES.txt`。随仓库附带 aria2 引擎、许可及 `engine/source/aria2-1.37.0.tar.xz` 对应源码。

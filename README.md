# DeskSnip · 截图 · 钉图 · 录屏

Windows 桌面截图、标注、钉图与录屏工具，独立实现 PixPin 的常用操作流程。

## 用途 / 动机

希望有一款可以自行维护、按日常使用习惯扩展的桌面工具。功能参考 PixPin 公开手册，技术架构沿用 desk-pins。
## 快速开始

试用免安装版：解压 `dist/release/DeskSnip_0.1.0_win-x64-unpack.zip`，双击 `DeskSnip.exe`。仅支持 Windows 10 / 11 x64，需要 .NET Framework 4.8。开发者从源码运行：

```bash
sh manager.sh doctor   # .NET SDK / PowerShell / 目标框架
sh manager.sh dev            # 构建 Debug 并启动窗口
sh manager.sh package  # 出包到 dist/dev 与 dist/release
```

不想开终端：在资源管理器里双击 `dev.bat`（见下文「双击脚本」）。

## 日常使用

| 操作 | 入口 |
|---|---|
| 截图 | `Ctrl+Alt+A`，拖动框选后按 Enter 进入标注；Ctrl+A 选全部桌面 |
| 取色 / 微调 | 选区中 Ctrl+C 复制 HEX；方向键移动，Shift 调尺寸，Ctrl 加速 |
| 编辑输出 | 矩形、椭圆、直线、箭头、画笔、高亮、文字、序号、马赛克、遮挡；Ctrl+Z / Y 撤销重做，Ctrl+C / S / P 复制、保存、钉图 |
| 剪贴板钉图 | `Ctrl+Alt+P`；文件从“打开图片”进入编辑后钉图 |
| 钉图管理 | 拖动移动、滚轮缩放、Ctrl+滚轮透明度；右键旋转/镜像/锁定/穿透；控制台与托盘可解除穿透和恢复最近关闭钉图 |
| 录屏 | `Ctrl+Alt+R` 框选，选择 MP4 / GIF、帧率与位置后开始；录制时同一热键切换暂停，点击“停止保存”结束 |
| 历史 | 双击查看，支持另存为；MP4 可以“导出 GIF” |

快捷键可在“设置”中修改，冲突会提示。关闭主窗口默认收起到托盘；从托盘或窗口的“退出”结束程序并完成正在录制的文件。详细说明见 [使用手册](docs/02-使用说明.md)。

## manager.sh 命令一览

所有命令都通过 `sh manager.sh <命令>` 执行（Windows 用 Git Bash），与 `sh manager.sh help` 一致。开关写成 `--kebab-case`（会转成 PowerShell 的 `-PascalCase`）：

| 命令 | 说明 |
|---|---|
| `build` | 构建解决方案（Debug；`--configuration Release` 出 Release） |
| `dev` | 构建 Debug 并启动窗口 |
| `run` | 启动已构建的应用（没有构建产物时先构建） |
| `start` | 同 `run`（后台启动并记录 `.runtime/app.json`） |
| `stop` | 请求应用正常退出并等待最多 30 秒（绝不强杀） |
| `restart` | `stop` 后再 `start` |
| `status` | 查看是否在运行（pid、exe、启动时间） |
| `test` | `dotnet test` |
| `verify` | 交付前总检：test + 严格编译（warnaserror）+ check-docs |
| `package` | 出包到 `dist/dev`（Debug 调试包）与 `dist/release`（`unpack/` + 免安装 zip + SHA256SUMS） |
| `clean` | 清理 bin/obj、`dist/`、`.runtime/staging`、`.runtime/package.lock` |
| `doctor` | 检查 .NET SDK / PowerShell / 目标框架 |
| `check-docs` | 文档体系机械体检（同 `sh check-docs.sh`） |
| `help` | 显示本帮助 |

## 双击脚本（Windows）

根目录的 `.bat` 是给资源管理器里双击用的快捷入口；它们直接调用 `scripts\*.ps1`（逻辑与上表的命令同源），纯 cmd + PowerShell，无需 Git Bash。成功时直接退出，失败时暂停显示错误：

| 脚本 | 作用 |
|---|---|
| `dev.bat` | 构建 Debug 并启动窗口 |
| `run.bat [dev\|release]` | 启动已构建的应用（默认 Debug；release 用 Release） |
| `stop.bat` | 请求应用正常退出（等待最多 30 秒，不强杀） |
| `build.bat` | 构建解决方案 |
| `test.bat` | 跑全部测试 |
| `verify.bat` | 交付前总检（test + 严格编译 + check-docs） |
| `ci.bat` | clean → verify → package 一键流水线 |
| `package.bat` | 出包到 `dist/dev` 与 `dist/release` |
| `clean.bat` | 清理构建产物 |

设置环境变量 `DESK_SNIP_NO_PAUSE=1` 可跳过失败时的暂停（自动化 / CI 用）。

## 依赖与前置条件

- 构建需要 [.NET SDK](https://dotnet.microsoft.com/download) 与 .NET Framework 4.8 Developer Pack。
- 目标框架与界面：.NET Framework 4.8 / WPF / x64。
- Windows PowerShell 5.1 或 PowerShell 7（脚本同时兼容）。
- 运行环境：Windows 10 / 11 x64，.NET Framework 4.8；免安装 zip。

## 目录结构

```
src/DeskSnip.Core/   # 业务规则 / 配置 / 状态机（无 UI，xunit 覆盖）
src/DeskSnip.App/    # wpf 界面（薄，只做接线）
tests/DeskSnip.Tests/# xunit
scripts/                 # PowerShell 构建 / 启停 / 打包 / 清理
manager.sh               # 命令唯一来源
Directory.Build.props    # 版本号 / 目标框架唯一出处
```

> AI 协作说明：本项目采用一套「AI 协作文档体系」，AI 代理开工请先读 [`AGENTS.md`](./AGENTS.md)。

## 当前状态

第一版基础代码已实现：截图编辑、图片钉图、MP4 / GIF 画面录制、历史、托盘与设置。自动测试覆盖区域、数据、标注、原生编码/解码与暂停成片时间。桌面自动化当前无法捕获/激活窗口，完整交互验收仍待实测；源码状态与各项差异见 [功能对照](docs/03-功能对照.md)，阶段安排见 [路线图](ROADMAP.md)。

基础版录制画面及可选鼠标指针。GIF 使用 256 色，最多 15 FPS / 207 万像素，MP4 → GIF 默认 10 FPS；MP4 最大 4096 边长及 829 万像素，右/下边缘可缩减 1 px 以满足编码的偶数尺寸。运行不依赖 FFmpeg，N 版 Windows 需系统媒体组件。

OCR、长截图、音频同步、回放剪辑、自动窗口定位、开机恢复活动钉图与高级预设尚未实现。混合 DPI、多屏、HDR 和游戏捕获需要专项测试。

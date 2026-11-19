# DeskSnip · 截图 · 钉图 · 录屏

Windows 桌面截图、标注、钉图与录屏工具，独立实现 PixPin 的常用操作流程。

## 用途 / 动机

希望有一款可以自行维护、按日常使用习惯扩展的桌面工具。功能参考 PixPin 公开手册，技术架构沿用 desk-pins。
## 快速开始

试用免安装版：解压 `dist/release/DeskSnip_0.3.0_win-x64-unpack.zip`，双击 `DeskSnip.exe`。仅支持 Windows 10 / 11 x64，需要 .NET Framework 4.8。开发者从源码运行：

```bash
sh manager.sh doctor   # .NET SDK / PowerShell / 目标框架
sh manager.sh dev            # 构建 Debug 并启动窗口
sh manager.sh package  # 出包到 dist/dev 与 dist/release
```

不想开终端：在资源管理器里双击 `dev.bat`（见下文「双击脚本」）。

## 日常使用

| 操作 | 入口 |
|---|---|
| 截图 | `Ctrl+Alt+A`，框选后直接就地标注，Enter/Ctrl+C 复制；摄像头或 G 切换到录屏；移动鼠标自动识别窗口/UI 元素，Ctrl+A 当前屏/全部屏，Tab 定位窗口/原生控件 |
| 取色 / 微调 | C 复制颜色、Alt+C 坐标；方向键移动 1 px，Shift 收缩边、Ctrl 扩展边；位置/尺寸输入、放大镜与多种透明选区 |
| 编辑输出 | 二十种工具、同类标注直选、控制点缩放/端点调整、字体/粗斜体/圆角、图片内直接输入文字、对象复制与样式重编辑；分组图标直接用默认工具；Ctrl+C/S/P 复制/保存/钉图 |
| 剪贴板钉图 | `Ctrl+Alt+P`；文件从“打开图片”进入编辑后钉图 |
| 钉图管理 | Ctrl 多选移动、边缘缩放、中键还原/切回；选中后 Space 就地标注、写回当前图；Esc 直关、左键双击复制并关闭；悬停不显示角标；分组/批量、恢复、穿透解除与屏幕救援 |
| OCR / 长截图 | Shift+C 或“文字识别”使用 Windows 离线 OCR；L/“长截图”在选区左侧/右侧显示长图面板，滚动时自动采集拼接，Ctrl+T 钉图 |
| 录屏 | `Ctrl+Alt+R`，MP4/GIF/WebP、两路音轨、倒计时、最高 60 FPS；同键暂停，停止后普通模式只保留临时素材并打开紧凑回放编辑器（复制/保存时才写文件），快捷模式直接输出；历史录像打开普通窗口 |
| 联网识别 | 先在设置配置协议服务，手动请求文字/翻译/表格/公式/文档；可编辑导出和恢复上次结果 |
| 设置 / 保存 | 按参考图分组的标注/保存偏好、动作/内置键、鼠标组合与全局拖动、忽略进程、主题及可移植配置 |
| 界面语言 | 设置 → 系统 → 界面语言，选择简体中文、English 或跟随系统；保存后切换并记住选择 |

快捷键可在“设置”中修改，冲突会提示；输入框聚焦后按组合键即完成，Esc / Backspace 取消。取消截图不再二次确认。程序启动后默认只在托盘运行，从托盘菜单“打开主界面”打开主窗口；关闭主窗口默认收起到托盘，从托盘或窗口的“退出”结束程序并完成正在录制的文件。详细说明见 [使用手册](docs/02-使用说明.md)。

## manager.sh 命令一览

所有命令都通过 `sh manager.sh <命令>` 执行（Windows 用 Git Bash），与 `sh manager.sh help` 一致。开关写成 `--kebab-case`（会转成 PowerShell 的 `-PascalCase`）：

| 命令 | 说明 |
|---|---|
| `build` | 构建解决方案（Debug；`--configuration Release` 出 Release） |
| `assets` | 从矢量源重新生成蓝色应用图标及 16–256 像素 ICO |
| `dev` | 构建 Debug 并启动窗口 |
| `run` | 启动已构建的应用（没有构建产物时先构建） |
| `start` | 同 `run`（后台启动并记录 `.runtime/app.json`） |
| `stop` | 请求应用正常退出并等待最多 30 秒（绝不强杀） |
| `restart` | `stop` 后再 `start` |
| `status` | 查看是否在运行（pid、exe、启动时间） |
| `test` | `dotnet test`；可用 `--filter test-expression` 运行指定测试 |
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
| `dev.bat` | 构建 Debug 并启动窗口；若应用正在运行会先强制关闭 |
| `run.bat [dev\|release]` | 启动已构建的应用（默认 Debug；release 用 Release） |
| `stop.bat` | 请求应用正常退出（等待最多 30 秒，不强杀） |
| `build.bat` | 构建解决方案 |
| `test.bat` | 跑全部测试 |
| `verify.bat` | 交付前总检（test + 严格编译 + check-docs） |
| `ci.bat` | clean → verify → package 一键流水线 |
| `package.bat` | 出包到 `dist/dev` 与 `dist/release`；若应用正在运行会先询问是否强制关闭 |
| `clean.bat` | 清理构建产物 |

设置环境变量 `DESK_SNIP_NO_PAUSE=1` 可跳过失败时的暂停（自动化 / CI 用）；非交互环境需要确认强制关闭运行中的实例时用 `DESK_SNIP_FORCE_CLOSE=1`。

## 依赖与前置条件

- 构建需要 [.NET SDK](https://dotnet.microsoft.com/download) 与 .NET Framework 4.8 Developer Pack。
- 目标框架与界面：.NET Framework 4.8 / WPF / x64。
- Windows PowerShell 5.1 或 PowerShell 7（脚本同时兼容）。
- 运行环境：Windows 10 / 11 x64，.NET Framework 4.8；免安装 zip。
- 本地 OCR 使用 Windows 已安装语言；音频/WebP/剪辑/硬件最终导出与恢复需本机 FFmpeg/ffprobe，设置路径或放入 PATH。
- 联网操作需自行配置支持 [服务协议](docs/04-联网服务协议.md) 的服务地址；密钥由 Windows 当前账号加密保存。第三方组件见 [组件说明](docs/05-第三方组件.md)。

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

当前版本 0.3.0，已交付 B1–B8 的常用子集：精确选区/标注、钉图管理、离线 OCR/手动长截图、录屏音轨/剪辑/恢复、保存/设置和可配置识别服务，并完善截图/录屏/设置排版、中英文切换、蓝色应用图标、按工具变化的标注子操作栏、钉图渐变选中边框与主题右键菜单。用户已确认语言切换与任务栏图标正常；服务用本地模拟请求验证，XLSX/Word 通过独立 Office 格式校验，完整桌面交互、真实设备和实际服务质量仍待验收。发布记录见 [更新日志](CHANGELOG.md)，逐项差异见 [功能对照](docs/03-功能对照.md)，后续见 [路线图](ROADMAP.md)。

基础无音频 MP4/GIF 可只用系统组件；GIF 最多 15 FPS/207 万像素、256 色，基础 MP4 转 GIF 默认 10 FPS。MP4 最大单边 4096、829 万像素，右/下边缘可能缩减 1 px；N 版 Windows 需系统媒体组件。应用不下载或打包本机 FFmpeg。

仍待补：多类型贴图/缩略图、自动滚动/超长分块、高级对象变换、键鼠/摄像头、脚本/手势/系统集成、更多识别格式与服务商适配。混合 DPI、多屏、HDR 和游戏捕获需专项测试。

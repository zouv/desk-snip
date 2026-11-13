# 代码链路图谱 · DeskSnip

> 改结构时同步；按函数名定位，不依赖行号。工作规则见 AGENTS.md。

## 0. 架构

Windows 原生 WPF 多窗口应用，Core 管理规则与本地状态，App 管理桌面交互及系统接口。沿用 desk-pins 的 C# / .NET Framework 4.8 / x64 架构。

```text
WPF 控制台 / 截图选区 / 图像编辑 / 钉图 / 录屏控制
                        ↓
          Core：区域、标注、配置、历史、录制状态
                        ↓
       Windows：Win32 / GDI / Media Foundation
```

Core 不依赖 WPF。App 将输入转换为 Core 模型，再由 WPF 或 Windows 适配层执行。录制使用专用工作线程；导出使用后台任务。

## 0.5 任务作用域路由

| 任务 | 首读 | 可忽略 | 入口小节 |
|---|---|---|---|
| 启动、托盘、热键、设置 | App.xaml.cs、AppController.cs、MainWindow.xaml.cs、Windows/Hotkeys.cs、SettingsWindow.cs | 编码 | §2、§3 |
| 截图、坐标与取色 | Core/Geometry.cs、CaptureWindow.cs、Windows/ScreenCapture.cs | 历史播放 | §3、§4 |
| 标注、图像导出与变换 | Core/Annotations.cs、EditorWindow.cs、Imaging | 热键 | §2、§4 |
| 钉图与恢复 | Core/PinPresentation.cs、PinWindow.cs、AppController.cs | 录制 | §3 |
| 录屏与 GIF 转换 | Core/RecordingTimeline.cs、Core/GifWriter.cs、App/Recording、RecordingWindows.cs | 标注 | §3、§4、§7 |
| 配置、历史与路径安全 | Core/LocalData.cs、AppController.cs | 截图遮罩 | §4、§7 |
| 独立测试 | tests/DeskSnip.Tests（按对应文件名） | 真实桌面内容 | §6 |
| 构建、验证和打包 | manager.sh、scripts、Directory.Build.props | 桌面交互 | §6 |
| 功能范围与接力 | docs/03-功能对照.md、ROADMAP.md、HANDOFF.md | 模板工程 | §7 |

## 1. 文件职责

| 文件或目录 | 职责 |
|---|---|
| AGENTS.md / CLAUDE.md | 代理工作规则与导入入口 |
| ARCHITECTURE.md | 源码导航与接口约定 |
| ROADMAP.md | 开发规划及完成度 |
| HANDOFF.md | 当前任务与接力状态 |
| CHANGELOG.md | 使用者可感知的变更 |
| README.md / docs | 使用、维护及官方功能对照 |
| src/DeskSnip.Core | 无 UI 的规则与本地状态 |
| src/DeskSnip.App | WPF 窗口与 Windows 接口 |
| Core/Geometry.cs、Annotations.cs、PinPresentation.cs | 物理像素半开矩形、不可变标注与撤销、钉图显示规则 |
| Core/LocalData.cs | 路径校验、原子 JSON/文件写入、配置与历史索引 |
| Core/RecordingTimeline.cs、GifWriter.cs | 排除暂停的单调时间线、RGB332/LZW 流式 GIF |
| App/AppController.cs、App.xaml.cs | 多窗口接线、托盘、单实例唤起/退出、异步录制与收尾 |
| App/CaptureWindow.cs、EditorWindow.cs、PinWindow.cs | 框选、标注交互、无激活钉图 |
| App/MainWindow.xaml、SettingsWindow.cs、RecordingWindows.cs、PlaybackWindow.cs、Ui.cs | 控制台、设置、录制配置/控制、录屏预览与公共样式 |
| App/Imaging | 格式归一、原像素导出、标注合成、旋转与镜像 |
| App/Windows | GDI 帧采集、Win32 热键/窗口适配 |
| App/Recording | MF 编码/解码、捕获循环、MP4 → GIF |
| tests/DeskSnip.Tests | 独立数据的 xunit 测试 |
| scripts / manager.sh / *.bat | 构建、启停、验证、打包 |
| Directory.Build.props / DeskSnip.sln | 框架、版本与工程组织 |
| .claude / check-docs.sh / .vibe-template | 模板提供的协作文档工具 |
| .gitignore / .gitattributes | 仓库卫生与换行规则 |

## 2. 常见入口

| 任务 | 文件与符号 |
|---|---|
| 启动与单实例 | App.xaml.cs：OnStartup / OnExit；app.manifest |
| 控制台与托盘 | MainWindow.xaml；AppController.Show / RequestExit / TrayIcon |
| 截图与取色 | AppController.Select；CaptureWindow.Down / Move / KeyDownHandler；ScreenCapture.Crop |
| 标注编辑与导出 | AnnotationCanvas.Down / Up；AnnotationDocument.Add / Undo / Redo；AnnotationRenderer.Flatten；EditorWindow.Output |
| 图片导入与格式 | ImageFiles.Load / Normalize / Save；ImageTransforms.Apply |
| 钉图、穿透与恢复 | PinWindow.SetInteraction / ResizeImage / RestoreInteraction；AppController.TogglePins / RestorePin |
| 录屏状态与输出 | RecordingSession.Run / TogglePause / StopAsync；Mp4Writer.WriteFrame / Complete；GifWriter.WriteFrame |
| GIF 转换与播放 | VideoReader.ReadFrame；GifExporter.Export；AppController.ExportGif；PlaybackWindow |
| 设置与历史 | LocalStore.SaveSettings / AddImage / ImportRecording；AtomicFile.Write；ManagedDirectory.Resolve |
| 全局热键 | Hotkeys.Parse / Apply / Hook |
| 产品身份 | src/DeskSnip.Core/AppInfo.cs：AppInfo.ReadVersion |

## 3. 重点链路

- **截图**：Select 隐藏控制台/编辑器 → 等待桌面刷新 → 一次捕获虚拟桌面 → CaptureWindow 使用原图框选 → Crop → EditorWindow → Flatten → 复制 / 保存 / 钉图。选区不重新捕获，遮罩不会进入输出。
- **标注**：鼠标坐标转图像像素 → 不可变 Annotation → AnnotationDocument 快照 → WPF DrawingContext → 原尺寸 RenderTargetBitmap。显示缩放与输出分离。
- **钉图**：PinWindow 使用 NOACTIVATE/TOOLWINDOW → 右键菜单管理 → TRANSPARENT 穿透 → 主窗口/托盘 RestoreInteraction 解除；关闭后保留最近 10 张内存图，历史跨启动保留。
- **录制**：选区 → RecordingSetup → RecordingSession 专用线程 → GDI BGRA → NV12 + MF H.264 或 GIF → 等停止后提交临时文件 → 历史索引。相邻帧的时间差决定显示时长，暂停不进入成片。
- **退出**：托盘/窗口退出或命名 Stop 事件 → 取消并等待 GIF 转换 → 停止并完成录制 → 正常关闭自有窗口 → 释放热键、托盘及单实例句柄。

## 4. 接口契约

- PixelRect 为半开物理像素范围，允许负坐标；WPF 绘制与窗口尺寸只在边界换算 DIP。框选通过全局 GetCursorPos 取物理坐标。
- Annotation 为不可变点集合；撤销历史最多 256 个快照。新编辑清除 redo；标注在输出时合成，不保存可编辑工程文件。
- IFrameSource.Copy 必须填满紧密 BGRA32（top-down）帧。生产为 GDI，时间线集成测试注入合成帧，测试不采集用户桌面。
- RecordingTimeline 使用单调 TimeSpan；RecordingSession 的状态与计时在同一锁内读取，工作线程独占编码器。
- MF vtable 索引依据公开 SDK：IMFAttributes UINT32=21、UINT64=22、GUID=24；IMFSample time=36、duration=38、buffer=42；SinkWriter finalize=11。每次 COM 调用检查 HRESULT，生命周期匹配 CoInitializeEx / MFStartup。
- MP4 输入 NV12（BT.601、有限范围、偶数尺寸）；输出 H.264。VideoReader 优先 IMF2DBuffer.Lock2D 返回的顶行及带符号 pitch，避免解码上下翻转。
- GifWriter 使用 RGB332 全局调色板，逐帧 LZW、可循环；直接录制最多 15 FPS / 207 万像素，转换默认 10 FPS。
- LocalStore 只接受生成的 GUID 文件名；配置/历史通过同目录原子替换提交。ManagedDirectory 拒绝越界/联接点；显式保存路径也经 AtomicFile.ValidateTarget 规范化、拒绝链接。

## 6. 构建与脚本

- manager.sh 分发到 scripts/*.ps1；Windows 的 *.bat 调用同一组 PowerShell 脚本。
- Directory.Build.props 是版本与 WindowsTfm / BaseTfm 的唯一出处。
- scripts/desktop.ps1 记录自启进程身份，通过进程专属命名事件请求退出；旧版本退回 CloseMainWindow，等待超时不强杀。
- scripts/package.ps1 在 .runtime/staging 构建校验后事务替换 dist/dev、dist/release。
- 测试使用临时目录与合成帧，包括 GIF 独立解码、MP4 容器和原生解码、暂停输出时长、写入失败保留旧文件；桌面实测与混合 DPI 另行验收。

## 7. 设计约束

- 第一版聚焦截图、标注、钉图、录屏；OCR、长截图与高级编辑独立排期。
- 仅复现公开行为，使用独立实现和自制图标，不复制 PixPin 源码或资源。
- 普通用户运行；快捷键冲突必须可见；录制开始与停止由用户触发。
- 源码或文档不宣称未实测的多屏、HDR、游戏捕获、系统重启或性能。
- MP4 使用系统 Media Foundation，不调用 FFmpeg。N 版 Windows 需要系统媒体组件；不代装。当前只录画面和可选指针，音频/剪辑在后续阶段。
- 录制先写同目录临时文件，完成编码后才替换输出；正常退出等待保存。崩溃/断电后恢复未完成录制尚未实现。
- 初始化期间停止也收尾为 Stopped；保存失败在退出前给出提示。编码器、帧缓冲与文件流在异常时同样释放。
- 图像历史保留上限与钉图恢复不同；活动钉图持有图像，历史淘汰不会关闭它。录屏原文件与历史副本分别保存。
- 图片超出屏幕时钉图先适合屏幕，再按显示比例缩放；旋转/镜像修改当前钉图导出内容，缩放/不透明度不改原图。
- Ctrl+A 框选全部桌面；双击选区进入编辑。窗口/控件自动识别、八向拖拽控制点、放大镜、位置输入尚未实现。
- 第二实例只唤起已有实例。进程启停脚本启动的是交互产品窗口，后台助手另行使用隐藏窗口。

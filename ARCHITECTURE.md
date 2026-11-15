# 代码链路图谱 · DeskSnip

> 改结构时同步；按函数名定位，不依赖行号。工作规则见 AGENTS.md。

## 0. 架构

Windows 原生 WPF 多窗口应用，Core 管理规则与本地状态，App 管理桌面交互及系统接口。沿用 desk-pins 的 C# / .NET Framework 4.8 / x64 架构。

```text
WPF 控制台 / 截图选区 / 图像编辑 / 钉图 / 录屏控制
                        ↓
          Core：区域、标注、配置、历史、录制状态
                        ↓
       Windows：Win32 / GDI / Media Foundation / OCR / WASAPI
                  可配置 FFmpeg 和 JSON 识别服务
```

Core 不依赖 WPF。App 将输入转换为 Core 模型，再由 WPF 或 Windows 适配层执行。录制使用专用工作线程；导出使用后台任务。

## 0.5 任务作用域路由

| 任务 | 首读 | 可忽略 | 入口小节 |
|---|---|---|---|
| 启动、托盘、热键、设置 | App.xaml.cs、AppController.cs、Windows/Hotkeys.cs、Windows/SystemIntegration.cs、Windows/PinFileChannel.cs、SettingsWindow.cs、SettingsWindow.Pages.cs、SettingsWindow.ReferenceControls.cs | 编码 | §2、§3、arch/preferences-and-saving.md |
| 截图、坐标与取色 | Core/Geometry.cs、SelectionGeometry.cs、PixelColor.cs、CaptureWindow.cs、SelectionInfoBar.cs、PixelMagnifier.cs | 历史播放 | §2、§3、§4 |
| 截图 / 编辑器 / 录屏排版 | App/FloatingToolbar.cs、InlineAnnotationBar.cs、AnnotationOptionsBar.cs、CaptureWindow.Redraw、SelectionInfoBar、EditorWindow、RecordingWindows | 编码与存储 | §2、§7 |
| 标注、图像导出与变换 | Core/Annotations.cs、EditorWindow.cs、Imaging | 热键 | §2、§4 |
| 选区预设、非矩形与扩展标注 | CaptureProfiles.cs、AnnotationAppearance.cs、PixelEffects.cs、PrivacyDetector.cs、AppController.Capture.cs、CaptureTargetReader.cs、CaptureMasks.cs | 编码 | arch/capture-and-annotation.md |
| 钉图与恢复 | Core/PinPresentation.cs、PinWorkspace.cs、PinWindow.cs、PinWindow.Menu.cs、PinManagerWindow.cs、AppController.Pins.cs | 录制 | §3 |
| 本地 OCR 与长截图 | Core/RecognitionText.cs、LongScreenshot.cs、App/Recognition、OcrWindow.cs、LongScreenshotBar.cs | 编码 | arch/recognition.md |
| 联网识别、表格/公式/文档输出 | Core/RecognitionService.cs、StructuredTable.cs、RecognitionExport.cs、DocumentExport.cs、MarkdownContent.cs、App/Recognition/ServiceClient.cs、RecognitionServiceWindow.cs | 原生编码 | arch/recognition-service.md、docs/04-联网服务协议.md |
| 录屏与导出 | Core/RecordingTimeline.cs、RecordingJournal.cs、VideoEditing.cs、App/Recording、RecordingWindows.cs、PlaybackWindow.cs | 标注 | §3、arch/recording-enhancements.md |
| 配置、保存与路径安全 | Core/LocalData.cs、AppPreferences.cs、ReferencePreferences.cs、ReferenceBehavior.cs、ImageSaving.cs、PreferenceTransfer.cs、AppController.Saving.cs、SettingsWindow.cs | 编码 | §4、arch/preferences-and-saving.md |
| 标注设置、动作、内置键与鼠标 | Core/InputPreferences.cs、InputBindings.cs、MouseGestureSession.cs、SettingsWindow.Input.cs、CaptureWindow.Input.cs、PinWindow.Input.cs、EditorWindow.Preferences.cs、AppController.Actions/GlobalMouse.cs | 编码 | arch/preferences-and-saving.md、arch/capture-and-annotation.md |
| 独立测试 | tests/DeskSnip.Tests（按对应文件名） | 真实桌面内容 | §6 |
| 中英文、品牌与任务栏 | Core/InterfaceLanguage.cs、TextCatalog.cs、Resources/English.xml、App/Localization.cs、ProductWindow.cs、Branding.cs、Assets | 媒体编码 | §2、§3、§4、§7 |
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
| Core/Geometry.cs、Annotations.cs、AnnotationAppearance.cs、PinPresentation.cs | 物理像素半开矩形、不可变标注/字体与圆角样式、对象编辑快照、钉图显示规则 |
| Core/SelectionGeometry.cs、PixelColor.cs | 选区八向调整、键盘边缘微调、比例/对称约束、整数输入校验与颜色格式 |
| Core/LocalData.cs | 路径校验、原子 JSON/文件写入、配置与历史索引 |
| Core/ReferencePreferences.cs、ReferenceBehavior.cs | 参考设置字段、旧配置默认值及校验，检测边距、复制 DPI、钉图锁定与缩放基准点规则 |
| Core/InputPreferences.cs、InputBindings.cs、MouseGestureSession.cs | 标注偏好、场景键位/鼠标目录与冲突、独立配置列表、物理区域拖动及钉图单击/拖动判定 |
| Core/ServicePreferences.cs、RecognitionService.cs、App/SettingsWindow.Service.cs | 服务偏好、DPAPI/环境变量凭据、原图/请求/结果本地保留与设置保存事务 |
| Core/RecordingTimeline.cs、GifWriter.cs | 排除暂停的单调时间线、RGB332/LZW 流式 GIF |
| Core/RecordingJournal.cs、VideoEditing.cs、WaveRecovery.cs、App/Recording/Ffmpeg.cs、AudioCapture.cs | 可恢复片段、剪辑规则、音频同步与 WAV 修复、媒体导出及 WASAPI 适配 |
| App/AppController.cs、App.xaml.cs | 多窗口接线、托盘、单实例唤起/退出、异步录制与收尾 |
| App/CaptureWindow.cs、EditorWindow.cs、PinWindow.cs | 框选、标注交互、初始不激活的可交互钉图 |
| Core/PinWorkspace.cs、App/AppController.Pins.cs、PinWindow.cs、PinWindow.Input/Menu、PinManagerWindow.cs | 独立钉图图像/布局、分组与关闭上限、多选和批量管理、主题菜单与选中渐变边框 |
| Core/RecognitionText.cs、LongScreenshot.cs、App/Recognition、OcrWindow.cs、LongScreenshotBar.cs | 离线 OCR、校对排版与纵横长图重叠匹配；截图会话内的长图采集条 |
| App/SelectionInfoBar.cs、PixelMagnifier.cs、Imaging/PixelInspector.cs | 折叠位置尺寸标签与原子应用输入、冻结原图取色、边缘像素放大与坐标显示 |
| App/FloatingToolbar.cs | 独立矢量图标、图标按钮/菜单与可访问名称、圆角浮动栏、避让和屏幕边缘定位 |
| App/InlineAnnotationBar.cs、AnnotationOptionsBar.cs | 就地标注的主工具条与按当前工具重建的子操作栏；控件与画布选中样式双向同步 |
| App/MainWindow.xaml、SettingsWindow.cs、RecordingWindows.cs、PlaybackWindow.cs、Ui.cs | 控制台、设置、录制配置/控制、录屏预览与公共样式 |
| App/SettingsWindow.Pages/ReferenceControls/Input.cs、SettingsLayout.cs、SettingsControlStyles.xaml | 15 类侧栏、分组偏好、动作/八场景按键、鼠标组合、颜色/数字、依赖草稿及固定操作底栏 |
| App/Windows/ShortcutKeys/ForegroundInput/GlobalMouse.cs、AppController.Actions/GlobalMouse.cs、GlobalMouseOverlay.cs | 按键格式/匹配、前台忽略/全屏判断、全局鼠标钩子及区域分派/遮罩 |
| App/CaptureWindow.Input/PinWindow.Input/EditorWindow.Preferences.cs | 可配置内置键、钉图鼠标/侧键/拖动及标注偏好接线 |
| App/Imaging | 格式归一、原像素导出、标注合成、旋转与镜像 |
| App/Windows | GDI 帧采集、Win32 热键/窗口适配 |
| App/Windows/SystemIntegration.cs、PinFileChannel.cs | 当前账号的自启/图像右键入口及注册表回滚；有长度与超时限制的单实例图像命令 |
| App/Recording | MF 编码/解码、捕获循环、MP4 → GIF |
| App/IBackgroundWindow.cs | 回放/恢复/联网窗口取消并等待后台任务后关闭的公共契约 |
| Core/InterfaceLanguage.cs、TextCatalog.cs、Resources/English.xml | 支持的语言规则、产品文案与独立格式参数，界面切换不翻译用户内容 |
| App/Localization.cs、ProductWindow.cs、Branding.cs、Assets | 产品窗口文案刷新、统一蓝色图标、原生 DPI 图标与进程应用标识；矢量源及 PNG/ICO |
| licenses/、docs/05-第三方组件.md | 随包第三方许可文本与依赖说明 |
| tests/DeskSnip.Tests | 独立数据的 xunit 测试 |
| scripts / manager.sh / *.bat | 构建、启停、验证、打包 |
| Directory.Build.props / DeskSnip.sln | 框架、版本与工程组织 |
| .claude / check-docs.sh / .vibe-template | 模板提供的协作文档工具 |
| .gitignore / .gitattributes | 仓库卫生与换行规则 |

## 2. 常见入口

| 任务 | 文件与符号 |
|---|---|
| 启动与单实例 | App.xaml.cs：OnStartup / OnExit；PinFileChannel.Send / ListenAsync / Dispose；SystemIntegrationChange.Commit / Dispose |
| 控制台与托盘 | MainWindow.xaml；AppController.Show / RequestExit / ApplyTrayAppearance / RunTrayAction |
| 截图自动识别 | CaptureTargetReader.Snapshot；CaptureTargets.At；UiAutomationTargetReader.ElementAt；CaptureWindow.UpdateHover / QueueUiAutomation / UiAutomationLoop | 编码 | §3 |
| 截图与选区交互 | AppController.Select / CompleteCapture / LongScreenshot / RecordRegion；CaptureWindow.BeginInteraction / UpdateInteraction / SetSelection / BeginInlineEdit / BuildInlineOutput / SwitchToRecording；LongScreenshotBar；SelectionGeometry.Transform / AdjustEdge / TryParse |
| 浮动界面与标注入口 | FloatingLayout.Actions / Info / Modes；CaptureWindow.Redraw / AnnotationTool；SelectionInfoBar.SetExpanded；AppController.EditWithTool；EditorWindow.AddToolGroup；AnnotationOptionsBar.Rebuild / RefreshState |
| 放大镜与取色 | CaptureWindow.KeyDownHandler；PixelInspector.Sample / Viewport；PixelColor.Format / Coordinates；PixelMagnifier.OnRender |
| 标注编辑与导出 | AnnotationCanvas.Down / Up / SelectAt / TryBeginHandleDrag / UpdateHandleDrag / ChangeCorner / OpenTextEditor；AnnotationDocument.Add / Undo / Redo；AnnotationRenderer.Flatten / CornerRadius；InlineAnnotationBar；AnnotationOptionsBar；AnnotationToolDefaults；FloatingTools.SplitMenuButton；EditorWindow.Output（打开图片 / 历史 / 完整编辑器） |
| 标注对象选择与移动 | AnnotationRenderer.HitTest / Bounds；AnnotationCanvas.SelectAt / BeginObjectDrag / UpdateObjectDrag / EndObjectDrag；AnnotationDocument.PreviewMove / Move |
| 标注删除、样式与文字 | AnnotationDocument.Replace / Remove；AnnotationCanvas.ChangeStyle / ReplaceSelectedText / DeleteSelected；EditorWindow.ApplyFont；TextPrompt |
| 图片导入与格式 | ImageFiles.Load / Normalize / Save；ImageTransforms.Apply |
| 钉图、穿透与恢复 | PinWindow.SetInteraction / ResizeImage / RestoreInteraction / BeginInlineEdit / CommitInlineEdit / CopyAndClose / CloseFromEscape / ToggleSelectionBorder / BuildContextMenu；PinSelectionGlow；AppController.TogglePins / RestorePin |
| 录屏状态与输出 | RecordingSession.Run / TogglePause / StopAsync；Mp4Writer.WriteFrame / Complete；GifWriter.WriteFrame；PlaybackWindow（录制结束时就地覆盖录制区域） |
| 录屏控制与区域边框 | RecordingBar.Position / Update / ShowFinishing；RecordingRegionWindow；RecordingSetup 的高级选项折叠 |
| GIF 转换与播放 | VideoReader.ReadFrame；GifExporter.Export；AppController.ExportGif；PlaybackWindow |
| 设置与历史 | LocalStore.SaveSettings / AddImage / ImportRecording；AtomicFile.Write；ManagedDirectory.Resolve |
| 设置导航与草稿 | SettingsWindow.SelectPage / ReadValues / RestoreCurrentPage；ShortcutEditRules.Resolve；BuildPages / MirrorBoolean / EnableWhen / AddSavePreview；SettingsLayout.Section / Row；Ui.ApplyThemeResources |
| 输入与标注设置 | BuildAnnotationPage / RenderActions / BuildBuiltinPage / BuildMouseRows / ReadInputValues / RestoreInputPage；OptionalColorField / NumberInput；ShortcutKeys.Display / FromDisplay / Match |
| 输入动作接线 | CaptureWindow.HandleConfiguredKeys；PinWindow.InitializeConfiguredMouse / HandleConfiguredKey；EditorWindow.HandleConfiguredShortcut；AppController.ApplyTrayActions / ApplyGlobalMouse / FinishGlobalGesture |
| 手动、快速与自动保存 | AppController.SaveImageManual / SaveImageToPath / SaveImageQuick / SaveAutomatically / CopyImage；AutomaticArchive.Save；SaveNaming.Expand / ChoosePath |
| 全局热键 | Hotkeys.Parse / Apply / Hook |
| 产品身份 | src/DeskSnip.Core/AppInfo.cs：AppInfo.ReadVersion |
| 界面语言 | InterfaceLanguageRules.Resolve；TextCatalog.Get / Format / Refresh；L.Apply / Refresh；ProductWindow.OnInterfaceLanguageChanged；SettingsWindow.OnInterfaceLanguageChanged |
| 图标与任务栏 | Branding.Initialize / Image / CreateTrayIcon / ApplyWindowIcon；ProductWindow.WindowMessage；scripts/assets.ps1 与 manager.sh assets |

## 3. 重点链路

- **界面语言**：读取配置 → L.Apply → 解析 System/zh-CN/en-US → 只修改应用 UI 文化 → ProductWindow 弱事件更新显示属性、未显示设置页和产品选项模板。保存成功后再切换；本机配置、快捷键原键名、用户内容与识别服务语言独立。
- **统一图标**：矢量源生成 16–256 ICO/PNG → EXE ApplicationIcon 与内嵌资源 → 冻结的独立 BitmapSource 和托盘图标 → SourceInitialized 设置大小 HICON。DPI 或 ShowInTaskbar 改变后刷新，进程 AppUserModelID 保持一致。

- **截图**：Select 隐藏控制台/编辑器 → 等待桌面刷新 → CapturePair 同帧生成干净图/指针图 → CaptureWindow 按偏好选择图源、检测/放大镜/鼠标行为 → Crop → CompleteCapture 分派复制 / 保存 / 钉图 / 编辑。未框选时 UpdateHover 先用 Win32 窗口/子 HWND，再向后台 UIA 线程投递最新光标请求，递归定位最深可见元素（结果回 UI 线程应用，取更小区域），自动框选随鼠标刷新；工具按钮直接 BeginInlineEdit 在选区内标注，选区固定，输出 InlineOutput；未完成选区时 Esc 直接取消，CaptureWindow 在 Loaded 强制前台/键盘焦点。选区工具栏的摄像头或 G 切到同一窗口的录屏工具栏（CaptureAction.Record），工具分组主区域直接使用组内默认工具，序号有独立按钮。取色不采集遮罩；复制可按选区屏幕 DPI 缩小，坐标及保存仍为原像素。Esc / 右键取消截图不再二次确认。
- **精确选区**：物理鼠标点 → HitTest 边角/内部 → Transform 约束移动/缩放 → SetSelection/Redraw。Space 切换调整与临时移动时以当前选区重新设锚点，防止跳变。四项数值先一起校验再应用，颜色刷新保留尚未应用的输入。
- **浮动工具**：Redraw 将物理选区转为 DIP → 测量工具 → FloatingLayout 外侧优先定位并避让；尺寸标签展开后重新定位。标注入口使用 CaptureAction 映射初始工具，输出仍经同一 CompleteCapture / EditorWindow 链路。
- **标注**：鼠标坐标转图像像素 → 不可变 Annotation → AnnotationDocument 快照 → WPF DrawingContext → 原尺寸 RenderTargetBitmap。活动工具按类别命中同类标注（形状/线条/画笔/文字等），点击即可选中；选中形状显示四角调整柄，箭头/直线显示端点柄，拖动经预览后一次替换提交。文字在图片位置用就地 TextBox 输入，提交后生成/替换 Text/Watermark；样式支持颜色、粗细、填充、线型、箭头、透明度、效果、粗体/斜体/字体/字号、矩形方角/圆角及 0–400 px 圆角半径。显示缩放与输出分离。截图和钉图使用 AnnotationCanvas + InlineAnnotationBar 就地编辑；工具栏分组按钮主区域直接使用组默认工具，右侧箭头 / 右键选择其他工具，选择经 AnnotationToolDefaults + MarkToolDefaults 持久化。切换工具时 AnnotationOptionsBar 在工具条下方重建该工具的子操作栏（形状含矩形/椭圆/圆角、填充、线型、线宽、圆角半径与颜色；线条/画笔/文字各有对应控件），与“标注样式”菜单共享同一画布状态。打开图片/历史仍可进入完整 EditorWindow，其工具栏不再提供独立“选择 / 移动”按钮。
- **对象拖动**：记录选中对象/起点 → 根据实际绘制范围限制位移 → PreviewMove 仅产生预览 → 松开时替换对象或追加副本，仅提交一次。选中框/拖动预览不进入导出；Document.Revision 仅随文档操作改变，选择和默认样式不产生重复历史。
- **钉图**：PinWorkspace 独立保存图片/原图与 pins.json → PinWindow 应用新图位置/透明度/置顶及可配置锁定限制 → 状态延迟落盘。选中钉图按 Space / E 或右键“继续标注”进入就地标注，Space / Enter 合成并写回当前图，Esc 取消；编辑期间禁用移动/缩放/替换。钉图不显示右上角按钮；选中时由 PinSelectionGlow 绘制淡黄色渐变边框（越靠外越透明），按 Pin 场景“显示 / 隐藏选中边框”（默认 F）切换。右键菜单由 PinWindow.Menu 构建应用主题样式，每项带矢量图标和快捷键文本。Esc 直接关闭（无确认），左键双击固定复制当前图并关闭。穿透由独立工具栏或主窗口/托盘解除；工具栏跟随显隐，在有效关闭前销毁。设置与管理器共用恢复/历史偏好，活动图不参与关闭记录淘汰。
- **录制**：选区 → RecordingSetup → RecordingSession 专用线程 → GDI BGRA → 基础 MF/GIF 或约 2 秒 MF 片段 + 独立 WASAPI 音轨 → FFmpeg 合并/混音/导出 → 原子提交目标和历史。相邻帧时间差决定显示时长，暂停不进入成片；增强素材与历史分开保留。刚停止的录制以 PlaybackWindow 无标题栏浮层覆盖录制区域（保留时间轴/裁剪/音量/导出），历史录像仍用普通窗口。
- **联网识别**：明确请求 → PNG/文字和语言 JSON → 可配置 HttpClient → 校验操作/大小/资产 → 先本地保存再显示结构化编辑器 → 校对/文件导出。打开窗口和恢复不联网，失败/取消不替换旧结果；细节见 arch/recognition-service.md。
- **退出**：托盘/窗口或命名 Stop 事件 → 取消并等待 GIF 转换 → 完成录制 → IBackgroundWindow 取消并等待回放/恢复/联网任务 → 保存钉图并正常关闭窗口 → 释放热键、托盘及单实例句柄。
- **设置保存**：ReadValues 克隆并整体验证 → 密钥事务 → SystemIntegrationChange 修改已变更的专属入口 → LocalStore 原子保存 → 提交系统入口 → 刷新主题/钉图/托盘/热键；保存失败回滚凭据与注册表值。读取/切页不修改系统配置。设置窗口打开期间暂停本应用全局热键，快捷键框才能收到已注册组合；关闭后按最新配置恢复。

## 4. 接口契约

- `AppSettings.InterfaceLanguage` 仅允许 System/zh-CN/en-US，旧配置补 zh-CN；System 在中文系统用中文，其余系统用英文。仅缺少语言字段的旧配置将默认 AccentColor #008978 迁移为 #3489F5；已有语言字段的显式颜色在重载时保留。
- `TextCatalog.Get/Format` 标记产品文案，格式参数保持独立；Refresh 只更新已标记文案。用户标题、校对文字、文件名、表格/公式/Markdown 与原键名不能按内容匹配翻译。

- PixelRect 为半开物理像素范围，允许负坐标；WPF 绘制与窗口尺寸只在边界换算 DIP。框选通过全局 GetCursorPos 取物理坐标。
- SelectionGeometry 保持至少 1 px 的非空尺寸与桌面范围；锁定比例在整数像素下允许舍入误差。Shift/方向键收缩对应边，Ctrl/方向键扩展对应边；没有修饰键时移动 1 px。
- PixelInspector 只接收与物理范围一致的 BGRA32 冻结图；取样限制在半开范围内。放大镜在桌面边缘保留中心像素的位置；颜色格式不依赖本机区域设置。
- Annotation 为不可变点集合，包含字体与独立字号；WithPoints/WithText/WithStyle 保留其它样式。撤销历史最多 256 个快照。新编辑清除 redo；Replace/Remove/Move 先验证再提交，失败保留当前快照与 redo。标注在输出时合成，不保存可编辑工程文件。
- 文档级移动限制控制点在画布内，界面再按实际绘制范围限制位移；文字按起点到图像右边的可用宽度自动换行。绘制与命中共用字体/字号，空文字提交删除对象且可撤销。
- IFrameSource.Copy 必须填满紧密 BGRA32（top-down）帧。生产为 GDI，时间线集成测试注入合成帧，测试不采集用户桌面。
- RecordingTimeline 使用单调 TimeSpan；RecordingSession 的状态与计时在同一锁内读取，工作线程独占编码器。
- MF vtable 索引依据公开 SDK：IMFAttributes UINT32=21、UINT64=22、GUID=24；IMFSample time=36、duration=38、buffer=42；SinkWriter finalize=11。每次 COM 调用检查 HRESULT，生命周期匹配 CoInitializeEx / MFStartup。
- MP4 输入 NV12（BT.601、有限范围、偶数尺寸）；输出 H.264。VideoReader 优先 IMF2DBuffer.Lock2D 返回的顶行及带符号 pitch，避免解码上下翻转。
- GifWriter 使用 RGB332 全局调色板，逐帧 LZW、可循环；直接录制最多 15 FPS / 207 万像素，转换默认 10 FPS。
- LocalStore 只接受生成的 GUID 文件名；配置/历史通过同目录原子替换提交。ManagedDirectory 拒绝越界/联接点；显式保存路径也经 AtomicFile.ValidateTarget 规范化、拒绝链接。
- AppSettings 新增参考偏好在 OnDeserializing 初始化默认值；可用项必须有消费入口。导出显式白名单排除目录、服务/代理连接信息；界面不可用项不生成虚假字段。
- InputBindingCatalog 分八个场景，未开放钉图类型不允许启用键位；BuiltinShortcuts/PinMouseBindings/GlobalMouseBindings 为可空独立列表，空键表示清除默认，null 保留默认/旧配置。Core 验证别名及小键盘合并冲突；局部场景优先于通用键位。
- 首次载入缺少钉图恢复/历史字段的旧配置时，AdoptLegacyPinPreferences 沿用工作区原值，再 Configure；明确的新偏好优先，迁移不得降低旧上限或开启原已关闭的启动恢复。
- PinFileChannel 仅允许当前账号，UTF-8 路径长度为 1–32768 字节；连接后 3 秒内收齐帧，残缺/非法输入关闭连接并继续监听，Dispose 关闭并等待监听任务。
- 快捷键输入框聚焦后用 `ShortcutEditRules.Resolve` 判定：Esc / Backspace 恢复进入前的值，Delete 清空，纯修饰键忽略，其余组合立即写入并移出焦点；设置窗口打开期间 AppController 暂停已注册全局热键，关闭后按最新配置恢复。
- 截图工具按钮进入 `CaptureWindow.BeginInlineEdit`，标注画布覆盖固定选区；复制/保存/钉图/识别时由 `BuildInlineOutput` 合成 `InlineOutput`，AppController 不再为截图打开 EditorWindow。取消截图直接关闭，`ScreenshotCloseConfirmation` 仅保留为旧配置兼容字段。
- `PinWindow.BeginInlineEdit` 在钉图内容上覆盖画布并用 Popup 工具栏编辑；Space / Enter 提交合成图并经 persist 写回，Esc 取消；编辑期间拦截移动、缩放、替换、关闭快捷键与右键菜单。
- `MarkToolDefaults` 保存 `Group=Kind` 逗号列表（组名来自 ToolbarGroups，Kind 必须属于该组）；AnnotationToolDefaults 读取/序列化并在选择变化时经 AppController 原子保存。`MarkRememberToolVariants` 仅为旧配置兼容字段。
- Annotation 增加 Bold/Italic，With*/Flatten 保持；AnnotationCanvas 按工具类别命中（形状/线条/画笔/文字/马赛克/遮挡/橡皮擦/聚光），选中后提供四角缩放柄和线/箭头端点柄，拖动结束一次 Document.Replace。文字用图片位置的透明 TextBox Popup，Enter 提交、Shift+Enter 换行、Esc 取消；输入期间不进入导出。
- `AnnotationAppearance.CornerRadius` 为 0–400 px，0 表示直角（RoundedRectangle 仍用自动圆角）；渲染时夹到短边一半。`WithEditorStyle` 的 `cornerRadius` 为空时保留原值。
- `AnnotationOptionsBar` 订阅 `AnnotationCanvas.ToolChanged` 重建控件、订阅 `Changed` 刷新选中状态；只显示当前工具适用的控件，所有修改经 `ChangeStyle` / `ChangeAppearance` 落到画布或选中对象。InlineAnnotationBar 由主工具条与子操作栏两个 FloatingToolbar 纵向排列。
- PinWindow 不显示右上角按钮；Selected 时用 `PinSelectionGlow` 绘制由外向内渐强的淡黄色边框，`ToggleSelectionBorder` 由 Pin 场景 `Border`（默认 F）触发且仅在选中时生效。右键菜单由 `BuildContextMenu` 生成，包含图标、`InputGestureText` 和主题化 ContextMenu/MenuItem 模板。Esc 走 CloseFromEscape（无确认），左键双击固定 CopyAndClose，二者仍受锁定关闭限制；`PinDoubleClickAction` 降为旧配置兼容字段，设置页不再提供双击动作选择。
- LongScreenshotBar 用 RegisterHotKey 在其窗口句柄上注册 Ctrl+T，WM_HOTKEY 直接 Finish(Pin)，关闭时注销；未注册成功时仍可用面板焦点快捷键与菜单。
- `LongScreenshotBar` 在截图选区左侧或右侧显示长图面板（按工作区空间选择一侧），固定区域每约 600ms 自动采集，`LongScreenshot` 只提交可信重叠；面板预览自动滚到新内容，方向/手工重叠/裁掉前后/撤销在“更多”中，输出经 AppController 走复制/保存/钉图/OCR/编辑。选区用点击穿透边框标识；旧独立长截图窗口已移除。
- `PlaybackWindow` 的 `inlineRegion` 模式为无标题栏、置顶、最小 820×560 的覆盖层，按录制区域居中并夹在工作区内；关闭按钮/ Esc 走 CloseSafelyAsync。无区域的历史录像保持普通窗口。
- `CaptureWindow.recordingMode` 初始由构造参数决定，截图窗口可经摄像头或 Screenshot 上下文的 Record 动作（默认 G）切到录屏工具栏；两个 FloatingToolbar 共用选区，截图分组主按钮直接 BeginInlineEdit 组内第一项，右键/右侧箭头选择其他工具。`CaptureAction.Record` 由 AppController 交给 RecordRegion，不再另开选择窗口。
- `ScreenshotUiDetection` 的 Elements 模式在 Win32 目标之外调用 UiAutomationTargetReader：从光标所在顶层窗口的 UIA 子树逐层选择包含光标的子元素，超过 16 层或 256 个子元素时放弃；结果与 Win32 目标取面积更小者。查询在独立 MTA 线程合并最新请求，UI 线程只投递和应用；120ms 定时器 + 移动 2px/70ms 限频，异常时回退 Win32，不记录元素文本。

## 6. 构建与脚本

- manager.sh 分发到 scripts/*.ps1；Windows 的 *.bat 调用同一组 PowerShell 脚本。
- verify 汇总测试、非增量严格编译和文档检查；测试失败仍运行后两项，最终保留非零退出码。
- Directory.Build.props 是版本与 WindowsTfm / BaseTfm 的唯一出处。
- scripts/desktop.ps1 记录自启进程身份，通过进程专属命名事件请求退出；旧版本退回 CloseMainWindow，等待超时不强杀。
- scripts/package.ps1 在 .runtime/staging 构建校验后事务替换 dist/dev、dist/release。
- 测试使用临时目录与合成帧，包括 GIF 独立解码、MP4 容器和原生解码、暂停输出时长、写入失败保留旧文件；桌面实测与混合 DPI 另行验收。

## 7. 设计约束

- L.Refresh 只更新显示属性，不写 ComboBox.Text、SelectedItem/SelectedValue、DataContext 或 TextBox.Text；集合用显示模板切换文案，不能 Items.Refresh 触发 OCR 重识别/重排或重置标注样式。不可用项的只读状态用 L.BindReadOnlyLabel 显式绑定语言，不能把用户内容当作产品标签。专用快捷键输入在 loading 保护下更新显示，保留原键值与草稿标志。
- 冻结 BitmapFrame 仍保留创建线程的 BitmapDecoder，WPF 窗口图标可能跨 STA 失败；Branding.Image 用独立 WriteableBitmap 再 Freeze，原生 Icon 缓存到应用退出后释放。

- B1–B8 已提供常用子集；批次代码完成不代表 PixPin 全功能复刻，详细缺失项保留在 docs/03-功能对照.md。
- 仅复现公开行为，使用独立实现和自制图标，不复制 PixPin 源码或资源。
- 普通用户运行；快捷键冲突必须可见；录制开始与停止由用户触发。
- 源码或文档不宣称未实测的多屏、HDR、游戏捕获、系统重启或性能。
- 基础无音频 MP4/GIF 使用系统组件，增强录制/剪辑/WebP/恢复依赖用户配置的 FFmpeg/ffprobe，不自动安装/捆绑。硬件选择作用于最终导出，捕获片段仍使用 MF；N 版 Windows 需要系统媒体组件。
- 目标在完整编码后原子替换，正常退出等待保存；增强模式登记已完成片段和独立音轨，可恢复 WAV 头。最后未收尾片段可能丢失，实际断电/声卡同步/长期稳定待验收。
- 初始化期间停止也收尾为 Stopped；保存失败在退出前给出提示。编码器、帧缓冲与文件流在异常时同样释放。
- 图像历史保留上限与钉图恢复不同；活动钉图持有图像，历史淘汰不会关闭它。录屏原文件与历史副本分别保存。
- 图片超出屏幕时钉图先适合屏幕，再按显示比例缩放；旋转/镜像修改当前钉图导出内容，缩放/不透明度不改原图。
- Ctrl+A 先选鼠标所在屏幕，再选全部桌面。Enter/双击/Ctrl+C 复制图像；编辑按钮进入标注，Ctrl+T 钉图，Ctrl+S 保存。C 复制取色、Alt+C 复制坐标后退出选区；数值输入框拥有正常的编辑快捷键。
- Space 非拖动时只切换工具栏显隐，信息栏仍可设置尺寸；拖动时临时移动。Alt 显示放大镜/辅助线，M 切换放大镜常显；像素和坐标不随 WPF 显示缩放改变。
- 共享 Ui / FloatingTools 画笔需冻结，工具按钮 Style 按线程缓存；菜单组合图标先解除旧逻辑父节点，避免独立 STA 和 WPF 逻辑树异常。
- 截图与录屏浮动栏保持白底 / 蓝色选框和独立线条图标，编辑器样式面板跟随应用主题。录制边框仅画捕获范围外的 2 px，点击穿透、不激活；控制条和边框请求排除捕获，收尾关闭边框并禁用动作。Loaded 时按目标屏 DPI 再定位，控制条保留测量的 DIP 尺寸。
- 编辑器保留图片级 Ctrl+C/Ctrl+S/Ctrl+P；输入框与字体/字号编辑拥有正常文字快捷键。Ctrl+D / Alt 拖动复制对象；Ctrl+Shift+Z 清空标注，沿用可撤销清空规则。
- 已有窗口/子 HWND、透明非矩形、固定/延时预设和可配置区域历史；无 HWND 的细粒度元素、截图刷新、多窗口合成、HDR 和放大镜合成叠层仍待补。
- 第一实例默认只驻留托盘，不显示主窗口；主窗口只创建句柄供热键使用。托盘图标设为隐藏时启动显示主窗口，避免没有入口。第二实例默认唤起已有实例并显示主窗口，--pin-file 通过受限管道发送图像路径；--background 保留后台自启兼容。启动集成只在用户保存相关设置时修改当前账号。
- 锁定的移动缩放、鼠标关闭、按键关闭和拖拽接收为独立限制；销毁及退出绕过锁定。穿透救援工具栏不得在窗口关闭后重建。
- 数字控件的禁用/联动作用于整个编辑器，不能只禁用文本而留下可改值的箭头。快捷键赋值用 SetCurrentValue 保留基础字段的双向绑定；页签选中字重不能继承到整页偏好。鼠标单击与拖动越过阈值后分别执行。
- 隐藏录制工具栏时托盘保留显示/停止入口；隐藏托盘时强制显示录制工具栏。net48 管道读取取消需同时关闭连接，避免残缺请求长期阻塞。
- 设置窗口打开期间必须暂停本应用全局热键，否则已注册组合会被 WM_HOTKEY 抢占而无法录入；快捷键框用 Esc / Backspace 取消、Delete / × 清空。取消截图不再询问，避免二次确认。
- 截图就地标注不改变选区：工具操作、方向键和快捷键只作用于标注画布；Esc 先取消当前标注交互/选择，再退出截图。钉图就地标注写回当前图并保留原图；未提交的标注在取消时丢弃。
- 工具栏分组是拆分按钮：主区域使用当前默认工具，右侧 14px 箭头或右键打开工具列表；列表选择即更新默认并跨启动保存。不要恢复成“点击先弹菜单”。工具子操作栏统一由 AnnotationOptionsBar 按工具类型重建，不要在 InlineAnnotationBar 里为每个工具复制控件。
- 自动识别选区不能在 UI 线程直接调用 UIA：独立 MTA 工作线程 + 最新请求合并，结果回到 UI 线程后才更新 hover；UIA 失败或返回更大区域时保留 Win32 结果。
- 截图窗口启动后必须取得前台和键盘焦点，未完成选区时 Esc 直接关闭；否则快捷键唤起的 Topmost 窗口收不到按键。
- 截图工具栏分组主区域直接进入默认标注工具，不得再“先弹菜单”；序号、箭头和摄像头使用独立按钮。
- 编辑工具切换后点击同类标注必须直接选中，工具栏不再提供“选择 / 移动”按钮；选区/控制点拖动必须在松开时只提交一次。
- 钉图不显示右上角角标；选中边框用淡黄色由外向内渐强的渐变，按 F 可隐藏，不得恢复为单色 Goldenrod 边框。发光可见时关闭 frame 投影，否则图片边缘会出现暗线；外侧圆环只占用 frame.Margin 的空白区。Esc 关闭不得走确认框；左键双击固定复制并关闭。右键菜单必须保留图标与快捷键文本，并随浅/深主题使用 PanelBrush/InkBrush。
- AnnotationToolDefaults 是进程级静态状态；触碰它的 UI 测试必须放进同一个 xUnit Collection（当前 CaptureUiTests 与 AnnotationEditorUiTests），否则并行执行的 Load/Set 会互相清空默认工具。
- 长截图面板 Ctrl+T 必须在底层滚动窗口保持焦点时也可用：用面板句柄 RegisterHotKey，不用窗口焦点快捷键作为唯一入口。
- 长截图必须留在截图会话内：选区左侧/右侧长图面板自动采集固定选区，不弹出独立大窗口；自动采集不得向其它应用发送滚动输入。播放回放浮层只在刚录完且有区域时启用，历史录像保持普通窗口。

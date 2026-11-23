# 截图与标注扩展

- `CaptureTargetReader.Snapshot` 在遮罩出现前按窗口层级采集 Win32 窗口/子 HWND，DWM 去掉不可见边框及 cloaked 窗口；`CaptureTargets.At` 按前台遮挡和子区面积筛选。Elements 模式再由 `UiAutomationTargetReader.ElementAt` 从光标所在顶层窗口的 UIA 子树逐层选择包含光标的元素，与 Win32 结果取面积更小者；后台 MTA 线程合并请求、结果回 UI 线程应用，按 120ms 定时/移动 2px 投递；异常或层级过深时回退。Tab 切换窗口/控件，未框选时滚轮选择父子范围。个别自绘元素仍可能只返回其容器。
- `CaptureProfiles` 在 captures.json 分开保存 20 条去重选区和最多 100 个预设；预设固定尺寸/位置/延时/指针/形状。`CapturePresetWindow` 编辑/复制/删除/交换宽高，`AppController.Capture` 分派直接截图或预填选区。
- `CaptureWindow` 支持矩形、椭圆、自由轮廓和逐点多边形；Enter/双击结束多边形。Loaded 时强制前台/键盘焦点，未完成选区按 Esc 直接取消。截图工具栏与录屏工具栏同窗切换：摄像头或 Screenshot 上下文 Record（默认 G）在截图/录屏间双向切换（含就地标注中与未框选时）：进入录屏时提交并携带标记、隐藏冻结截图/参考线；选区前用全屏 alpha 1 背景板压住悬停预览挖空，选区确定后收起遮罩、选区内穿透、边框抓取带可调、框外可直接操作其它窗口；切回截图恢复冻结画面与遮罩，录屏标记转成截图标注；`RecordingEscapeHook` 在其它应用前台时拦截 Esc 取消；CaptureAction.Record 交给 RecordRegion；形状/画笔等分组主区域直接进入组内第一个标注工具，序号/箭头/摄像头为独立按钮。`CaptureMasks.Apply` 将非矩形外部导出为透明像素，长截图仍只接收矩形。非矩形最大包围范围仍按物理像素处理。
- `AnnotationAppearance` 为不可变填充/线型/透明度/箭头/强度/文本底色及连接/端点、笔迹平滑、换行和序号样式，WithPoints/WithText/WithStyle 都保留样式。`AnnotationCanvas.ChangeAppearance` 接样式编辑并记录快照；折线逐点，Enter/双击结束，画笔 Shift 可选自由直线/45°/水平垂直。
- `AnnotationRenderer` 包含圆角矩形、多个聚光灯范围、局部放大、模糊、画笔马赛克/遮挡、矩形/画笔橡皮擦与水印。橡皮擦在局部恢复原图，保留后来绘制的标注，可撤销；不会生成背景补全。
- 绘制缓存按冻结底图与不可变 Annotation 使用弱引用表，避免命中测试重复处理大图。命中遵守绘制组的 ClipGeometry；空心矩形/椭圆/圆角矩形/聚光灯只命中描边，填充形状命中填充区，不会因点击内部空白误选。
- `EditorWindow.Preferences` 接调色板/工具组顺序、快捷面板、通用内置键位；AnnotationCanvas 消费共享/工具颜色、二次编辑、边界、序号补号/进程连续编号、光标/端点提示，并增加粗体/斜体、字体/字号和方角/圆角切换。活动工具按类别直选同类标注；空心形状只命中描边，选中形状显示四角与四边中点缩放柄，可拖边柄单独改宽高或在 `AnnotationOptionsBar` 输入宽/高（`AnnotationCanvas.ResizeSelected` 按可见外框换算并保持左上角）；箭头/直线显示端点柄；文字在图片位置就地输入。最后工具由 AppController 分截图/钉图保存；工具栏分组使用 `FloatingTools.SplitMenuButton`：主按钮直接激活组默认工具，右侧箭头 / 右键打开列表，列表选择经 `AnnotationToolDefaults` 更新并按组持久化到 `MarkToolDefaults`。子工具颜色暂仅进程/编辑器内记忆。
- 截图的工具按钮、工具快捷键和“更多 → 编辑截图”直接进入 `CaptureWindow.BeginInlineEdit`：在固定选区上叠 `AnnotationCanvas`，`InlineAnnotationBar` 提供“选择”、全部工具、撤销/重做和颜色/线宽/填充/线型/箭头/透明度/效果/粗体/斜体/字体/字号；未指定工具时默认“选择”，开启“记住截图最后工具”则恢复上次工具。复制/保存/钉图/识别时 `BuildInlineOutput` 合成当前画布，不再打开 EditorWindow。选区在编辑期间锁定，Esc 先取消画布交互/选择，再取消整张截图（无二次确认）。长截图在选区左侧/右侧显示长图面板，Ctrl+T 钉图，不再使用横向控制条。
- 钉图选中后按 Space / E 或右键“继续标注”进入 `PinWindow.BeginInlineEdit`：画布按当前显示尺寸缩放覆盖钉图，Popup 工具栏提供同一组工具；Space / Enter 合成当前图并持久化，Esc 丢弃。编辑期间禁用钉图移动/缩放/替换/关闭快捷键。右上角按钮只在选中时显示，Esc 直接关闭无确认，左键双击复制并关闭；`EditorWindow` 仅保留给打开图片与历史。
- `PrivacyDetector` 只检测本地 OCR 的手机号/邮箱，包括多个词片段；触发检测后按偏好直接添加可撤销马赛克或先确认，用户检查并补画。`QrRecognition` 用 ZXing 离线读取 QR，结果确认后复制，不自动访问内容。正常截图输出及编辑器按偏好自动水印，原图留档在标注前完成。
- 像素测试验证透明轮廓、填充/橡皮擦、聚光灯、样式复制、模糊及 QR；多窗口合成、HDR、圆角输出效果、自动头像处理、旋转与复杂几何控制点仍未实现。

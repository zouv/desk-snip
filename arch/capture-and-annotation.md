# 截图与标注扩展

- `CaptureTargetReader.Snapshot` 在遮罩出现前按窗口层级采集 Win32 窗口/子 HWND，DWM 去掉不可见边框及 cloaked 窗口；`CaptureTargets.At` 按前台遮挡和子区面积筛选。Tab 切换窗口/控件，未框选时滚轮选择父子范围。WPF/浏览器无原生 HWND 的细粒度控件仍待补。
- `CaptureProfiles` 在 captures.json 分开保存 20 条去重选区和最多 100 个预设；预设固定尺寸/位置/延时/指针/形状。`CapturePresetWindow` 编辑/复制/删除/交换宽高，`AppController.Capture` 分派直接截图或预填选区。
- `CaptureWindow` 支持矩形、椭圆、自由轮廓和逐点多边形；Enter/双击结束多边形。`CaptureMasks.Apply` 将非矩形外部导出为透明像素，长截图仍只接收矩形。非矩形最大包围范围仍按物理像素处理。
- `AnnotationAppearance` 为不可变填充/线型/透明度/箭头/强度/文本底色及连接/端点、笔迹平滑、换行和序号样式，WithPoints/WithText/WithStyle 都保留样式。`AnnotationCanvas.ChangeAppearance` 接样式编辑并记录快照；折线逐点，Enter/双击结束，画笔 Shift 可选自由直线/45°/水平垂直。
- `AnnotationRenderer` 包含圆角矩形、多个聚光灯范围、局部放大、模糊、画笔马赛克/遮挡、矩形/画笔橡皮擦与水印。橡皮擦在局部恢复原图，保留后来绘制的标注，可撤销；不会生成背景补全。
- 绘制缓存按冻结底图与不可变 Annotation 使用弱引用表，避免命中测试重复处理大图。命中遵守绘制组的 ClipGeometry。
- `EditorWindow.Preferences` 接调色板/工具组顺序、快捷面板、通用内置键位；AnnotationCanvas 消费共享/工具颜色、二次编辑、边界、序号补号/进程连续编号、光标/端点提示。最后工具由 AppController 分截图/钉图保存；子工具和各工具颜色暂仅进程/编辑器内记忆。
- `PrivacyDetector` 只检测本地 OCR 的手机号/邮箱，包括多个词片段；触发检测后按偏好直接添加可撤销马赛克或先确认，用户检查并补画。`QrRecognition` 用 ZXing 离线读取 QR，结果确认后复制，不自动访问内容。正常截图输出及编辑器按偏好自动水印，原图留档在标注前完成。
- 像素测试验证透明轮廓、填充/橡皮擦、聚光灯、样式复制、模糊及 QR；多窗口合成、HDR、圆角输出效果、自动头像处理、几何控制点缩放/旋转仍未实现。

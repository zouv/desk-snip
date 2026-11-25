# 识别与长截图

## 本地 OCR

- `Recognition/LocalOcr.RecognizeAsync` 使用 Windows.Media.Ocr；SDK Contracts 和 WindowsRuntime 包只提供 .NET Framework 投影，识别引擎及语言由 Windows 提供。
- 图像归一化为 BGRA，超过引擎最大边长时缩小，识别词的物理坐标再映射回原图。`RecognizedLine/Word` 保留原结果；`RecognitionText.Format` 独立生成排版，`SelectedWords` 仅高亮当前校对文本中精确对应的词。
- `OcrWindow` 对照图、文本重试、复制/保存。更改排版通过 TextBox 变更组写入，可 Ctrl+Z 恢复校对；失败保留原图与已有结果。`AppController.Recognition` 接截图、钉图、历史和编辑器入口。
- 识别语言未安装时给出系统语言组件说明，不自动下载安装语言包。

## 手动长截图

- `PixelBuffer` 校验尺寸并隔离像素；最多 5000 万像素。`LongScreenshot.FindOverlap` 先粗搜双向重叠，再对前 4 个候选密集采样并裁掉最高 10% 局部误差；用“原位 vs 最佳重叠”识别运动，最小可靠重叠 4–16px，重复/歧义帧拒绝。direction 为 0 自动判向，`StitchMatch.Direction` 返回实际方向。
- `Append` 保留最近 6 个已提交锚点和最多 12 个待匹配帧；后续帧可从任一锚点重新连接并按像素位置提交，不依赖采集顺序。画布向前扩展时同步平移旧锚点，撤销快照深拷贝偏移。合成时旧图重叠区优先，新帧只填补尚未覆盖的位置，避免滚动瞬态黑边污染接缝。`PendingCount` 明确未连接数量，`DiscardPending` 由用户确认放弃；手工重叠、裁剪和单步撤销保留。公开状态修改加锁，后台匹配与 UI 操作不能写坏会话。
- `LongScreenshotBar` 每 90ms 采集固定物理区域，屏幕读取留在 UI 线程，拼接经 `Task.Run` 串行执行；只有新提交才重建预览。首次运动时 `LongScreenshotViewport.Detect` 按行/列差异识别局部滚动视口，后续只裁剪该区域匹配；`Compose` 将固定边缘保留一次并接回完整输出，避免固定标题栏抬高整帧误差。局部区域至少占选区 1/4，整帧运动和小范围动态像素不进入该模式。
- 面板使用应用浮动栏图标与圆角样式，完整长图按 `Uniform` 直接显示，无滚动条；可暂停/继续自动采集。窗口 `SizeToContent.Height` 随状态文字和工具栏换行增高，`SizeChanged` 后按实际尺寸重新夹到工作区，底部按钮不被裁切；“返回普通截图”使用 Back 左箭头。有待匹配帧时阻止输出，用户可继续恢复或显式丢弃。
- 截图内按 L 经 `CaptureWindow.LongScreenshotTransitionRequested` 进入；原模态窗口只用原生 `ShowWindow` 隐藏/恢复，不能 WPF `Hide()/Show()`，否则模态状态被破坏，后续 `DialogResult` 会崩溃。独立“返回普通截图”恢复原实例及焦点，选区、冻结图、标注和撤销历史不变；X/Esc 通过 `CancelLongScreenshotTransition` 结束整个截图，完成输出才 `FinishLongScreenshotTransition`。独立长截图入口仍恢复主窗口/编辑器。
- 自动采集失败不修改拼接结果；用户回到成功位置继续、改手工重叠或撤回。输出走复制/保存/钉图/OCR/联网识别/编辑长图，并自动留档 LongScreenshot。旧的独立长截图窗口已移除。

## 验证与未完成边界

- 合成内容验证纵横像素、1px 小滚动、大滚动桥接恢复、反向 prepend 锚点平移、局部动态内容、周期歧义、手工恢复、裁剪和撤回；固定上部局部滚动验证视口检测、空白边界位移校正及固定区域只合成一次。真实滚轮冒烟覆盖常规往返及 50ms 连续输入，自建固定标题窗口验证实际面板采集与输出像素。合成清晰文字验证 Windows OCR 或缺语言反馈。
- 自动滚动、超长分块、竖排识别、系统语言包管理和原图拖动选词仍待补。真实桌面滚动、混合 DPI 与自动化输入限制见 HANDOFF。

联网识别、翻译、表格/公式/文档编辑导出见 [recognition-service.md](recognition-service.md)，协议见 [服务协议](../docs/04-联网服务协议.md)。

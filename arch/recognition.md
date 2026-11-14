# 识别与长截图

## 本地 OCR

- `Recognition/LocalOcr.RecognizeAsync` 使用 Windows.Media.Ocr；SDK Contracts 和 WindowsRuntime 包只提供 .NET Framework 投影，识别引擎及语言由 Windows 提供。
- 图像归一化为 BGRA，超过引擎最大边长时缩小，识别词的物理坐标再映射回原图。`RecognizedLine/Word` 保留原结果；`RecognitionText.Format` 独立生成排版，`SelectedWords` 仅高亮当前校对文本中精确对应的词。
- `OcrWindow` 对照图、文本重试、复制/保存。更改排版通过 TextBox 变更组写入，可 Ctrl+Z 恢复校对；失败保留原图与已有结果。`AppController.Recognition` 接截图、钉图、历史和编辑器入口。
- 识别语言未安装时给出系统语言组件说明，不自动下载安装语言包。

## 手动长截图

- `PixelBuffer` 校验尺寸并隔离像素；最多 5000 万像素。`LongScreenshot.FindOverlap` 采样搜索重叠，比较最佳与备选误差，重复/低信息/不可靠帧拒绝。
- `Append` 先匹配，再分配和提交；支持正/反向重叠、手工重叠、当前帧位置和撤回最后一步。`TrimBeforeCurrent/TrimAfterCurrent` 以当前帧为界裁剪。
- `LongScreenshotWindow.Collect` 隐藏预览后采集指定物理区域，再恢复预览；定时器只采集，不向其它应用发送滚动输入。`AppController` 隐藏自身控制台和原编辑器。
- 失败不修改拼接结果；用户回到成功位置继续、改手工重叠或撤回。输出使用共用编辑、OCR、钉图、复制和 PNG 保存。

## 验证与未完成边界

- 合成内容验证纵横拼接像素、反向定位、失败保留、手工恢复、裁剪和撤回；合成清晰文字验证 Windows OCR 或缺语言反馈。
- 自动滚动、超长分块、竖排识别、系统语言包管理和原图拖动选词仍待补。真实桌面滚动、混合 DPI 与自动化输入限制见 HANDOFF。

联网识别、翻译、表格/公式/文档编辑导出见 [recognition-service.md](recognition-service.md)，协议见 [服务协议](../docs/04-联网服务协议.md)。

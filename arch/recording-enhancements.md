# 录屏增强链路

- `RecordingSetup` 选择两路 WASAPI 设备、帧率/倒计时/画质和 FFmpeg 路径。设备检测无结果时阻止选中的声音录制。`RecordingOptions` 非空启用增强模式，缺 FFmpeg 时仍可基础 MP4/GIF。
- `RecordingToolbarDisplayMode` 的 Ask/Always/Hidden 控制设置窗口的工具栏选择；隐藏托盘时强制显示工具栏。托盘提供显示/停止入口，录制中改为隐藏托盘也显示工具栏。
- `AppController.DefaultRecordingPath(format, region)` 的 MP4 快速规则用独立目录/模板/覆盖及实际区域尺寸；其它格式沿用录制输出选择。AfterRecordingSaved 统一停止/另存/转换/剪辑/恢复成功后的文件或路径复制与音效；PlaybackWindow/RecordingRecoveryWindow 通过 outputSaved 回调接线。RecordingCloseAfterExport 控制返回主窗/自动成片及回放导出后关闭。
- `RecordingSession.RunEnhanced` 用 MF 编码，每约 2 秒收尾成独立片段；`RecordingJournal` 在专用 `recording-drafts/<id>` 目录原子登记已完成片段。失败保留素材，最后尚未收尾的片段可能丢失。
- `AudioCapture` 分别保存系统声和麦克风 WAV。`AudioTimeline.Place` 以排除暂停的录制时间定位音频包，补静音并丢弃重复采样。启动后设备参数固定；音频设备中断保留片段。实际设备同步和长时稳定性仍需验收。
- 停止后 `Ffmpeg.Join` 合并片段，`Export` 裁剪/倍速/独立音量/混音/编码。参数通过 `ProcessArguments.Quote` 和 CreateProcess 传递，不经过 shell；目标只在完整输出后原子替换。取消向自有 FFmpeg 发送 q 并等待正常退出，不强杀。
- `TestEncoder` 实际编码一帧探测 NVIDIA/Intel/AMD，失败回退 libx264；导出时硬件失败也回退。捕获片段仍用 Windows MF，硬件选择作用于最终导出。
- `PlaybackWindow` 时间轴定位、首尾秒数、裁剪、0.2–2x、两路音量、质量及 MP4/GIF/WebP 输出。独立音轨存在时预览不重复播放成片的混音；导出可调至 200%，系统预览音量上限 100%。
- 刚停止的录制由 `AppController.OpenHistory(entry, recording.Region)` 传入录制区域，`PlaybackWindow` 以 `inlineRegion` 模式显示为无标题栏、置顶、覆盖录制区域的浮层，保留时间轴、裁剪、音量和导出并增加关闭按钮/Esc；历史录像没有区域时仍打开普通窗口。
- 回放 WebP 预览/探测有生命周期取消；回放和恢复实现 `IBackgroundWindow`，关闭及总退出取消并等待加载/导出/恢复，完成前不关进程，也不打开新的回放窗。
- `RecordingRecoveryWindow` 重导已登记片段，`WaveRecovery.Repair` 修复 WAV 长度后恢复音轨；删除素材仅在产品确认后经管理路径删除已知文件。历史 `RecordingId` 关联素材，素材删掉后仍可播放成片。
- FFmpeg 是可配置外部程序，默认查 tools/ 或 PATH，发行包不自动捆绑其本机安装。NAudio.Wasapi 用于设备和 PCM 采集，Windows SDK Contracts 用于 OCR。
- 验证：合成双音轨、剪辑/裁剪/倍速、视频解码、GIF/WebP 签名、取消保留目标、增强录制暂停/素材、硬件探测、负时间音频前缀裁剪和 WAV 头恢复；捕获中断后已收尾片段可重导。键鼠事件、摄像头、可拖动剪辑手柄仍留在对照表。

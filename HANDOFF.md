# 会话接力 · DeskSnip

## 状态快照

- 最近更新：2026-11-13。
- 整体状态：0.1.0 首版已归档；无进行中任务，M1 真实桌面交互待验收。
- 工作分支：main。
- 最后验证：50 项测试与 manager.sh verify 通过（编译 / 文档 0 警告），含原生编解码、暂停/初始化停止、像素、数据与路径；免安装包 zip 内容和 SHA256 验证通过。真实进程可启动、响应、正常退出。
- 交付：dist/release/DeskSnip_0.1.0_win-x64-unpack.zip；直接运行 dist/release/unpack/DeskSnip.exe。
- 最终 Release 的启动/响应、第二实例唤起、正常退出验证通过；使用默认 LocalAppData 数据目录，进程状态由 manager.sh status 查看。

## 当前任务

- 无。

## 进行中

- 无。

## 下一步

- 按用户试用反馈验证 M1：AppController.Select / Capture / Record、CaptureWindow、EditorWindow、PinWindow.SetInteraction 与 RecordingSession.Run。

## 待验收

- M1 操作试用：截图微调与输出、钉图穿透解除、MP4/GIF 暂停保存。默认三热键 Ctrl+Alt+A/P/R；使用说明见 docs/02-使用说明.md。

## 阻塞

- computer-use 已读取主窗口控件；多次捕获出现 window capture / FrameArrived timeout，输入出现 failed to activate captured window / coordinate input geometry is unavailable。已重新绑定并重置会话，仍失败，不能据此声称交互通过。

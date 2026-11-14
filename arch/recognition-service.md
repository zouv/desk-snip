# 可配置联网识别

## 请求与凭据

- `ServicePreferences` 为旧配置初始化服务默认值，校验 HTTPS/本机 HTTP、环境变量名、超时和语言。`PreferenceTransfer` 不导出服务地址/环境变量名/密钥。
- `ServiceCredentialStore` 为当前 Windows 账号 DPAPI 加密，环境变量优先。`Change` 保存原加密字节，设置保存失败原子回滚；只在“保存设置”调用，导入/导出/读取偏好不会写密钥。不同账号恢复数据后可重新填写密钥。
- `Recognition/ServiceClient` 只在请求按钮调用，POST JSON、禁止自动重定向/Cookie、Bearer 密钥、独立超时和用户取消，逐段限响应为 40 MiB。HTTP/连接失败不会显示包含密钥的请求信息。
- `RecognitionRequest/Response.Validate` 校验 schema/operation、内容大小、矩形表格/不重叠合并和安全配图名称/MIME/尺寸；App 再实际解码配图。协议契约见 docs/04-联网服务协议.md。

## 窗口和编辑

- `AppController.Recognition` 接截图、图像编辑器、钉图、长截图、历史、剪贴板和本地恢复。进入窗口不会联网；语言/发送原图由用户确认，服务请求时禁用编辑，避免结果覆盖进行中的修改。
- `RecognitionServiceWindow` 保留原图和请求文字，一套窗口选择五种操作；请求失败/取消保留旧界面。应用结果前解绑可复用控件，避免多次请求或恢复触发 WPF 父控件冲突。
- `TableDocument` 深复制快照，100 步撤销/重做；插删行列解除合并，合并隐藏底层单元格内容，拆分后恢复。编辑隐藏格修改合并区域左上格；数值/文本稳定整行排序，跨行合并先拆分。
- DataGrid 编辑结束后提交模型，导出前确认单元格/行编辑，选区要求连续矩形。合并记录并不在 DataGrid 跨格渲染，当前只显示左上格和提示；XLSX 输出合并范围。
- `FormulaImages` 使用 WpfMath.Parse + 本地位图，剥离常用定界符；限制源码/嵌套/图像尺寸，预览失败保留源码。多条公式可编辑和导出，PNG 为当前项。
- `MarkdownContent` 为基础段落/标题/列表/代码/管道表格/块公式/登记配图解析器；不运行 HTML、不抓取远程资源。WPF 预览公式用位图；Word 中公式保留源码。

## 输出与恢复

- `TableExport` 生成 UTF-8 CSV/TSV 或 XLSX inlineStr，固定边框/左对齐/首行加粗。文本疑似公式时 CSV/TSV 加单引号，XLSX 不生成公式节点；不静默截断超长单元格。
- `DocumentExport` 用 ZipArchive 和 XML 生成 Word 段落/标题/表格/配图，识别配图保留比例。`RecognitionExport` 写 UTF-8 源码和带 images/ 的 Markdown ZIP；均通过 AtomicFile 发布。
- `StoredRecognitionSession` 将原图 Base64、请求文字和编辑结果原子写入 recognition/last-session.json；失败不替换最近结果，恢复不联网。编辑延迟 700 ms 保存，关闭前再提交；结果与密钥分开。
- `IBackgroundWindow.CloseSafelyAsync` 请求取消后等 HTTP 任务返回，再持久化/关闭；总退出等待所有回放/恢复/识别窗口，避免进程结束时留下媒体子进程或未完成请求。

## 验证与边界

- 本机 TCP 模拟服务验证五种操作、PNG/语言/Bearer 请求及响应；注入失败、重定向、非 JSON/错误操作、超限、取消和超时。测试使用合成数据。
- 表格快照/合并/排序/撤销、配图包和原子失败有测试；DocumentFormat.OpenXml 只在测试独立校验 XLSX/Word，应用不依赖 Office 安装。
- 最小窗口合成渲染、多次结果切换、凭据保存回滚、公式真实像素通过。实际服务商质量、原译图覆盖、同步缩放、更多公式格式/PDF/整篇 LaTeX 和完整 Markdown 尚未完成。

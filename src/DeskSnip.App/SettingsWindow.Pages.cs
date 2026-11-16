using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeskSnip.Core;

namespace DeskSnip.App;

internal sealed partial class SettingsWindow
{
    private readonly TextBlock namePreview = SettingsLayout.Text("", 12, "SubtleBrush");
    private readonly Border accentSwatch = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 7, 0) };
    private void BuildPages()
    {
        BuildSystemPage(); BuildAccountPage(); BuildAppearancePage(); BuildScreenshotPage(); BuildPinPage(); BuildSavePage(); BuildAnnotationPage(); BuildShortcutPage(); BuildBuiltinPage(); BuildMousePage(); BuildGlobalMousePage(); BuildTransferPage(); BuildRecordingPage();
    }
    private void BuildSystemPage()
    {
        var page = Page("system", L.Get("系统"), ToolGlyph.Styles);
        var system = SettingsLayout.Section(page.Panel, L.Get("系统"));
        Choice(system, nameof(AppSettings.InterfaceLanguage), L.Get("界面语言"), new[] { "System", "zh-CN", "en-US" }, new[] { L.Get("跟随系统"), L.Get("简体中文"), "English" }, L.Get("保存后更新界面，不改变识别内容或服务语言"));
        Unavailable(system, L.Get("忽略复制按键宏"), L.Get("按键宏检测暂未开放"), L.Get("不可用"));
        Bind(system, nameof(AppSettings.StartWithWindows), L.Get("开机后自动启动"));
        Choice(system, nameof(AppSettings.ExplorerContextMenu), L.Get("右键菜单模式"), new[] { "Disabled", "Images" }, new[] { L.Get("已禁用"), L.Get("仅图像文件") }, L.Get("保存后添加当前账号的 DeskSnip 钉图入口"));
        Unavailable(system, L.Get("桌面工具栏显示"), L.Get("桌面悬浮球与工具栏暂未开放"), L.Get("已禁用"));
        Unavailable(system, L.Get("以管理员身份运行"), L.Get("当前版本以普通权限运行"), L.Get("普通权限"));
        UnavailableButton(system, L.Get("以管理员身份重新启动"), L.Get("管理员启动暂未开放"));
        Bind(system, nameof(AppSettings.MinimizeToTray), L.Get("关闭主窗口时收起到托盘"));
        var clipboard = SettingsLayout.Section(page.Panel, L.Get("剪贴板"));
        var copyAsFile = new ComboBox { ItemsSource = new[] { new ChoiceItem("Disabled", L.Get("禁用")), new ChoiceItem("PNG", L.Get("复制为 PNG 文件")), new ChoiceItem("JPEG", L.Get("复制为 JPEG 文件")), new ChoiceItem("BMP", L.Get("复制为 BMP 文件")), new ChoiceItem("Auto", L.Get("自动（PNG）")) }, DisplayMemberPath = "Label", SelectedValuePath = "Value" }; SettingsLayout.Input(copyAsFile, "ClipboardCopyAsFile", L.Get("复制图像为文件"), 250); SettingsLayout.Row(clipboard, L.Get("复制图像为文件"), copyAsFile, L.Get("WEBP / AVIF 编码尚未开放")); clipboardFormatChoice = copyAsFile;
        buildingPage.Preferences.Add(nameof(AppSettings.CopyFileWithImage)); buildingPage.Preferences.Add(nameof(AppSettings.ClipboardImageFileFormat));
        copyAsFile.SelectionChanged += (_, _) => { if (loading || !(copyAsFile.SelectedValue is string format)) return; if (format != "Disabled") clipboardFormatDraft = format; boolPreferences[nameof(AppSettings.CopyFileWithImage)].IsChecked = format != "Disabled"; };
        var recognition = SettingsLayout.Section(page.Panel, L.Get("文字识别"));
        Choice(recognition, nameof(AppSettings.OcrTextLayout), L.Get("文本布局"), Enum.GetNames(typeof(TextLineLayout)), new[] { L.Get("多行排版"), L.Get("移除换行符"), L.Get("自动分段") });
        Bind(recognition, nameof(AppSettings.OcrDirectCopy), L.Get("识别完成后直接复制（关闭时显示校对窗口）"));
        Bind(recognition, nameof(AppSettings.OcrCloseAfterCopyAll), L.Get("复制全部文字后关闭校对窗口"));
        var dialogs = new WrapPanel(); foreach (var item in new[] { (nameof(AppSettings.OcrDialogOnScreenshot), L.Get("截图")), (nameof(AppSettings.OcrDialogOnCopy), L.Get("复制")), (nameof(AppSettings.OcrDialogOnPin), L.Get("钉图")) }) { var checkbox = new CheckBox { Content = item.Item2, Margin = new Thickness(0, 4, 14, 4) }; RegisterBoolean(checkbox, item.Item1); dialogs.Children.Add(checkbox); } SettingsLayout.Row(recognition, L.Get("显示识别确认窗口的时机"), dialogs, L.Get("直接复制总开关开启时跳过确认"));
        Choice(recognition, nameof(AppSettings.OcrPunctuation), L.Get("标点处理"), Enum.GetNames(typeof(PunctuationStyle)), new[] { L.Get("无动作"), L.Get("转换半角标点"), L.Get("转换全角标点") });
        Bind(recognition, nameof(AppSettings.LocalOcrLanguage), L.Get("识别语言"), L.Get("留空跟随 Windows；例如 zh-Hans-CN / en-US"));
        Bind(recognition, nameof(AppSettings.OcrRemoveSpaces), L.Get("去除空格"));
        var translation = SettingsLayout.Section(page.Panel, L.Get("翻译"));
        Bind(translation, nameof(AppSettings.RecognitionSourceLanguage), L.Get("源语言"), L.Get("auto 为自动识别，或使用服务支持的语言代码"));
        Bind(translation, nameof(AppSettings.RecognitionTargetLanguage), L.Get("目标语言"), L.Get("例如 zh / en / ja"));
        UnavailableChoice(translation, L.Get("翻译服务"), new[] { L.Get("自定义协议服务"), L.Get("有道（尚未接入）"), L.Get("百度（尚未接入）") }, L.Get("自定义协议服务"), L.Get("当前使用已确认的可配置服务接口"));
        Unavailable(translation, L.Get("服务商 API ID"), L.Get("内置服务商适配暂未开放；协议服务使用地址与访问密钥"), L.Get("由自定义服务处理"));
        SettingsLayout.FullRow(translation, SettingsLayout.Button(L.Get("配置服务地址 / 密钥"), () => SelectPage("service")));
        SettingsLayout.FullRow(translation, SettingsLayout.Text(L.Get("服务协议和配置示例见随包使用说明中的联网服务章节。配置后通过联网识别窗口明确请求服务。"), 11, "SubtleBrush"));
        var tray = SettingsLayout.Section(page.Panel, L.Get("任务栏托盘"));
        var trayValues = new[] { "None", "Capture", "Pin", "Settings", "ToggleHotkeys", "TogglePins", "Show" };
        var trayLabels = new[] { L.Get("无动作"), L.Get("截图"), L.Get("钉图"), L.Get("打开设置"), L.Get("禁用 / 恢复快捷键"), L.Get("隐藏 / 显示所有钉图"), L.Get("打开控制台") };
        Choice(tray, nameof(AppSettings.TraySingleClickAction), L.Get("单击动作"), trayValues, trayLabels);
        Choice(tray, nameof(AppSettings.TrayDoubleClickAction), L.Get("双击动作"), trayValues, trayLabels);
        Choice(tray, nameof(AppSettings.TrayMiddleClickAction), L.Get("中键动作"), trayValues, trayLabels);
        var update = SettingsLayout.Section(page.Panel, L.Get("更新"));
        Unavailable(update, L.Get("升级配置"), L.Get("自动更新暂未开放，请使用新版本免安装包"), L.Get("手动更新"));
        Unavailable(update, L.Get("版本通道"), L.Get("发布通道暂未开放"), L.Get("当前免安装版本"));
        UnavailableButton(update, L.Get("检查更新"), L.Get("自动检查更新暂未开放"));
        var proxy = SettingsLayout.Section(page.Panel, L.Get("网络代理"));
        var proxyMode = Choice(proxy, nameof(AppSettings.NetworkProxyMode), L.Get("代理类型"), new[] { "System", "None", "Http" }, new[] { L.Get("跟随系统代理"), L.Get("无代理"), "HTTP" });
        var host = Bind(proxy, nameof(AppSettings.NetworkProxyHost), L.Get("主机"));
        var port = Bind(proxy, nameof(AppSettings.NetworkProxyPort), L.Get("端口"), "1–65535");
        proxyMode.SelectionChanged += (_, _) => host.IsEnabled = port.IsEnabled = (string?)proxyMode.SelectedValue == "Http";
        SettingsLayout.FullRow(proxy, SettingsLayout.Button(L.Get("验证代理连接"), VerifyProxy));
        Unavailable(proxy, "Socks5", L.Get("Socks5 代理暂未开放"), L.Get("不可用"));
        var analytics = SettingsLayout.Section(page.Panel, L.Get("分享分析数据"));
        Unavailable(analytics, L.Get("自动发送使用统计"), L.Get("DeskSnip 当前不收集或发送使用统计"), L.Get("关闭"));
    }
    private void BuildAppearancePage()
    {
        var page = Page("appearance", L.Get("外观"), ToolGlyph.Palette); var appearance = SettingsLayout.Section(page.Panel, L.Get("主题与字体"));
        Choice(appearance, nameof(AppSettings.Theme), L.Get("主题模式"), new[] { "Light", "Dark", "System" }, new[] { L.Get("浅色"), L.Get("深色"), L.Get("跟随系统") });
        var font = Choice(appearance, nameof(AppSettings.InterfaceFont), L.Get("界面字体"), Fonts.SystemFontFamilies.Select(family => family.Source).Distinct().OrderBy(value => value).ToArray()); font.IsEditable = true; font.IsTextSearchEnabled = true; font.LostKeyboardFocus += (_, _) => MarkChanged(nameof(AppSettings.InterfaceFont));
        ColorField(appearance, nameof(AppSettings.AccentColor), L.Get("主题颜色")); ColorField(appearance, nameof(AppSettings.LockedPinColor), L.Get("锁定窗口颜色")); ColorField(appearance, nameof(AppSettings.CaptureMaskColor), L.Get("截图时未选区域的颜色"), L.Get("支持 #AARRGGBB 透明度")); ColorField(appearance, nameof(AppSettings.ClickThroughColor), L.Get("鼠标穿透时阴影颜色"), L.Get("支持 #AARRGGBB 透明度"));
        Choice(appearance, nameof(AppSettings.TrayIconMode), L.Get("托盘图标"), new[] { "Auto", "Color", "Custom", "Hidden" }, new[] { L.Get("自动"), L.Get("彩色 DeskSnip 图标"), L.Get("自定义颜色"), L.Get("隐藏") }); ColorField(appearance, nameof(AppSettings.TrayIconColor), L.Get("托盘图标颜色")); Choice(appearance, nameof(AppSettings.ToolbarIconSize), L.Get("工具栏图标大小"), new[] { "Small", "Medium", "Large" }, new[] { L.Get("小"), L.Get("中"), L.Get("大") });
        Unavailable(appearance, L.Get("颜色选择框样式"), L.Get("现代颜色窗口暂未开放"), L.Get("经典系统窗口")); Unavailable(appearance, L.Get("保存对话框样式"), L.Get("带压缩预览的现代保存窗口暂未开放"), L.Get("经典系统窗口"));
        var selection = SettingsLayout.Section(page.Panel, L.Get("截图选框")); Bind(selection, nameof(AppSettings.SelectionBorderWidth), L.Get("选区边框宽度"), L.Get("0–20 像素；0 隐藏边框")); OptionalColorField(selection, nameof(AppSettings.SelectionBorderFollowsTheme), nameof(AppSettings.SelectionBorderColor), L.Get("选区边框颜色"), L.Get("跟随主题")); Choice(selection, nameof(AppSettings.SelectionAnchorDisplay), L.Get("锚点显示"), new[] { "Eight", "Four", "None" }, new[] { L.Get("显示八个方向"), L.Get("显示四个方向"), L.Get("禁用") }); Unavailable(selection, L.Get("显示选框切换动画"), L.Get("平滑切换动画暂未开放"), L.Get("直接切换"));
    }
    private void BuildScreenshotPage()
    {
        var page = Page("screenshot", L.Get("截图"), ToolGlyph.Rectangle);
        var capture = SettingsLayout.Section(page.Panel, L.Get("截图")); Choice(capture, nameof(AppSettings.ScreenshotUiDetection), L.Get("自动识别选区"), new[] { "Elements", "Windows", "None" }, new[] { L.Get("检测元素"), L.Get("仅窗口"), L.Get("不检测") }); SettingsLayout.FullRow(capture, SettingsLayout.Text(L.Get("鼠标移动时自动框住光标下的窗口或 UI 元素，点击选中；Tab 切换元素 / 窗口。"), 11, "SubtleBrush")); Bind(capture, nameof(AppSettings.ScreenshotDetectionPadding), L.Get("元素检测边距"), L.Get("−50–999 像素")); Bind(capture, nameof(AppSettings.ScreenshotExpandOutside), L.Get("点击选区外时自动扩展选区")); Bind(capture, nameof(AppSettings.ScreenshotCursorVisible), L.Get("默认显示截图光标（` 临时切换）")); Choice(capture, nameof(AppSettings.ScreenshotGuideVisibility), L.Get("显示辅助线"), new[] { "Alt", "Always", "Never" }, new[] { L.Get("按 Alt 键显示"), L.Get("始终显示"), L.Get("不显示") });
        var guides = SettingsLayout.Section(page.Panel, L.Get("辅助线")); Bind(guides, nameof(AppSettings.GuideMouseLines), L.Get("鼠标指示线")); Bind(guides, nameof(AppSettings.GuideSelectionCenter), L.Get("选区中心线")); Bind(guides, nameof(AppSettings.GuideSelectionBorder), L.Get("选区边框线")); Bind(guides, nameof(AppSettings.GuideMagnifier), L.Get("辅助线显示放大镜")); Bind(guides, nameof(AppSettings.GuideRuleOfThirds), L.Get("三分法网格")); Bind(guides, nameof(AppSettings.ScreenshotShowInfoBar), L.Get("显示信息栏"));
        var magnifier = SettingsLayout.Section(page.Panel, L.Get("放大镜")); Bind(magnifier, nameof(AppSettings.ScreenshotShowMagnifier), L.Get("显示放大镜")); Bind(magnifier, nameof(AppSettings.MagnifierGrid), L.Get("放大镜内容：网格线")); Bind(magnifier, nameof(AppSettings.MagnifierCrosshair), L.Get("放大镜内容：十字线")); Bind(magnifier, nameof(AppSettings.MagnifierCenter), L.Get("放大镜内容：中心框线")); Bind(magnifier, nameof(AppSettings.MagnifierCoordinates), L.Get("放大镜内容：鼠标坐标")); foreach (var overlay in new[] { L.Get("截图边框"), L.Get("控制点"), L.Get("标注控制线") }) Unavailable(magnifier, L.Join(L.Get("放大镜内容："),overlay), L.Get("放大镜中的合成叠层暂未开放"), L.Get("不可用")); Bind(magnifier, nameof(AppSettings.MagnifierZoom), L.Get("放大镜倍数"), "1–100"); Bind(magnifier, nameof(AppSettings.MagnifierShowShortcuts), L.Get("放大镜显示快捷键提示")); ColorField(magnifier, nameof(AppSettings.MagnifierGridColor), L.Get("放大镜网格线颜色")); Bind(magnifier, nameof(AppSettings.ColorFormatOrder), L.Get("启用颜色格式与顺序"), L.Get("用逗号排列：HashHex,Rgb,Bgr,Hex,Hsv,Hsl")); Bind(magnifier, nameof(AppSettings.MagnifierSmooth), L.Get("平滑缩放放大镜内容"));
        var behavior = SettingsLayout.Section(page.Panel, L.Get("提示、历史与鼠标")); Bind(behavior, nameof(AppSettings.ScreenshotShowShortcuts), L.Get("显示快捷键提示（F1 临时切换）")); Unavailable(behavior, L.Get("二维码检测模式"), L.Get("自动检测暂未开放；编辑器可手动识别二维码"), L.Get("手动启用")); Bind(behavior, nameof(AppSettings.HistoryLimit), L.Get("保留的历史截图数量"), L.Get("0–99999；0 不保留")); Bind(behavior, nameof(AppSettings.ScreenshotAreaHistoryLimit), L.Get("保留的历史截图区域数量"), L.Get("0–99999；0 停止记录和读取选区")); Bind(behavior, nameof(AppSettings.PinAlsoCopy), L.Get("钉图时同时复制图片")); Unavailable(behavior, L.Get("复制截图后自动粘贴到输入焦点"), L.Get("向其他应用自动粘贴暂未开放"), L.Get("关闭"));
        var actions = new[] { "None", "Copy", "Ocr", "QuickSave", "Save", "Pin", "Close" }; var labels = new[] { L.Get("无动作"), L.Get("复制"), L.Get("检测文本并复制"), L.Get("快速保存"), L.Get("保存"), L.Get("钉图"), L.Get("关闭截图") }; Choice(behavior, nameof(AppSettings.ScreenshotDoubleClickAction), L.Get("左键双击动作"), actions, labels); Choice(behavior, nameof(AppSettings.ScreenshotMiddleClickAction), L.Get("中键单击动作"), actions, labels); Bind(behavior, nameof(AppSettings.ScreenshotWheelTargets), L.Get("滚轮切换上层 / 下层元素")); Bind(behavior, nameof(AppSettings.ScreenshotWheelResize), L.Get("滚轮调整选区大小")); Choice(behavior, nameof(AppSettings.ScreenshotCopyDpiLimit), L.Get("HDPI 下复制图像缩小到"), new[] { "0", "100", "150", "200", "250", "300" }, new[] { L.Get("不缩小"), "100%", "150%", "200%", "250%", "300%" }); Bind(behavior, nameof(AppSettings.ScreenshotSecondaryButtons), L.Get("显示次要按钮区域")); Unavailable(behavior, L.Get("功能按钮调整"), L.Get("拖动排序及逐按钮隐藏暂未开放"), L.Get("默认分组")); Unavailable(behavior, L.Get("截图模式"), L.Get("性能采集与 HDR 转换暂未开放"), L.Get("兼容 GDI 模式"));
        var sounds = SettingsLayout.Section(page.Panel, L.Get("音效播放时机")); Bind(sounds, nameof(AppSettings.SoundCaptureStart), L.Get("开始截图")); Bind(sounds, nameof(AppSettings.SoundCapturePin), L.Get("截图时钉图")); Bind(sounds, nameof(AppSettings.SoundCaptureCopy), L.Get("复制截图")); Bind(sounds, nameof(AppSettings.SoundSaveSuccess), L.Get("保存成功")); Bind(sounds, nameof(AppSettings.SoundQuickSave), L.Get("快速保存"));
        var polygon = SettingsLayout.Section(page.Panel, L.Get("折线截图")); Unavailable(polygon, L.Get("折线圆角"), L.Get("多边形选区圆角暂未开放"), L.Get("0（尖角）"));
        var longCapture = SettingsLayout.Section(page.Panel, L.Get("长截图")); Choice(longCapture, nameof(AppSettings.LongScreenshotStart), L.Get("进入长截图后"), new[] { "Vertical", "Horizontal", "Manual", "Buttons" }, new[] { L.Get("垂直采集"), L.Get("水平采集"), L.Get("不自动开始采集"), L.Get("显示开始 / 停止按钮") }); Bind(longCapture, nameof(AppSettings.LongScreenshotShortcuts), L.Get("响应快捷键"));
        var video = SettingsLayout.Section(page.Panel, L.Get("动态截图")); Bind(video, nameof(AppSettings.RecordingCloseAfterExport), L.Get("保存 / 导出后关闭回放编辑器")); Unavailable(video, L.Get("不再提醒音频录制警告"), L.Get("录制开始后声音参数已固定"), L.Get("录制前调整")); SettingsLayout.FullRow(video, SettingsLayout.Button(L.Get("配置硬件加速 / 编码器"), () => SelectPage("recording"))); Unavailable(video, L.Get("平滑鼠标轨迹"), L.Get("键鼠轨迹录制暂未开放"), L.Get("不可用")); Choice(video, nameof(AppSettings.RecordingToolbarDisplayMode), L.Get("录制时工具栏显示模式"), new[] { "Ask", "Always", "Hidden" }, new[] { L.Get("显示（默认）"), L.Get("始终显示"), L.Get("隐藏（从托盘停止）") }); Bind(video, nameof(AppSettings.RecordingExcludeToolbar), L.Get("录制画面中隐藏工具栏（屏幕仍可操作）"));
    }
    private void BuildPinPage()
    {
        var page = Page("pin", L.Get("钉图"), ToolGlyph.Pin); var clipboard = SettingsLayout.Section(page.Panel, L.Get("钉图")); Bind(clipboard, nameof(AppSettings.PinImageEnabled), L.Get("支持图像钉图")); foreach (var type in new[] { L.Get("文本"), L.Get("文件"), L.Get("颜色"), L.Get("LaTeX 公式"), L.Get("动态"), L.Get("窗口") }) Unavailable(clipboard, L.Format("支持{0}钉图", type), L.Get("此钉图类型暂未开放"), L.Get("不可用")); Bind(clipboard, nameof(AppSettings.PinRestoreOnStart), L.Get("启动时恢复未关闭的钉图")); Unavailable(clipboard, L.Get("默认文本可选择"), L.Get("图内文本选择暂未开放"), L.Get("不可用")); Choice(clipboard, nameof(AppSettings.PinDisplayPosition), L.Get("钉图显示位置"), new[] { "Screenshot", "Mouse", "PrimaryCenter", "MouseScreenCenter" }, new[] { L.Get("截图位置"), L.Get("鼠标位置"), L.Get("主屏幕中心"), L.Get("鼠标所在屏幕中心") }); Choice(clipboard, nameof(AppSettings.PinRestoreOrder), L.Get("恢复钉图的顺序"), new[] { "Closed", "Created" }, new[] { L.Get("按关闭时间"), L.Get("按创建时间") }); Bind(clipboard, nameof(AppSettings.PinHistoryLimit), L.Get("保留的历史钉图数量"), L.Get("0–1000；活动图独立保留")); MirrorBoolean(clipboard, nameof(AppSettings.PinAlsoCopy), L.Get("钉图时同时复制图片"));
        var lockOptions = SettingsLayout.Section(page.Panel, L.Get("锁定与二次确认")); Bind(lockOptions, nameof(AppSettings.PinLockMovement), L.Get("锁定时禁止移动和缩放")); Bind(lockOptions, nameof(AppSettings.PinLockMouseClose), L.Get("锁定时禁止鼠标关闭")); Bind(lockOptions, nameof(AppSettings.PinLockKeyClose), L.Get("锁定时禁止按键关闭")); Bind(lockOptions, nameof(AppSettings.PinLockDrop), L.Get("锁定时禁止接受拖拽")); Bind(lockOptions, nameof(AppSettings.PinShowLockHint), L.Get("显示锁定提示")); Bind(lockOptions, nameof(AppSettings.PinShowMenuShortcuts), L.Get("显示菜单快捷键")); Bind(lockOptions, nameof(AppSettings.PinConfirmKeyClose), L.Get("按键关闭钉图需要确认")); Bind(lockOptions, nameof(AppSettings.PinConfirmMouseClose), L.Get("鼠标关闭钉图需要确认")); Unavailable(lockOptions, L.Get("按键 / 鼠标销毁钉图确认"), L.Get("销毁操作在管理器中固定确认"), L.Get("需要确认"));
        var display = SettingsLayout.Section(page.Panel, L.Get("缩放、透明度与显示")); Bind(display, nameof(AppSettings.PinShowShadow), L.Get("显示窗口阴影")); Bind(display, nameof(AppSettings.PinDefaultOpacity), L.Get("钉图窗口默认不透明度（%）"), "10–100"); Bind(display, nameof(AppSettings.PinClickThroughOpacity), L.Get("启用穿透时不透明度上限（%）"), L.Get("10–100；退出穿透恢复原值")); Bind(display, nameof(AppSettings.PinZoomStep), L.Get("滚轮缩放步长（%）"), "1–100"); Bind(display, nameof(AppSettings.PinFineZoomStep), L.Get("Alt + 滚轮精细缩放步长（%）"), "1–100"); Bind(display, nameof(AppSettings.PinOpacityStep), L.Get("滚轮不透明度步长（%）"), "1–100"); var anchors = new[] { "TopLeft", "Mouse", "Center", "TopRight", "BottomLeft", "BottomRight", "Disabled" }; var anchorLabels = new[] { L.Get("基于左上角"), L.Get("基于鼠标"), L.Get("基于中心"), L.Get("基于右上角"), L.Get("基于左下角"), L.Get("基于右下角"), L.Get("禁用") }; Choice(display, nameof(AppSettings.PinWheelAnchor), L.Get("鼠标滚轮缩放模式"), anchors, anchorLabels); Choice(display, nameof(AppSettings.PinMiddleAnchor), L.Get("中键缩放模式"), anchors, anchorLabels); Bind(display, nameof(AppSettings.PinResizeFromBorder), L.Get("使用边框控制窗口大小")); Bind(display, nameof(AppSettings.PinClickThroughToolbar), L.Get("鼠标穿透时显示救援工具栏")); Choice(display, nameof(AppSettings.PinOutputImage), L.Get("保存 / 复制时的图像类型"), new[] { "Current", "Original" }, new[] { L.Get("当前图像"), L.Get("原始图像") }); Bind(display, nameof(AppSettings.PinDefaultTopmost), L.Get("新建钉图窗口置顶")); Bind(display, nameof(AppSettings.PinAutoFit), L.Get("钉图过大时自动缩小")); Bind(display, nameof(AppSettings.PinRescueOnShow), L.Get("调整钉图以确保显示")); Bind(display, nameof(AppSettings.PinKeepOthersHidden), L.Get("新建钉图时保持其他钉图隐藏状态")); Bind(display, nameof(AppSettings.PinTaskbarWhenUnpinned), L.Get("取消置顶时显示任务栏图标"));
        var mouse = SettingsLayout.Section(page.Panel, L.Get("鼠标操作")); var values = new[] { "Zoom", "Opacity", "Original", "Close", "Hide", "Copy", "Lock", "None" }; var labels = new[] { L.Get("缩放"), L.Get("透明度"), L.Get("原始大小 / 切回"), L.Get("关闭"), L.Get("隐藏"), L.Get("复制"), L.Get("锁定切换"), L.Get("无操作") };
        Choice(mouse, nameof(AppSettings.PinWheelAction), L.Get("滚轮"), values, labels); Choice(mouse, nameof(AppSettings.PinControlWheelAction), L.Get("Ctrl + 滚轮"), values, labels); Choice(mouse, nameof(AppSettings.PinMiddleAction), L.Get("中键单击"), values, labels); SettingsLayout.FullRow(mouse, SettingsLayout.Text(L.Get("左键双击固定为复制当前图并关闭；上方动作仅用于滚轮与中键。"), 11, "SubtleBrush"));
        var other = SettingsLayout.Section(page.Panel, L.Get("缩略图与窗口行为")); foreach (var title in new[] { L.Get("缩略图模式滚轮缩放"), L.Get("缩略图模式文字选择"), L.Get("缩略图缩放模式"), L.Get("标题位置"), L.Get("重复钉图时的动作"), L.Get("替换钉图时的动作"), L.Get("震荡时隐藏其他钉图"), L.Get("Mac 虚拟桌面显示") }) Unavailable(other, title, L.Get("此行为暂未开放"), L.Get("不可用"));
        var image = SettingsLayout.Section(page.Panel, L.Get("图像")); Choice(image, nameof(AppSettings.PinTransparentBackground), L.Get("默认透明层处理"), new[] { "Checkerboard", "Transparent", "Light", "Dark" }, new[] { L.Get("棋盘图像"), L.Get("保留透明"), L.Get("白色背景"), L.Get("深色背景") }); Bind(image, nameof(AppSettings.PinSmoothScaling), L.Get("平滑缩放")); Unavailable(image, L.Get("显示文本菜单"), L.Get("图内文本选择暂未开放"), L.Get("不可用")); Unavailable(image, L.Get("文本高亮颜色"), L.Get("图内文本选择暂未开放"), L.Get("不可用"));
        var text = SettingsLayout.Section(page.Panel, L.Get("文本")); foreach (var title in new[] { L.Get("纯文本最大宽度"), L.Get("文本内边距"), L.Get("双击贴图文本选择"), L.Get("显示 HTML 样式"), L.Get("HTML 最大宽度"), L.Get("自适应屏幕 DPI"), L.Get("文本字体"), L.Get("文本颜色"), L.Get("背景颜色"), L.Get("长文本钉图需要确认") }) Unavailable(text, title, L.Get("文字钉图暂未开放"), L.Get("不可用")); var windowPin = SettingsLayout.Section(page.Panel, L.Get("窗口钉图")); Unavailable(windowPin, L.Get("隐藏原窗口"), L.Get("窗口钉图暂未开放"), L.Get("关闭"));
    }
    private void BuildSavePage()
    {
        var page = Page("save", L.Get("保存"), ToolGlyph.Save); var image = SettingsLayout.Section(page.Panel, L.Get("保存 / 复制通用设置")); Bind(image, nameof(AppSettings.JpegQuality), L.Get("保存 / 复制图像质量"), L.Get("0–100；0 为最低 JPEG 质量，PNG 等无损格式保持原像素")); Bind(image, nameof(AppSettings.SaveCopyFile), L.Get("手动 / 快速保存后复制文件")); Bind(image, nameof(AppSettings.SaveCopyPath), L.Get("手动 / 快速保存后复制路径")); Bind(image, nameof(AppSettings.SaveExif), L.Get("JPEG / TIFF 保存时写入 EXIF 时间与软件信息")); Bind(image, nameof(AppSettings.CopyFileWithImage), L.Get("复制图片时同时提供 PNG 文件"));
        var manual = SettingsLayout.Section(page.Panel, L.Get("手动保存")); Bind(manual, nameof(AppSettings.ManualSaveNameTemplate), L.Get("文件名模板"), "{date} / {time} / {width} / {height} / {kind} / {title}"); Choice(manual, nameof(AppSettings.ManualSaveFormat), L.Get("默认图片格式"), new[] { "PNG", "JPEG", "BMP", "TIFF" }); Bind(manual, nameof(AppSettings.RememberSaveExtension), L.Get("记住上次使用的扩展名")); AddSavePreview(manual, nameof(AppSettings.ManualSaveNameTemplate), nameof(AppSettings.ManualSaveFormat));
        var quick = SettingsLayout.Section(page.Panel, L.Get("快速保存")); Bind(quick, nameof(AppSettings.QuickSaveNotification), L.Get("显示通知")); Choice(quick, nameof(AppSettings.QuickSaveNotificationAction), L.Get("点击通知后"), new[] { "Locate", "CopyFile", "CopyPath", "None" }, new[] { L.Get("定位文件"), L.Get("复制文件"), L.Get("复制路径"), L.Get("无动作") }); DirectoryField(quick, nameof(AppSettings.QuickSaveDirectory), L.Get("保存目录"), L.Get("留空使用本机数据目录下的 quick-saves")); SettingsLayout.FullRow(quick, SettingsLayout.Button(L.Get("打开文件夹"), () => OpenSaveFolder(nameof(AppSettings.QuickSaveDirectory), "quick-saves"))); Choice(quick, nameof(AppSettings.QuickSaveFormat), L.Get("图片格式"), new[] { "PNG", "JPEG", "BMP", "TIFF" }); Bind(quick, nameof(AppSettings.QuickSaveOverwrite), L.Get("覆盖同名文件（关闭时添加数字后缀）"));
        var naming = SettingsLayout.Section(page.Panel, L.Get("快速保存文件命名"), L.Get("手动保存、快速录制与自动留档使用各自的独立模板。")); Bind(naming, nameof(AppSettings.SaveNameTemplate), L.Get("命名模板"), "{date} / {time} / {width} / {height} / {kind} / {title}"); namePreview.Name = "SettingsSaveNamePreview"; System.Windows.Automation.AutomationProperties.SetName(namePreview, L.Get("文件名预览")); SettingsLayout.FullRow(naming, namePreview);
        textPreferences[nameof(AppSettings.SaveNameTemplate)].TextChanged += (_, _) => UpdateNamePreview(); textPreferences[nameof(AppSettings.QuickSaveDirectory)].TextChanged += (_, _) => UpdateNamePreview(); choicePreferences[nameof(AppSettings.QuickSaveFormat)].SelectionChanged += (_, _) => UpdateNamePreview();
        var quickRecord = SettingsLayout.Section(page.Panel, L.Get("快速录制")); Bind(quickRecord, nameof(AppSettings.QuickRecordingEnabled), L.Get("使用预设路径快速录制 MP4"));
        var quickRecordDirectory = DirectoryField(quickRecord, nameof(AppSettings.QuickRecordingDirectory), L.Get("快速录制目录"), L.Get("留空使用 recordings"));
        Bind(quickRecord, nameof(AppSettings.QuickRecordingNameTemplate), L.Get("录制文件名模板")); Bind(quickRecord, nameof(AppSettings.QuickRecordingOverwrite), L.Get("覆盖同名录制（关闭时添加数字后缀）"));
        AddSavePreview(quickRecord, nameof(AppSettings.QuickRecordingNameTemplate), null, ".mp4", "Recording", directory: nameof(AppSettings.QuickRecordingDirectory), fallback: "recordings"); SettingsLayout.FullRow(quickRecord, SettingsLayout.Button(L.Get("打开录制文件夹"), () => OpenSaveFolder(nameof(AppSettings.QuickRecordingDirectory), "recordings")));
        var automatic = SettingsLayout.Section(page.Panel, L.Get("自动保存 / 留档")); Bind(automatic, nameof(AppSettings.AutomaticSaveEnabled), L.Get("启用自动保存（按下方选中的操作类型留档）"));
        var automaticDirectory = DirectoryField(automatic, nameof(AppSettings.AutomaticSaveDirectory), L.Get("留档目录"), L.Get("留空使用独立本机目录 auto-saves；文件添加 DeskSnip-auto- 标识以便安全清理"));
        Bind(automatic, nameof(AppSettings.AutomaticSaveNameTemplate), L.Get("自动保存命名模板")); Choice(automatic, nameof(AppSettings.AutomaticSaveFormat), L.Get("自动保存图片格式"), new[] { "PNG", "JPEG", "BMP", "TIFF" }); Bind(automatic, nameof(AppSettings.AutomaticSaveOverwrite), L.Get("覆盖同名留档（关闭时添加数字后缀）"));
        AddSavePreview(automatic, nameof(AppSettings.AutomaticSaveNameTemplate), nameof(AppSettings.AutomaticSaveFormat), prefix: "DeskSnip-auto-", directory: nameof(AppSettings.AutomaticSaveDirectory), fallback: "auto-saves"); SettingsLayout.FullRow(automatic, SettingsLayout.Button(L.Get("打开留档文件夹"), () => OpenSaveFolder(nameof(AppSettings.AutomaticSaveDirectory), "auto-saves")));
        Bind(automatic, nameof(AppSettings.AutoSaveCapture), L.Get("截图完成时留档")); Bind(automatic, nameof(AppSettings.AutoSaveCopy), L.Get("复制图片时留档")); Bind(automatic, nameof(AppSettings.AutoSavePin), L.Get("钉图时留档")); Bind(automatic, nameof(AppSettings.AutoSaveManual), L.Get("手动 / 快速保存时留档"));
        Bind(automatic, nameof(AppSettings.AutoSaveOcr), L.Get("文字 / 表格 / 公式 / 文档识别时留档")); Bind(automatic, nameof(AppSettings.AutoSaveTranslation), L.Get("从图片发起翻译时留档")); Bind(automatic, nameof(AppSettings.AutoSaveLongScreenshot), L.Get("长截图输出时留档")); Bind(automatic, nameof(AppSettings.AutomaticSaveCleanup), L.Get("启用自动清理早于指定天数的留档"));
        Bind(automatic, nameof(AppSettings.AutomaticSaveDays), L.Get("保留天数"), L.Get("1–36500 天")); Bind(automatic, nameof(AppSettings.AutomaticSaveMegabytes), L.Get("留档容量（MB）"), L.Get("至少 1 MB；最新一张始终保留，仅清理本应用登记的留档"));
        EnableWhen(quickRecordDirectory, nameof(AppSettings.QuickRecordingEnabled)); EnableWhen(textPreferences[nameof(AppSettings.QuickRecordingNameTemplate)], nameof(AppSettings.QuickRecordingEnabled)); EnableWhen(boolPreferences[nameof(AppSettings.QuickRecordingOverwrite)], nameof(AppSettings.QuickRecordingEnabled));
        EnableWhen(automaticDirectory, nameof(AppSettings.AutomaticSaveEnabled));
        foreach (var name in new[] { nameof(AppSettings.AutomaticSaveNameTemplate), nameof(AppSettings.AutomaticSaveMegabytes) }) EnableWhen(textPreferences[name], nameof(AppSettings.AutomaticSaveEnabled));
        foreach (var name in new[] { nameof(AppSettings.AutomaticSaveOverwrite), nameof(AppSettings.AutoSaveCapture), nameof(AppSettings.AutoSaveCopy), nameof(AppSettings.AutoSavePin), nameof(AppSettings.AutoSaveManual), nameof(AppSettings.AutoSaveOcr), nameof(AppSettings.AutoSaveTranslation), nameof(AppSettings.AutoSaveLongScreenshot), nameof(AppSettings.AutomaticSaveCleanup) }) EnableWhen(boolPreferences[name], nameof(AppSettings.AutomaticSaveEnabled));
        EnableWhen(textPreferences[nameof(AppSettings.AutomaticSaveDays)], nameof(AppSettings.AutomaticSaveEnabled), nameof(AppSettings.AutomaticSaveCleanup));
        EnableWhen(choicePreferences[nameof(AppSettings.AutomaticSaveFormat)], nameof(AppSettings.AutomaticSaveEnabled)); EnableWhen(choicePreferences[nameof(AppSettings.QuickSaveNotificationAction)], nameof(AppSettings.QuickSaveNotification));
    }
    private void BuildRecordingPage()
    {
        var page = Page("recording", L.Get("录屏"), ToolGlyph.Stop); var capture = SettingsLayout.Section(page.Panel, L.Get("录制默认值")); Choice(capture, nameof(AppSettings.RecordingFormat), L.Get("录制格式"), new[] { "MP4", "GIF", "WebP" }); Bind(capture, nameof(AppSettings.FramesPerSecond), L.Get("录制帧率"), L.Get("1–60 FPS；GIF 最大 15 FPS")); Bind(capture, nameof(AppSettings.RecordingDelay), L.Get("开始倒计时（秒）"), L.Get("0–30 秒")); Bind(capture, nameof(AppSettings.CaptureCursor), L.Get("包含鼠标指针"));
        var audio = SettingsLayout.Section(page.Panel, L.Get("声音")); Bind(audio, nameof(AppSettings.RecordSystemAudio), L.Get("默认录制系统声音")); Bind(audio, nameof(AppSettings.RecordMicrophone), L.Get("默认录制麦克风"));
        var encode = SettingsLayout.Section(page.Panel, L.Get("编码与输出")); FileField(encode, nameof(AppSettings.FfmpegPath), L.Get("FFmpeg 程序"), L.Get("留空从 PATH 查找；声音、WebP、剪辑和恢复需要"), L.Get("FFmpeg|ffmpeg.exe|程序|*.exe")); Bind(encode, nameof(AppSettings.VideoQuality), L.Get("默认画质"), "1–100"); Choice(encode, nameof(AppSettings.VideoEncoder), L.Get("最终导出编码器"), Enum.GetNames(typeof(VideoEncoder)), new[] { L.Get("软件"), L.Get("自动硬件 / 软件回退"), "NVIDIA", "Intel", "AMD" }); DirectoryField(encode, nameof(AppSettings.OutputDirectory), L.Get("默认录制目录"), L.Get("普通录制保存时的初始文件夹；留空使用“视频”或桌面"));
    }
    private void BuildTransferPage()
    {
        var page = Page("transfer", L.Get("导出 / 导入"), ToolGlyph.Transfer); var transfer = SettingsLayout.Section(page.Panel, L.Get("偏好配置"), L.Get("导出已修改的偏好；导入时合并到当前设置，点击保存后生效。"));
        SettingsLayout.FullRow(transfer, SettingsLayout.Button(L.Get("导出已修改偏好…"), Export)); SettingsLayout.FullRow(transfer, SettingsLayout.Button(L.Get("导入偏好…"), Import)); SettingsLayout.FullRow(transfer, SettingsLayout.Text(L.Get("配置文件包含主题、快捷键、保存方式等可移植偏好。本机目录、FFmpeg 路径和服务地址 / 密钥不导出。"), 12, "SubtleBrush"));
        var reset = SettingsLayout.Section(page.Panel, L.Get("恢复默认")); SettingsLayout.FullRow(reset, SettingsLayout.Button(L.Get("恢复全部默认值"), () => { serviceKey.Clear(); clearServiceKey.IsChecked = false; LoadValues(new AppSettings()); footerHint.Text = L.Get("已恢复全部默认值，保存后生效"); })); SettingsLayout.FullRow(reset, SettingsLayout.Text(L.Get("恢复仅修改当前设置窗口。取消可放弃；已保存的图片、钉图和录制素材保留。"), 12, "SubtleBrush"));
    }
    private void BuildAboutPage()
    {
        var page = Page("about", L.Get("关于"), ToolGlyph.Info); var product = SettingsLayout.Section(page.Panel, "DeskSnip"); Hint(product, L.Get("版本"), AppInfo.Version); Hint(product, L.Get("功能"), L.Get("截图、标注、图片钉图、区域录屏、识别与翻译")); Hint(product, L.Get("数据"), L.Get("配置和内容保存在本机；联网识别仅在明确请求时发送当前图片或文字。"));
    }
    private static void Hint(Panel group, string title, string content)
    {
        var note = SettingsLayout.Text(content, 12, "SubtleBrush"); SettingsLayout.Row(group, title, note);
    }
    private Panel DirectoryField(Panel group, string name, string label, string description)
    {
        var input = new TextBox(); SettingsLayout.Input(input, name, label, double.PositiveInfinity); textPreferences.Add(name, input); buildingPage.Preferences.Add(name);
        var row = new DockPanel(); var browse = SettingsLayout.Button(L.Get("选择…"), () =>
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = label, SelectedPath = Directory.Exists(input.Text) ? input.Text : "" }) if (dialog.ShowDialog(new NativeWindowOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle)) == System.Windows.Forms.DialogResult.OK) input.Text = dialog.SelectedPath;
        }); browse.Margin = new Thickness(8, 0, 0, 0); DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse); row.Children.Add(input); preferenceLabels.Add(name, SettingsLayout.Row(group, label, row, description)); input.TextChanged += (_, _) => MarkChanged(name); return row;
    }
    private void FileField(Panel group, string name, string label, string description, string filter)
    {
        var input = new TextBox(); SettingsLayout.Input(input, name, label, double.PositiveInfinity); textPreferences.Add(name, input); buildingPage.Preferences.Add(name);
        var row = new DockPanel(); var browse = SettingsLayout.Button(L.Get("选择…"), () => { var dialog = new Microsoft.Win32.OpenFileDialog { Filter = filter }; if (dialog.ShowDialog(this) == true) input.Text = dialog.FileName; }); browse.Margin = new Thickness(8, 0, 0, 0); DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse); row.Children.Add(input); preferenceLabels.Add(name, SettingsLayout.Row(group, label, row, description)); input.TextChanged += (_, _) => MarkChanged(name);
    }
    private sealed class NativeWindowOwner : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; }
        internal NativeWindowOwner(IntPtr handle) { Handle = handle; }
    }
    private void ChooseAccent()
    {
        using (var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true })
        {
            try { dialog.Color = System.Drawing.ColorTranslator.FromHtml(textPreferences[nameof(AppSettings.AccentColor)].Text); } catch (ArgumentException) { }
            if (dialog.ShowDialog(new NativeWindowOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle)) == System.Windows.Forms.DialogResult.OK) textPreferences[nameof(AppSettings.AccentColor)].Text = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        }
    }
    private void UpdateAccentPreview()
    {
        if (textPreferences.TryGetValue(nameof(AppSettings.AccentColor), out var input)) { try { accentSwatch.Background = (Brush)new BrushConverter().ConvertFromString(input.Text)!; } catch (Exception error) when (error is FormatException || error is NotSupportedException || error is ArgumentException) { accentSwatch.Background = Brushes.Transparent; } }
    }
    private void UpdateNamePreview()
    {
        if (!textPreferences.TryGetValue(nameof(AppSettings.SaveNameTemplate), out var input)) return;
        try { var format = (string?)choicePreferences[nameof(AppSettings.QuickSaveFormat)].SelectedValue ?? "PNG"; var extension = format == "JPEG" ? ".jpg" : format == "TIFF" ? ".tiff" : "." + format.ToLowerInvariant(); namePreview.Text = L.Join(L.Get("预览："),PreviewPath(nameof(AppSettings.QuickSaveDirectory), "quick-saves", SaveNaming.Expand(input.Text, DateTime.Now, 1920, 1080, "Image", L.Get("示例")) + extension)); }
        catch (ArgumentException error) { namePreview.Text = error.Message; }
        catch (FormatException) { namePreview.Text = L.Get("模板中的日期或时间格式无效。"); }
    }
}

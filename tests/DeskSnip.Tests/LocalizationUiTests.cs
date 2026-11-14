using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskSnip.App;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

[Collection("Interface language")]
public sealed class LocalizationUiTests
{
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) foreach (var nested in Descendants(child)) yield return nested;
    }
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static Control Preference(Window window, string name) => Descendants(window).OfType<Control>().Single(control => AutomationProperties.GetAutomationId(control) == name);
    private static AppSettings Read(SettingsWindow window) => (AppSettings)typeof(SettingsWindow).GetMethod("ReadValues", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle);
    private static BitmapSource Image()
    {
        var pixels = new byte[160 * 100 * 4]; for (var index = 0; index < pixels.Length; index += 4) { pixels[index] = 220; pixels[index + 1] = 190; pixels[index + 2] = 160; pixels[index + 3] = 255; }
        var image = BitmapSource.Create(160, 100, 96, 96, PixelFormats.Bgra32, null, pixels, 640); image.Freeze(); return image;
    }
    private sealed class LanguageScope : IDisposable
    {
        private readonly string preference = InterfaceLanguageState.Current.Preference;
        private readonly CultureInfo culture = CultureInfo.CurrentUICulture;
        private readonly CultureInfo? defaultCulture = CultureInfo.DefaultThreadCurrentUICulture;
        internal LanguageScope(string language = "zh-CN") => L.Apply(language);
        public void Dispose() { L.Apply(preference); CultureInfo.CurrentUICulture = culture; CultureInfo.DefaultThreadCurrentUICulture = defaultCulture; }
    }

    [Fact] public void UpdatingLabelsKeepsSelectionsBindingsAndEditableContent() => ImagingTests.Sta(() =>
    {
        using (new LanguageScope())
        {
            var user = new string("保存设置".ToCharArray());
            var labels = new[] { L.Get("保留换行"), L.Get("合并换行"), user };
            var choice = new ComboBox { ItemsSource = labels, SelectedIndex = 1 };
            var text = new TextBox { Text = user }; var caption = new TextBlock();
            caption.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Text") { Source = text });
            var menu = new ContextMenu(); menu.Items.Add(new MenuItem { Header = L.Get("复制文本") });
            var panel = new StackPanel(); panel.Children.Add(choice); panel.Children.Add(text); panel.Children.Add(caption);
            var window = new ProductWindow { Title = L.Get("DeskSnip · 本地文字识别"), Content = panel, ContextMenu = menu };
            try
            {
                var changes = 0; choice.SelectionChanged += (_, _) => changes++;
                var selected = choice.SelectedItem; L.Apply("en-US"); Drain();
                Assert.Equal(0, changes); Assert.Same(labels, choice.ItemsSource); Assert.Same(selected, choice.SelectedItem);
                Assert.Equal(user, text.Text); Assert.Equal(user, caption.Text); Assert.True(System.Windows.Data.BindingOperations.IsDataBound(caption, TextBlock.TextProperty));
                Assert.Equal("DeskSnip · Local text recognition", window.Title); Assert.Equal(TextCatalog.Get("复制文本"), ((MenuItem)menu.Items[0]).Header);
                choice.ItemTemplate.Seal(); var rendered = (TextBlock)choice.ItemTemplate.LoadContent(); rendered.DataContext = choice.SelectedItem; Drain(); Assert.Equal(TextCatalog.Get("合并换行"), rendered.Text);
                L.Apply("zh-CN"); Drain(); Assert.Equal("合并换行", rendered.Text); Assert.Equal(0, changes); Assert.Equal(user, text.Text);
            }
            finally { window.Close(); }
        }
    });

    [Theory]
    [InlineData(760, 540, "Light")]
    [InlineData(1020, 780, "Dark")]
    public void AllSettingsPagesSwitchBothWaysWithoutLosingDrafts(int width, int height, string theme) => ImagingTests.Sta(() =>
    {
        using (new LanguageScope())
        {
            var window = new SettingsWindow(new AppSettings { Theme = theme, CaptureHotkey = "Ctrl+NumPad5" }, _ => { });
            try
            {
                window.SelectPage("annotation"); ((TextBox)Preference(window, nameof(AppSettings.MarkWatermarkText))).Text = "保存设置"; window.SelectPage("system");
                var systemLanguage = (ComboBox)Preference(window, nameof(AppSettings.InterfaceLanguage)); systemLanguage.SelectedValue = "en-US";
                Assert.Equal("zh-CN", InterfaceLanguageState.Current.Preference);
                Assert.Equal("en-US", Read(window).InterfaceLanguage);
                foreach (var language in new[] { "en-US", "zh-CN", "en-US" })
                {
                    L.Apply(language); Drain();
                    foreach (var item in Field<ListBox>(window, "navigation").Items.OfType<ListBoxItem>())
                    {
                        var page = (SettingsPage)item.Tag; window.SelectPage(page.Id);
                        var root = (Grid)window.Content; root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); Drain();
                        Assert.Equal(L.Format("{0}设置", page.Title), AutomationProperties.GetName(page.View));
                        Assert.Equal("en-US", Read(window).InterfaceLanguage); Assert.Equal("保存设置", Read(window).MarkWatermarkText);
                        Assert.Equal("Ctrl+NumPad5", Read(window).CaptureHotkey);
                        if (page.Id == "shortcuts") Assert.Equal(language == "en-US" ? "Ctrl+Numpad 5" : "Ctrl+小键盘 5", ((TextBox)Preference(window, "Action_Capture")).Text);
                        foreach (var block in Descendants(page.View).OfType<TextBlock>().Where(block => TextCatalog.IsProductText(block.Text)))
                            if (language == "en-US" && block.Text != "简体中文") Assert.DoesNotMatch(@"[\p{IsCJKUnifiedIdeographs}]", block.Text);
                        foreach (var input in Descendants(page.View).OfType<TextBox>().Where(input => !input.IsEnabled && TextCatalog.IsProductText(input.Text)))
                            if (language == "en-US") Assert.DoesNotMatch(@"[\p{IsCJKUnifiedIdeographs}]", input.Text);
                        if (page.Id == "system" || page.Id == "shortcuts" || page.Id == "recording") Preview(root, width, height, "language-" + language + "-" + page.Id + "-" + theme);
                    }
                }
                window.SelectPage("system"); typeof(SettingsWindow).GetMethod("RestoreCurrentPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                Assert.Equal("zh-CN", Read(window).InterfaceLanguage); Assert.Equal("en-US", InterfaceLanguageState.Current.Preference); Assert.Equal("保存设置", Read(window).MarkWatermarkText);
            }
            finally { window.Close(); }
        }
    });

    [Fact] public void OcrCorrectionsAndAnnotationObjectsSurviveLanguageChanges() => ImagingTests.Sta(() =>
    {
        using (new LanguageScope())
        {
            var ocr = new OcrWindow(Image()); var editor = new EditorWindow(Image(), _ => { }, _ => null!);
            try
            {
                var line = new RecognizedLine(new[] { new RecognizedWord("original recognition", new PixelRect(1, 1, 10, 10)) });
                typeof(OcrWindow).GetField("lines", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(ocr, new[] { line });
                var text = Field<TextBox>(ocr, "text"); text.Text = "保存设置 · corrected OCR text"; text.Select(2, 4);
                var canvas = Field<AnnotationCanvas>(editor, "canvas"); var annotation = new Annotation(AnnotationKind.Text, new[] { new PixelPoint(5, 8) }, 0xff000000, 2, "保存设置"); canvas.Document.Add(annotation);
                var dash = Field<ComboBox>(editor, "dash"); dash.SelectedIndex = 2;
                L.Apply("en-US"); Drain();
                Assert.Equal("保存设置 · corrected OCR text", ocr.ResultText); Assert.Equal(2, text.SelectionStart); Assert.Equal(4, text.SelectionLength);
                Assert.Equal(2, dash.SelectedIndex); Assert.Same(annotation, Assert.Single(canvas.Document.Items));
                L.Apply("zh-CN"); Drain(); Assert.Equal("保存设置 · corrected OCR text", ocr.ResultText); Assert.Same(annotation, Assert.Single(canvas.Document.Items));
            }
            finally { ocr.Close(); editor.Close(); }
        }
    });

    [Fact] public void SavingLanguageUpdatesAnOpenWindowAndPersistsForNextStartup() => ImagingTests.Sta(() =>
    {
        var root = Path.Combine(Path.GetTempPath(), "DeskSnip-language-save-" + Guid.NewGuid().ToString("N"));
        using (new LanguageScope())
        {
            var store = new LocalStore(root); var saves = 0; var draftCulture = "";
            var open = new ProductWindow { Title = L.Get("DeskSnip · 开始录制") };
            var window = new SettingsWindow(store.Settings, value => { store.SaveSettings(value); L.Apply(value.InterfaceLanguage); saves++; }, root);
            try
            {
                window.Loaded += (_, _) =>
                {
                    ((ComboBox)Preference(window, nameof(AppSettings.InterfaceLanguage))).SelectedValue = "en-US";
                    draftCulture = InterfaceLanguageState.Current.Preference;
                    Descendants(window).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "保存设置").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                Assert.True(window.ShowDialog());
                Assert.Equal("zh-CN", draftCulture); Assert.Equal(1, saves); Assert.Equal("en-US", new LocalStore(root).Settings.InterfaceLanguage);
                Assert.Equal(TextCatalog.Get("DeskSnip · 开始录制"), open.Title); Assert.DoesNotMatch(@"[\p{IsCJKUnifiedIdeographs}]", open.Title);
                var next = new SettingsWindow(new LocalStore(root).Settings, _ => { });
                try { Assert.Equal("en-US", ((ComboBox)Preference(next, nameof(AppSettings.InterfaceLanguage))).SelectedValue); }
                finally { next.Close(); }
            }
            finally { window.Close(); open.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    });

    [Fact] public void FormulaAndMarkdownEditsSurviveLiveLanguageSwitches() => RecognitionServiceUiTests.StaAsync(async () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "DeskSnip-language-service-" + Guid.NewGuid().ToString("N"));
        using (new LanguageScope())
        {
            var window = new RecognitionServiceWindow(null, new AppSettings(), root);
            try
            {
                window.ApplyResult(RecognitionServiceTests.Result("formula")); var formulas = Field<ListBox>(window, "formulaList");
                var source = Field<TextBox>(window, "formulaSource"); source.Text = "x^2+42"; var index = formulas.SelectedIndex;
                L.Apply("en-US"); Assert.Equal(index, formulas.SelectedIndex); Assert.Equal("x^2+42", source.Text); Assert.Equal("x^2+42", window.Snapshot()!.Formulas[index]);
                window.ApplyResult(RecognitionServiceTests.Result("document")); var markdown = Field<TextBox>(window, "resultText"); markdown.Text = "# 保存设置\n\nEdited document";
                L.Apply("zh-CN"); Assert.Equal("# 保存设置\n\nEdited document", window.Snapshot()!.Markdown);
            }
            finally { await window.CloseSafelyAsync(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    });

    [Theory]
    [InlineData("Ctrl+NumPad5", "Ctrl+小键盘 5", "Ctrl+Numpad 5")]
    [InlineData("Ctrl+Add", "Ctrl+小键盘 +", "Ctrl+Numpad +")]
    [InlineData("Subtract", "小键盘 -", "Numpad -")]
    [InlineData("Oem102", "反斜杠", "Backslash")]
    public void ShortcutDisplayAcceptsBothLanguagesAfterSwitching(string stored, string chinese, string english) => ImagingTests.Sta(() =>
    {
        using (new LanguageScope())
        {
            Assert.Equal(chinese, ShortcutKeys.Display(stored)); L.Apply("en-US"); Assert.Equal(english, ShortcutKeys.Display(stored));
            Assert.Equal(stored, ShortcutKeys.FromDisplay(chinese)); Assert.Equal(stored, ShortcutKeys.FromDisplay(english));
            L.Apply("zh-CN"); Assert.Equal(stored, ShortcutKeys.FromDisplay(english));
        }
    });

    private static void Preview(Visual visual, int width, int height, string name)
    {
        var directory = Environment.GetEnvironmentVariable("DESK_SNIP_TEST_PREVIEW_DIR"); if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
        var pixel = new byte[4]; image.CopyPixels(new Int32Rect(5, 5, 1, 1), pixel, 4, 0); Assert.True(pixel[3] > 0, "The desktop returned an empty preview.");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var output = File.Create(Path.Combine(directory, name + ".png"))) encoder.Save(output);
    }
}

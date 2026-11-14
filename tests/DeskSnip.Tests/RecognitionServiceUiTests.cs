using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskSnip.App;
using DeskSnip.App.Recognition;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public sealed class RecognitionServiceUiTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "DeskSnip-service-ui-" + Guid.NewGuid().ToString("N"));
    internal static void StaAsync(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher; SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () => { try { await action(); } catch (Exception error) { failure = error; } finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } })); Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    [Fact] public void RepeatedServiceResultsReuseEditorsAndKeepEditedSnapshots() => StaAsync(async () =>
    {
        var window = new RecognitionServiceWindow(null, new AppSettings(), root);
        foreach (var operation in new[] { "translation", "table", "table", "formula", "formula", "document", "text", "document" })
        {
            window.ApplyResult(RecognitionServiceTests.Result(operation)); Assert.Equal(operation, window.Snapshot()!.Operation);
            if (operation == "translation") { var input = Descendants((DependencyObject)window.Content).OfType<TextBox>().Single(t => t.Text == "示例译文"); input.Text = "校对后的译文"; Assert.Equal("校对后的译文", window.Snapshot()!.TranslatedText); }
        }
        await window.CloseSafelyAsync(); var saved = new RecognitionResultStore(root).Read(); Assert.Equal("document", saved!.Operation);
    });
    [Theory] [InlineData("table")][InlineData("formula")][InlineData("document")]
    public void ServiceEditorsFitMinimumWindowAndRenderWithoutExternalContent(string operation) => StaAsync(async () =>
    {
        var window = new RecognitionServiceWindow(null, new AppSettings(), root); window.ApplyResult(RecognitionServiceTests.Result(operation));
        var content = (DockPanel)window.Content; content.Background = Brushes.White; var size = new Size(930, 600); content.Measure(size); content.Arrange(new Rect(new Point(), size)); content.UpdateLayout();
        foreach (var button in Descendants(content).OfType<Button>())
        {
            var bounds = button.TransformToAncestor(content).TransformBounds(new Rect(new Point(), button.RenderSize)); Assert.True(new Rect(new Point(), size).Contains(bounds), "Service button outside viewport: " + button.Content);
        }
        Preview(content, 930, 600, "service-" + operation); await window.CloseSafelyAsync();
    });
    [Fact] public void FormulaRendersRealPixelsAndUnsafeDepthFailsWithoutLosingSource() => ImagingTests.Sta(() =>
    {
        var image = FormulaImages.Render("$$\\frac{x^2+1}{2}$$"); Assert.True(image.PixelWidth > 20); Assert.True(image.PixelHeight > 20);
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0); Assert.Contains(pixels.Where((_, index) => index % 4 == 3), alpha => alpha > 0);
        Assert.Equal("x^2", FormulaImages.Source("\\[x^2\\]")); Assert.Throws<ArgumentException>(() => FormulaImages.Render(new string('{', 33) + "x" + new string('}', 33)));
        var markdown = RecognitionServiceWindow.MarkdownPreview("# 标题\n\n| A | B |\n| --- | --- |\n| 1 | 2 |\n\n![外部](https://example.com/image.png)", Array.Empty<RecognitionAsset>());
        Assert.Contains(markdown.Blocks, block => block is Table); Assert.DoesNotContain(markdown.Blocks, block => block is BlockUIContainer);
    });
    [Fact] public void ReadingPreferencesDoesNotWritePendingKeyAndFailedSaveRestoresCredential() => ImagingTests.Sta(() =>
    {
        var credentials = new ServiceCredentialStore(root); credentials.Write("synthetic-old-key");
        var window = new SettingsWindow(new AppSettings(), _ => throw new IOException("Synthetic save failure."), root);
        var content = (Grid)window.Content; window.SelectPage("service");
        var password = Descendants(content).OfType<PasswordBox>().Single(); password.Password = "synthetic-new-key";
        var values = (AppSettings)typeof(SettingsWindow).GetMethod("ReadValues", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
        Assert.Equal("synthetic-old-key", credentials.Read());
        var error = Assert.Throws<TargetInvocationException>(() => typeof(SettingsWindow).GetMethod("SaveConfigured", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { values })); Assert.IsType<IOException>(error.InnerException); Assert.Equal("synthetic-old-key", credentials.Read());
        content.Background = Brushes.White; content.Measure(new Size(830, 700)); content.Arrange(new Rect(0, 0, 830, 700)); content.UpdateLayout(); Preview(content, 830, 700, "settings-service"); window.Close();
    });
    [Fact] public void EditorCompactOutputsFitWithAllModuleEntrypoints() => ImagingTests.Sta(() =>
    {
        var image = ServiceImages.Decode(RecognitionServiceTests.SyntheticImage()); var editor = new EditorWindow(image, _ => { }, _ => null!, _ => { }, _ => { }, (_, _) => { }, new AppSettings(), _ => { });
        var content = (DockPanel)editor.Content; content.Background = Brushes.White; var size = new Size(820, 580); content.Measure(size); content.Arrange(new Rect(new Point(), size)); content.UpdateLayout();
        foreach (var button in Descendants(content).OfType<Button>()) { var bounds = button.TransformToAncestor(content).TransformBounds(new Rect(new Point(), button.RenderSize)); Assert.True(new Rect(new Point(), size).Contains(bounds), "Editor button outside viewport: " + button.Content); }
        Preview(content, 820, 580, "editor-all-tools"); editor.Close();
    });
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var item in LogicalTreeHelper.GetChildren(root)) if (item is DependencyObject child) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void Preview(Visual visual, int width, int height, string name)
    {
        var directory = Environment.GetEnvironmentVariable("DESK_SNIP_TEST_PREVIEW_DIR"); if (string.IsNullOrEmpty(directory)) return; Directory.CreateDirectory(directory);
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var output = File.Create(Path.Combine(directory, name + ".png"))) encoder.Save(output);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

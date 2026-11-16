using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DeskSnip.Core;

namespace DeskSnip.App;

/// <summary>选区左侧的标记列表：按竖排显示“类型图标 + 序号”，点击选中对应标记。</summary>
internal sealed class AnnotationListBar : FloatingToolbar
{
    private readonly AnnotationCanvas canvas;
    private readonly StackPanel items = new StackPanel { Orientation = Orientation.Vertical };
    private readonly ScrollViewer viewer;
    private readonly TextBlock topMore;
    private readonly TextBlock bottomMore;
    internal AnnotationListBar(AnnotationCanvas canvas)
    {
        this.canvas = canvas;
        Name = "AnnotationList";
        Padding = new Thickness(2);
        viewer = new ScrollViewer
        {
            Content = items, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, CanContentScroll = false
        };
        topMore = MoreMarker(VerticalAlignment.Top);
        bottomMore = MoreMarker(VerticalAlignment.Bottom);
        viewer.ScrollChanged += (_, _) => UpdateOverflow();
        Child = new Grid { Children = { viewer, topMore, bottomMore } };
        canvas.Changed += Refresh;
        Refresh();
    }
    internal void Refresh()
    {
        var offset = viewer.VerticalOffset;
        items.Children.Clear();
        var list = canvas.Document.Items;
        for (var index = 0; index < list.Count; index++)
        {
            var kind = list[index].Kind;
            var glyph = Glyph(kind);
            var selected = index == canvas.SelectedIndex;
            var icon = new ToolIcon(glyph) { Width = 11, Height = 11, VerticalAlignment = VerticalAlignment.Center, Color = selected ? FloatingTools.Blue : FloatingTools.Ink };
            var label = new TextBlock
            {
                Text = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), FontSize = 9,
                Foreground = selected ? FloatingTools.Blue : FloatingTools.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0)
            };
            var target = index;
            var button = FloatingTools.Button(glyph, $"{index + 1} · {EditorWindow.ToolNames[(int)kind]}", () => canvas.Dispatcher.BeginInvoke(new Action(() => canvas.SelectIndex(target))));
            button.Content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { icon, label } };
            button.Width = 36; button.Height = 22; button.Margin = new Thickness(0, 1, 0, 1);
            FloatingTools.SetSelected(button, selected);
            items.Children.Add(button);
        }
        Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (offset > 0) viewer.ScrollToVerticalOffset(offset);
            UpdateOverflow();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private static TextBlock MoreMarker(VerticalAlignment alignment) => new TextBlock
    {
        Text = "…", Foreground = FloatingTools.MutedInk, Background = Brushes.White, FontSize = 10,
        HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = alignment, TextAlignment = TextAlignment.Center,
        Height = 10, IsHitTestVisible = false, Visibility = Visibility.Collapsed
    };
    private void UpdateOverflow()
    {
        topMore.Visibility = viewer.VerticalOffset > .5 ? Visibility.Visible : Visibility.Collapsed;
        bottomMore.Visibility = viewer.VerticalOffset + viewer.ViewportHeight < viewer.ExtentHeight - .5 ? Visibility.Visible : Visibility.Collapsed;
    }
    private static ToolGlyph Glyph(AnnotationKind kind)
    {
        switch (kind)
        {
            case AnnotationKind.Rectangle: case AnnotationKind.RoundedRectangle: return ToolGlyph.Rectangle;
            case AnnotationKind.Ellipse: return ToolGlyph.Ellipse;
            case AnnotationKind.Line: case AnnotationKind.Polyline: return ToolGlyph.Line;
            case AnnotationKind.Arrow: return ToolGlyph.Arrow;
            case AnnotationKind.Pen: return ToolGlyph.Brush;
            case AnnotationKind.Highlight: return ToolGlyph.Highlight;
            case AnnotationKind.Text: case AnnotationKind.Watermark: return ToolGlyph.Text;
            case AnnotationKind.Number: return ToolGlyph.Number;
            case AnnotationKind.Mosaic: case AnnotationKind.MosaicPen: case AnnotationKind.Blur: return ToolGlyph.Mosaic;
            case AnnotationKind.Cover: case AnnotationKind.CoverPen: return ToolGlyph.Cover;
            case AnnotationKind.Eraser: case AnnotationKind.EraserPen: return ToolGlyph.Eraser;
            case AnnotationKind.Spotlight: return ToolGlyph.Spotlight;
            case AnnotationKind.Magnifier: return ToolGlyph.Zoom;
            default: return ToolGlyph.Rectangle;
        }
    }
}

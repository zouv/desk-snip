using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DeskSnip.Core;

namespace DeskSnip.App;

internal sealed class CapturePresetWindow : ProductWindow
{
    private readonly AppController controller;
    private readonly ListBox list = new ListBox { Width = 220, Margin = new Thickness(0, 0, 18, 0) };
    private readonly TextBox name = Field();
    private readonly TextBox[] geometry = { Field("0"), Field("0"), Field("800"), Field("600") };
    private readonly TextBox delay = Field("0");
    private readonly CheckBox fixedPosition = new CheckBox { Content = L.Get("固定位置直接截取；否则先显示选区"), Margin = new Thickness(5) };
    private readonly CheckBox cursor = new CheckBox { Content = L.Get("包含鼠标指针"), Margin = new Thickness(5) };
    private readonly ComboBox shape = new ComboBox { ItemsSource = new[] { L.Get("矩形"), L.Get("椭圆"), L.Get("自由形状"), L.Get("多边形") }, SelectedIndex = 0, Margin = new Thickness(5) };
    private string editingId = "";
    internal CapturePresetWindow(AppController controller)
    {
        this.controller = controller; Title = L.Get("DeskSnip · 截图预设"); Width = 760; Height = 590; MinWidth = 620; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root; DockPanel.SetDock(list, Dock.Left); root.Children.Add(list);
        var form = new StackPanel(); root.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); form.Children.Add(Ui.Text(L.Get("固定与延时截图"), 22)); form.Children.Add(Ui.Text(L.Get("预设名称"))); form.Children.Add(name);
        var labels = new[] { L.Get("X（物理像素）"), L.Get("Y（物理像素）"), L.Get("宽"), L.Get("高") }; for (var i = 0; i < geometry.Length; i++) { form.Children.Add(Ui.Text(labels[i])); form.Children.Add(geometry[i]); }
        form.Children.Add(Ui.Button(L.Get("交换宽高"), () => { var value = geometry[2].Text; geometry[2].Text = geometry[3].Text; geometry[3].Text = value; }));
        form.Children.Add(Ui.Text(L.Get("延时秒数（0–30）"))); form.Children.Add(delay); form.Children.Add(shape); form.Children.Add(fixedPosition); form.Children.Add(cursor);
        var buttons = new WrapPanel(); form.Children.Add(buttons);
        buttons.Children.Add(Ui.Button(L.Get("新建"), () => { editingId = ""; name.Text = L.Get("新预设"); })); buttons.Children.Add(Ui.Button(L.Get("保存预设"), Save)); buttons.Children.Add(Ui.Button(L.Get("复制"), () => { editingId = ""; name.Text += L.Get(" 副本"); Save(); }));
        buttons.Children.Add(Ui.Button(L.Get("删除"), () => { if (editingId != "") { controller.Profiles.RemovePreset(editingId); editingId = ""; Refresh(); } })); buttons.Children.Add(Ui.Button(L.Get("截取"), Capture, true));
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is CapturePreset preset) Load(preset); }; Refresh();
    }
    private static TextBox Field(string value = "") => new TextBox { Text = value, Margin = new Thickness(4), Padding = new Thickness(5) };
    private void Refresh() { list.ItemsSource = controller.Profiles.Snapshot.Presets; if (list.Items.Count > 0) list.SelectedIndex = 0; }
    private void Load(CapturePreset preset) { editingId = preset.Id; name.Text = preset.Name; var values = new[] { preset.X, preset.Y, preset.Width, preset.Height }; for (var i = 0; i < values.Length; i++) geometry[i].Text = values[i].ToString(CultureInfo.InvariantCulture); delay.Text = preset.DelaySeconds.ToString(); fixedPosition.IsChecked = preset.FixedPosition; cursor.IsChecked = preset.Cursor; shape.SelectedIndex = (int)preset.Shape; }
    private CapturePreset Value()
    {
        int Parse(TextBox input) => int.TryParse(input.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : throw new ArgumentException(L.Get("坐标、尺寸和延时请输入整数。"));
        var values = geometry.Select(Parse).ToArray();
        if (values[2] < 1 || values[3] < 1) throw new ArgumentException(L.Get("宽高必须大于零。"));
        return new CapturePreset { Id = editingId, Name = name.Text.Trim(), X = values[0], Y = values[1], Width = values[2], Height = values[3], DelaySeconds = Parse(delay), FixedPosition = fixedPosition.IsChecked == true, Cursor = cursor.IsChecked == true, Shape = (CaptureShape)shape.SelectedIndex };
    }
    private void Save() { try { controller.Profiles.SavePreset(Value()); Refresh(); } catch (Exception error) { Ui.Error(this, error, L.Get("保存预设")); } }
    private void Capture() { try { var preset = Value(); controller.Profiles.SavePreset(preset); Close(); controller.UseCapturePreset(preset); } catch (Exception error) { Ui.Error(this, error, L.Get("预设截图")); } }
}

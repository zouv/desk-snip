using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DeskSnip.Core;

namespace DeskSnip.App;

internal sealed class PinManagerWindow : ProductWindow
{
    private readonly AppController controller;
    private readonly ComboBox groups = new ComboBox { MinWidth = 160, Margin = new Thickness(5) };
    private readonly ComboBox targetGroup = new ComboBox { MinWidth = 150, Margin = new Thickness(5) };
    private readonly ListBox list = new ListBox { SelectionMode = SelectionMode.Extended, Margin = new Thickness(0, 12, 0, 12) };
    private readonly CheckBox closed = new CheckBox { Content = L.Get("显示已关闭记录"), Margin = new Thickness(5, 10, 5, 5) };
    private readonly CheckBox restore = new CheckBox { Content = L.Get("启动恢复活动钉图"), Margin = new Thickness(5, 10, 5, 5) };
    private readonly TextBox limit = new TextBox { Width = 60, Margin = new Thickness(5), Padding = new Thickness(5) };
    private bool updating;
    internal PinManagerWindow(AppController controller)
    {
        this.controller = controller; Title = L.Get("DeskSnip · 钉图管理"); Width = 850; Height = 650; MinWidth = 740; MinHeight = 500;
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var header = new WrapPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        header.Children.Add(groups); header.Children.Add(Ui.Button(L.Get("新建分组"), () => Prompt(L.Get("分组名称"), "", value => controller.Workspace.AddGroup(value))));
        header.Children.Add(Ui.Button(L.Get("编辑分组"), EditGroup)); header.Children.Add(Ui.Button(L.Get("删除组并保留钉图"), () => Change(() => controller.DeletePinGroup(GroupId, false))));
        groups.SelectionChanged += (_, _) => { if (!updating && groups.SelectedItem is PinGroup group) Change(() => controller.ChoosePinGroup(group.Id)); };
        var options = new WrapPanel(); DockPanel.SetDock(options, Dock.Top); root.Children.Add(options);
        restore.IsChecked = controller.Workspace.RestoreOnStart; limit.Text = controller.Workspace.Snapshot.ClosedLimit.ToString();
        options.Children.Add(closed); options.Children.Add(restore); options.Children.Add(Ui.Text(L.Get("已关闭上限"), 12)); options.Children.Add(limit);
        options.Children.Add(Ui.Button(L.Get("保存偏好"), () => Change(() => { if (!int.TryParse(limit.Text, out var count)) throw new ArgumentException(L.Get("上限请输入整数。")); controller.ConfigurePins(restore.IsChecked == true, count); })));
        closed.Checked += (_, _) => Reload(); closed.Unchecked += (_, _) => Reload();
        var actions = new WrapPanel(); DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        actions.Children.Add(Ui.Button(L.Get("隐藏所选"), () => Change(() => controller.HidePinRecords(Selected, true))));
        actions.Children.Add(Ui.Button(L.Get("显示所选"), () => Change(() => controller.HidePinRecords(Selected, false))));
        actions.Children.Add(Ui.Button(L.Get("关闭所选"), () => Change(() => controller.ClosePinRecords(Selected))));
        actions.Children.Add(Ui.Button(L.Get("恢复所选"), () => Change(() => { foreach (var id in Selected) controller.RestorePinRecord(id); })));
        actions.Children.Add(Ui.Button(L.Get("移回屏幕"), () => Change(() => controller.RescuePinRecords(Selected))));
        actions.Children.Add(Ui.Button(L.Get("批量保存"), () => Prompt(L.Get("保存到目录"), "", value => controller.SavePinRecords(Selected, value))));
        actions.Children.Add(Ui.Button(L.Get("改标题"), () => { var record = list.SelectedItem as PinRecord; if (record != null) Prompt(L.Get("钉图标题"), record.Title, value => controller.RenamePinRecord(record.Id, value)); }));
        actions.Children.Add(Ui.Button(L.Get("左移"), () => Change(() => controller.MovePinRecords(Selected, -10, 0))));
        actions.Children.Add(Ui.Button(L.Get("右移"), () => Change(() => controller.MovePinRecords(Selected, 10, 0))));
        actions.Children.Add(Ui.Button(L.Get("上移"), () => Change(() => controller.MovePinRecords(Selected, 0, -10))));
        actions.Children.Add(Ui.Button(L.Get("下移"), () => Change(() => controller.MovePinRecords(Selected, 0, 10))));
        actions.Children.Add(targetGroup); actions.Children.Add(Ui.Button(L.Get("移入分组"), () => Change(() => { if (targetGroup.SelectedItem is PinGroup group) controller.MovePinGroup(Selected, group.Id); })));
        actions.Children.Add(Ui.Button(L.Get("销毁所选"), () => { if (MessageBox.Show(this, L.Get("永久删除所选钉图和恢复记录？"), L.Get("销毁钉图"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) Change(() => controller.DestroyPinRecords(Selected)); }));
        root.Children.Add(list); Reload();
    }
    private string GroupId => (groups.SelectedItem as PinGroup)?.Id ?? controller.Workspace.CurrentGroupId;
    private string[] Selected => list.SelectedItems.Cast<PinRecord>().Select(p => p.Id).ToArray();
    private void Reload()
    {
        updating = true;
        try
        {
            var destination = (targetGroup.SelectedItem as PinGroup)?.Id;
            groups.ItemsSource = controller.Workspace.Groups; groups.SelectedItem = controller.Workspace.Groups.FirstOrDefault(g => g.Id == controller.Workspace.CurrentGroupId);
            groups.SelectedIndex = controller.Workspace.Groups.ToList().FindIndex(g => g.Id == controller.Workspace.CurrentGroupId);
            targetGroup.ItemsSource = controller.Workspace.Groups; targetGroup.SelectedIndex = Math.Max(0, controller.Workspace.Groups.ToList().FindIndex(g => g.Id == destination));
            list.ItemsSource = controller.Workspace.Records.Where(p => p.GroupId == controller.Workspace.CurrentGroupId && (p.Active || closed.IsChecked == true)).OrderByDescending(p => p.Active).ThenByDescending(p => p.CreatedUtc).ToArray();
        }
        finally { updating = false; }
    }
    private void Change(Action action) { try { action(); Reload(); } catch (Exception e) { Ui.Error(this, e, L.Get("钉图管理")); } }
    private void Prompt(string title, string value, Action<string> apply)
    {
        var prompt = new TextPrompt(value) { Title = title, Owner = this }; if (prompt.ShowDialog() == true) Change(() => apply(prompt.Value.Trim()));
    }
    private void EditGroup()
    {
        if (groups.SelectedItem is PinGroup group) Prompt(L.Get("分组名称"), group.Name, name => Prompt(L.Get("分组颜色 #RRGGBB"), group.Color, color => controller.Workspace.EditGroup(group.Id, name, color)));
    }
}

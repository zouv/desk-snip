using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using DeskSnip.App;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using Xunit;

namespace DeskSnip.Tests;

public class SettingsInputUiTests
{
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var item in LogicalTreeHelper.GetChildren(root)) if (item is DependencyObject child) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static Control Input(SettingsWindow window, string name) => Descendants((DependencyObject)window.Content).OfType<Control>().Single(c => AutomationProperties.GetAutomationId(c) == name);
    private static AppSettings Read(SettingsWindow window) => (AppSettings)typeof(SettingsWindow).GetMethod("ReadValues", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
    private static void Restore(SettingsWindow window) => typeof(SettingsWindow).GetMethod("RestoreCurrentPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);

    [Fact]
    public void ShortcutCaptureUsesEscAndBackspaceToCancelAndDeleteToClear()
    {
        Assert.Equal(ShortcutEditAction.Cancel, ShortcutEditRules.Resolve(Key.Escape, ModifierKeys.Control, true, out _));
        Assert.Equal(ShortcutEditAction.Cancel, ShortcutEditRules.Resolve(Key.Back, ModifierKeys.None, true, out _));
        Assert.Equal(ShortcutEditAction.Clear, ShortcutEditRules.Resolve(Key.Delete, ModifierKeys.None, true, out _));
        Assert.Equal(ShortcutEditAction.Ignore, ShortcutEditRules.Resolve(Key.LeftCtrl, ModifierKeys.Control, true, out _));
        Assert.Equal(ShortcutEditAction.Set, ShortcutEditRules.Resolve(Key.F6, ModifierKeys.Control | ModifierKeys.Alt, true, out var value));
        Assert.Equal("Ctrl+Alt+F6", value);
        Assert.Equal(ShortcutEditAction.Set, ShortcutEditRules.Resolve(Key.D1, ModifierKeys.Control, false, out var numbered));
        Assert.Equal("Ctrl+1", numbered);
    }

    [Fact]
    public void AnnotationDraftAndRestoreDoNotChangeOtherInputPages() => ImagingTests.Sta(() =>
    {
        var original = new AppSettings { ShortcutIgnoredProcesses = "example.exe", GlobalMouseBindings = new List<DeskSnip.Core.MouseBinding> { new DeskSnip.Core.MouseBinding { Action = "Copy", Modifiers = "Ctrl", Gesture = "LeftDrag" } } };
        var window = new SettingsWindow(original, _ => { });
        try
        {
            window.SelectPage("annotation"); ((CheckBox)Input(window, nameof(AppSettings.MarkSharedColor))).IsChecked = false;
            ((ComboBox)Input(window, nameof(AppSettings.MarkPencilShiftMode))).SelectedValue = "Snap45"; ((TextBox)Input(window, nameof(AppSettings.MarkQuickSettingsPadding))).Text = "18";
            ((CheckBox)Input(window, nameof(AppSettings.MarkSerialDefaultColor))).IsChecked = false; ((TextBox)Input(window, nameof(AppSettings.MarkSerialBackground))).Text = "#112233";
            window.SelectPage("shortcuts"); window.SelectPage("annotation"); var draft = Read(window);
            Assert.False(draft.MarkSharedColor); Assert.Equal("Snap45", draft.MarkPencilShiftMode); Assert.Equal(18, draft.MarkQuickSettingsPadding); Assert.Equal("#112233", draft.MarkSerialBackground);
            Assert.True(original.MarkSharedColor); Restore(window); draft = Read(window); Assert.True(draft.MarkSharedColor); Assert.Equal(0, draft.MarkQuickSettingsPadding);
            Assert.Equal("example.exe", draft.ShortcutIgnoredProcesses); Assert.Equal("Ctrl", Assert.Single(draft.GlobalMouseBindings!).Modifiers);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void ClearingAndRerenderingBaseActionPreservesItsDraftBinding() => ImagingTests.Sta(() =>
    {
        var window = new SettingsWindow(new AppSettings(), _ => { });
        try
        {
            window.SelectPage("shortcuts");
            var clear = Descendants((DependencyObject)window.Content).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "清空快捷键：截图"); clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal("", Read(window).CaptureHotkey);
            typeof(SettingsWindow).GetMethod("RenderActions", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null); var key = (TextBox)Input(window, "Action_Capture"); Assert.Equal("", key.Text);
            key.SetCurrentValue(TextBox.TextProperty, "Ctrl+Alt+F6"); key.GetBindingExpression(TextBox.TextProperty)!.UpdateSource(); Assert.Equal("Ctrl+Alt+F6", Read(window).CaptureHotkey);
            Restore(window); Assert.Equal(new AppSettings().CaptureHotkey, Read(window).CaptureHotkey); Assert.Equal(new AppSettings().CaptureHotkey, ((TextBox)Input(window, "Action_Capture")).Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void BuiltinEditingAndClearingActuallyChangeRuntimeKeys() => ImagingTests.Sta(() =>
    {
        var window = new SettingsWindow(new AppSettings(), _ => { });
        try
        {
            window.SelectPage("builtin"); var copy = (TextBox)Input(window, "Builtin_Common_Copy"); copy.Text = "Ctrl+F6"; var draft = Read(window);
            Assert.Equal("Copy", ShortcutKeys.Match(draft, "Common", Key.F6, ModifierKeys.Control)); Assert.Null(ShortcutKeys.Match(draft, "Common", Key.C, ModifierKeys.Control));
            copy.Text = ""; draft = Read(window); Assert.Null(ShortcutKeys.Match(draft, "Common", Key.F6, ModifierKeys.Control)); Assert.Null(ShortcutKeys.Match(draft, "Common", Key.C, ModifierKeys.Control));
            Restore(window); draft = Read(window); Assert.Equal("Copy", ShortcutKeys.Match(draft, "Common", Key.C, ModifierKeys.Control));
            Assert.Equal("Table", ShortcutKeys.Match(draft, "Common", Key.Q, ModifierKeys.Shift));
            var tabs = Descendants((DependencyObject)window.Content).OfType<TabControl>().Single(); ((TabItem)tabs.Items[5]).IsSelected = true;
            var unavailable = (TextBox)Input(window, "Builtin_DynamicPin_Play"); Assert.False(unavailable.IsEnabled);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void MouseDraftRoundTripsAndConflictsBlockSaving() => ImagingTests.Sta(() =>
    {
        var window = new SettingsWindow(new AppSettings(), _ => { });
        try
        {
            window.SelectPage("mouse"); ((ComboBox)Input(window, "PinMouse_CopyGesture")).SelectedValue = "X1Click";
            ((ComboBox)Input(window, "PinMouse_OpacityModifiers")).SelectedValue = "Ctrl+Alt";
            window.SelectPage("globalmouse"); ((ComboBox)Input(window, "GlobalMouse_PinModifiers")).SelectedValue = "Ctrl"; ((ComboBox)Input(window, "GlobalMouse_PinGesture")).SelectedValue = "LeftDrag";
            var draft = Read(window); Assert.Equal("Copy", InputBindingRules.MouseAction(draft.PinMouseBindings!, "", "X1Click")); Assert.Equal("Opacity", InputBindingRules.MouseAction(draft.PinMouseBindings!, "Alt+Ctrl", "Wheel"));
            Assert.Equal("Pin", InputBindingRules.MouseAction(draft.GlobalMouseBindings!, "Ctrl", "LeftDrag"));
            window.SelectPage("mouse"); ((ComboBox)Input(window, "PinMouse_CloseGesture")).SelectedValue = "X1Click"; Assert.Throws<TargetInvocationException>(() => Read(window));
            Restore(window); draft = Read(window); Assert.Equal("Pin", InputBindingRules.MouseAction(draft.GlobalMouseBindings!, "Ctrl", "LeftDrag")); Assert.Equal("Scale", InputBindingRules.MouseAction(draft.PinMouseBindings!, "", "Wheel"));
            window.SelectPage("globalmouse"); Restore(window); draft = Read(window); Assert.DoesNotContain(draft.GlobalMouseBindings!, b => b.Gesture != "None");
        }
        finally { window.Close(); }
    });

    [Fact]
    public void SerialDefaultColorDisablesBothColorInputAndPicker() => ImagingTests.Sta(() =>
    {
        var window = new SettingsWindow(new AppSettings(), _ => { });
        try
        {
            window.SelectPage("annotation"); var toggle = (CheckBox)Input(window, nameof(AppSettings.MarkSerialDefaultColor)); var color = (TextBox)Input(window, nameof(AppSettings.MarkSerialBackground));
            var picker = Descendants((DependencyObject)window.Content).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "选择颜色"); Assert.False(color.IsEnabled); Assert.False(picker.IsEnabled);
            toggle.IsChecked = false; Assert.True(color.IsEnabled); Assert.True(picker.IsEnabled); color.Text = "#102030"; toggle.IsChecked = true;
            Assert.Equal("#102030", Read(window).MarkSerialBackground); Assert.False(picker.IsEnabled);
        }
        finally { window.Close(); }
    });
}

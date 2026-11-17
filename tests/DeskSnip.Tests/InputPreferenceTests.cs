using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Input;
using DeskSnip.App.Windows;
using DeskSnip.Core;
using Xunit;
using MouseBinding = DeskSnip.Core.MouseBinding;

namespace DeskSnip.Tests;

public class InputPreferenceTests
{
    [Fact]
    public void OldSettingsKeepAnnotationDefaultsAndLegacyMouseActions()
    {
        var json = "{\"PinMiddleAction\":\"Lock\",\"PinDoubleClickAction\":\"Hide\",\"PinWheelAction\":\"Opacity\",\"PinControlWheelAction\":\"Zoom\"}";
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
        {
            var settings = (AppSettings)new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(stream)!;
            Assert.True(settings.MarkSharedColor); Assert.True(settings.MarkSecondaryEditing); Assert.True(settings.MarkSmoothPencil);
            Assert.Equal("CrossAndOutline", settings.MarkPencilCursor); Assert.Equal(50, settings.MarkPencilOutlineMinWidth);
            Assert.True(settings.BuiltinMergeNumberKeys); Assert.Null(settings.BuiltinShortcuts); Assert.Null(settings.GlobalMouseBindings);
            var mouse = InputBindingCatalog.DefaultPin(settings); InputBindingRules.ValidateMouse(mouse, false);
            Assert.Equal("Lock", InputBindingRules.MouseAction(mouse, "", "MiddleClick"));
            Assert.Equal("Hide", InputBindingRules.MouseAction(mouse, "", "LeftDoubleClick"));
            Assert.Equal("Opacity", InputBindingRules.MouseAction(mouse, "", "Wheel")); Assert.Equal("Scale", InputBindingRules.MouseAction(mouse, "Ctrl", "Wheel"));
        }
    }

    [Fact]
    public void PinSelectionBorderShortcutDefaultsToF()
    {
        var settings = new AppSettings();
        Assert.Equal("F", InputBindingRules.Shortcut(settings, "Pin", "Border"));
        Assert.Contains(InputBindingCatalog.Actions("Pin"), action => action.Name == "Border" && action.Supported);
        settings.Validate();
    }

    [Fact]
    public void InputPreferencesCloneAndPortableImportKeepIndependentLists()
    {
        var settings = new AppSettings
        {
            MarkSharedColor = false, MarkPencilShiftMode = "Snap45", MarkSerialStartAtZero = true,
            BuiltinShortcuts = new List<ShortcutBinding> { new ShortcutBinding { Context = "Common", Action = "Copy", Hotkey = "Ctrl+F6" } },
            PinMouseBindings = new List<MouseBinding> { new MouseBinding { Action = "Copy", Gesture = "X1Click" } },
            GlobalMouseBindings = new List<MouseBinding> { new MouseBinding { Action = "Copy", Modifiers = "Ctrl", Gesture = "LeftDrag" } },
            HiddenDefaultActions = new List<string> { "ClosePins" }, ShortcutIgnoredProcesses = "example.exe", GlobalMouseIgnoredProcesses = "example.exe"
        };
        var clone = settings.Clone(); clone.BuiltinShortcuts![0].Hotkey = "Ctrl+F7"; clone.PinMouseBindings![0].Gesture = "X2Click"; clone.GlobalMouseBindings![0].Modifiers = "Alt"; clone.HiddenDefaultActions!.Clear();
        Assert.Equal("Ctrl+F6", settings.BuiltinShortcuts![0].Hotkey); Assert.Equal("X1Click", settings.PinMouseBindings![0].Gesture); Assert.Equal("Ctrl", settings.GlobalMouseBindings![0].Modifiers); Assert.Single(settings.HiddenDefaultActions!);
        var document = PreferenceTransfer.Export(settings); Assert.DoesNotContain(nameof(AppSettings.ShortcutIgnoredProcesses), document.Values.Keys); Assert.DoesNotContain(nameof(AppSettings.GlobalMouseIgnoredProcesses), document.Values.Keys);
        using (var stream = new MemoryStream())
        {
            var serializer = new DataContractJsonSerializer(typeof(PreferenceDocument), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }); serializer.WriteObject(stream, document); stream.Position = 0;
            var restored = (PreferenceDocument)serializer.ReadObject(stream)!; var imported = PreferenceTransfer.Import(new AppSettings(), restored);
            Assert.False(imported.MarkSharedColor); Assert.True(imported.MarkSerialStartAtZero); Assert.Equal("Snap45", imported.MarkPencilShiftMode);
            Assert.Equal("Ctrl+F6", imported.BuiltinShortcuts![0].Hotkey); Assert.Equal("X1Click", imported.PinMouseBindings![0].Gesture); Assert.Equal("Ctrl", imported.GlobalMouseBindings![0].Modifiers);
            imported.BuiltinShortcuts[0].Hotkey = "Ctrl+F8"; Assert.Equal("Ctrl+F6", document.BuiltinShortcuts![0].Hotkey);
        }
    }

    [Theory]
    [InlineData("Ctrl+Return", "Ctrl+Enter")]
    [InlineData("Alt+Prior", "Alt+PageUp")]
    [InlineData("Ctrl+OemSemicolon", "Ctrl+Oem1")]
    public void KeyAliasesCannotCreateAmbiguousBindings(string first, string second)
    {
        var settings = new AppSettings { BuiltinShortcuts = new List<ShortcutBinding> { new ShortcutBinding { Action = "Copy", Hotkey = first }, new ShortcutBinding { Action = "Save", Hotkey = second } } };
        Assert.Throws<ArgumentException>(settings.Validate);
    }

    [Fact]
    public void KeypadMergeControlsConflictDetectionAndRuntimeMatching()
    {
        var settings = new AppSettings { BuiltinShortcuts = new List<ShortcutBinding> { new ShortcutBinding { Action = "Copy", Hotkey = "Shift+NumPad1" } } };
        Assert.Throws<ArgumentException>(settings.Validate); settings.BuiltinMergeNumberKeys = false; settings.Validate();
        Assert.Equal("Copy", ShortcutKeys.Match(settings, "Common", Key.NumPad1, ModifierKeys.Shift)); Assert.Equal("ToolRectangle", ShortcutKeys.Match(settings, "Common", Key.D1, ModifierKeys.Shift));
        settings.BuiltinShortcuts.Clear(); settings.BuiltinMergeNumberKeys = true;
        Assert.Equal("ToolRectangle", ShortcutKeys.Match(settings, "Common", Key.NumPad1, ModifierKeys.Shift)); Assert.Null(ShortcutKeys.Match(settings, "Common", Key.ImeProcessed, ModifierKeys.None));
    }

    [Theory]
    [InlineData("Ctrl+UnknownKey")]
    [InlineData("Ctrl+Ctrl+C")]
    [InlineData("Ctrl+None")]
    [InlineData("Ctrl+")]
    public void InvalidShortcutsCannotEnterPortablePreferences(string hotkey)
    {
        var document = new PreferenceDocument { BuiltinShortcuts = new List<ShortcutBinding> { new ShortcutBinding { Action = "Copy", Hotkey = hotkey } } }; var current = new AppSettings();
        Assert.Throws<ArgumentException>(() => PreferenceTransfer.Import(current, document)); Assert.Null(current.BuiltinShortcuts);
    }

    [Theory]
    [InlineData("Escape")]
    [InlineData("Shift+D1")]
    [InlineData("Ctrl+NumPad2")]
    [InlineData("Shift+OemPlus")]
    [InlineData("Ctrl+Add")]
    public void FriendlyKeyLabelsRoundTripWithoutChangingStoredKeys(string value)
        => Assert.Equal(value, ShortcutKeys.FromDisplay(ShortcutKeys.Display(value)));

    [Fact]
    public void ConflictingAndUnsupportedMouseBindingsAreRejected()
    {
        var repeated = new List<MouseBinding> { new MouseBinding { Action = "Copy", Gesture = "LeftClick", Modifiers = "Ctrl" }, new MouseBinding { Action = "Close", Gesture = "LeftClick", Modifiers = "Ctrl" } };
        Assert.Throws<ArgumentException>(() => InputBindingRules.ValidateMouse(repeated, false));
        Assert.Throws<ArgumentException>(() => InputBindingRules.ValidateMouse(new List<MouseBinding> { new MouseBinding { Action = "Thumbnail", Gesture = "RightDrag" } }, false));
        Assert.Throws<ArgumentException>(() => InputBindingRules.ValidateMouse(new List<MouseBinding> { new MouseBinding { Action = "Scale", Gesture = "LeftClick" } }, false));
        Assert.Throws<ArgumentException>(() => InputBindingRules.ValidateMouse(new List<MouseBinding> { new MouseBinding { Action = "Copy", Gesture = "LeftDrag" } }, true));
        Assert.Throws<ArgumentException>(() => InputBindingRules.ValidateMouse(new List<MouseBinding> { new MouseBinding { Action = "Copy", Gesture = "LeftClick", Modifiers = "Ctrl" } }, true));
    }

    [Fact]
    public void GlobalDragUsesPhysicalCoordinatesAndClickCancelOrIgnoredProcessDoNotCapture()
    {
        var gesture = new MouseGestureSession(new[] { new MouseBinding { Action = "Pin", Modifiers = "Ctrl+Alt", Gesture = "LeftDrag" } });
        Assert.False(gesture.Begin("Left", "Alt+Ctrl", new PixelPoint(-100, -50), false)); Assert.False(gesture.Active);
        Assert.True(gesture.Begin("Left", "Alt+Ctrl", new PixelPoint(-100, -50), true)); gesture.Move(new PixelPoint(-130, -80));
        Assert.Null(gesture.Finish("Right", new PixelPoint(-130, -80))); Assert.True(gesture.Active);
        var output = gesture.Finish("Left", new PixelPoint(-130, -80)); Assert.True(output.HasValue); Assert.Equal("Pin", output!.Value.Action); Assert.Equal(new PixelRect(-130, -80, 30, 30), output.Value.Region); Assert.False(gesture.Active);
        gesture.Begin("Left", "Ctrl+Alt", new PixelPoint(10, 10), true); Assert.Null(gesture.Finish("Left", new PixelPoint(11, 11)));
        gesture.Begin("Left", "Ctrl+Alt", new PixelPoint(10, 10), true); gesture.Cancel(); Assert.Null(gesture.Finish("Left", new PixelPoint(100, 100)));
        Assert.True(InputBindingRules.Ignored("example.exe\nEXAMPLE.exe", "example")); Assert.Throws<ArgumentException>(() => InputBindingRules.ProcessNames("C:\\path\\example.exe"));
    }

    [Fact]
    public void PinClickAndContentDragDoNotTriggerEachOther()
    {
        var gesture = new PinPointerGesture(); gesture.Begin("Left", new PixelPoint(10, 10), "MultiSelect", "DragContent");
        Assert.Null(gesture.Move(new PixelPoint(12, 12))); Assert.Equal("MultiSelect", gesture.Finish("Left", new PixelPoint(12, 12)));
        gesture.Begin("Left", new PixelPoint(10, 10), "MultiSelect", "DragContent"); Assert.Equal("DragContent", gesture.Move(new PixelPoint(18, 10))); Assert.Null(gesture.Move(new PixelPoint(20, 10))); Assert.Equal("DragContent", gesture.Finish("Left", new PixelPoint(20, 10)));
        gesture.Begin("Left", new PixelPoint(10, 10), "Copy", "Move"); gesture.Cancel(); Assert.Null(gesture.Finish("Left", new PixelPoint(20, 10)));
    }

    [Fact]
    public void LongPressDragDistinguishesClickHoldAndMovement()
    {
        var gesture = new LongPressDragGesture(); gesture.Begin(new PixelPoint(10, 10));
        Assert.False(gesture.Update(new PixelPoint(40, 40), 199)); Assert.True(gesture.Finish()); Assert.False(gesture.Active);
        gesture.Begin(new PixelPoint(10, 10)); Assert.False(gesture.Update(new PixelPoint(10, 10), 200)); Assert.True(gesture.LongPressed); Assert.False(gesture.Finish());
        gesture.Begin(new PixelPoint(10, 10)); Assert.False(gesture.Update(new PixelPoint(13, 10), 250)); Assert.True(gesture.Update(new PixelPoint(14, 10), 250)); Assert.True(gesture.Dragging);
        Assert.True(gesture.Update(new PixelPoint(10, 10), 300)); Assert.False(gesture.Finish()); Assert.False(gesture.Dragging);
    }
}

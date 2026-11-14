using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Automation;
using System.Windows.Markup;
using System.Windows.Media;
using DeskSnip.Core;

namespace DeskSnip.App;

internal sealed class InterfaceLanguageState : INotifyPropertyChanged
{
    internal static InterfaceLanguageState Current { get; } = new InterfaceLanguageState();
    internal CultureInfo SystemCulture { get; } = CultureInfo.CurrentUICulture;
    internal string Preference { get; private set; } = "zh-CN";
    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("zh-CN");
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void Apply(string preference)
    {
        var resolved = InterfaceLanguageRules.Resolve(preference, SystemCulture);
        Preference = preference; Culture = CultureInfo.GetCultureInfo(resolved);
        CultureInfo.CurrentUICulture = Culture; CultureInfo.DefaultThreadCurrentUICulture = Culture;
        // Only this application's UI thread; no Windows language preference is changed.
        SetThreadUILanguage((ushort)Culture.LCID);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
    }
    [DllImport("kernel32.dll")] private static extern ushort SetThreadUILanguage(ushort language);
}

internal static class L
{
    private static readonly HashSet<DependencyProperty> DisplayProperties = new HashSet<DependencyProperty>
    {
        Window.TitleProperty, TextBlock.TextProperty, Run.TextProperty, ContentControl.ContentProperty,
        HeaderedContentControl.HeaderProperty, HeaderedItemsControl.HeaderProperty, DataGridColumn.HeaderProperty,
        FrameworkElement.ToolTipProperty, MenuItem.InputGestureTextProperty, AutomationProperties.NameProperty, AutomationProperties.HelpTextProperty
    };
    internal static string Get(string source) => TextCatalog.Get(source);
    internal static string Format(string source, params object?[] arguments) => TextCatalog.Format(source, arguments);
    internal static string Join(object? left, object? right) => TextCatalog.Join(left, right);
    internal static void Apply(string preference) => InterfaceLanguageState.Current.Apply(preference);
    internal static void BindReadOnlyLabel(TextBox input, string value)
    {
        input.IsReadOnly = true;
        input.SetBinding(TextBox.TextProperty, LabelBinding(new Binding { Source = Get(value) }));
    }
    private static MultiBinding LabelBinding(BindingBase value)
    {
        var binding = new MultiBinding { Mode = BindingMode.OneWay, Converter = new ProductLabelConverter() };
        binding.Bindings.Add(value);
        binding.Bindings.Add(new Binding(nameof(InterfaceLanguageState.Culture)) { Source = InterfaceLanguageState.Current });
        return binding;
    }

    internal static void Refresh(DependencyObject root, CultureInfo? culture = null)
    {
        var target = culture ?? CultureInfo.CurrentUICulture;
        var visited = new HashSet<DependencyObject>();
        void Visit(DependencyObject element)
        {
            if (!visited.Add(element)) return;
            var values = element.GetLocalValueEnumerator();
            var changes = new List<(DependencyProperty Property, string Text)>();
            while (values.MoveNext())
            {
                var entry = values.Current;
                if (entry.Property.ReadOnly || !DisplayProperties.Contains(entry.Property)) continue;
                var effective = element.GetValue(entry.Property);
                if (effective is string text && TextCatalog.IsProductText(text)) changes.Add((entry.Property, TextCatalog.Refresh(text, target)));
            }
            foreach (var change in changes) element.SetCurrentValue(change.Property, change.Text);
            if (element is ItemsControl items && !(items is DataGrid) && items.ItemTemplate == null && items.DisplayMemberPath.Length == 0 && items.ItemsSource != null && !(items is ComboBox editable && editable.IsEditable))
            {
                if (items.Items.Cast<object>().Any(item => TextCatalog.IsProductText(item.ToString() ?? "")))
                {
                    // Keep the source objects and selection stable. Refreshing the collection can
                    // trigger OCR/annotation handlers and overwrite edits during a language change.
                    var binding = LabelBinding(new Binding());
                    var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, binding);
                    items.ItemTemplate = new DataTemplate { VisualTree = label };
                }
            }
            if (element is DataGrid grid) foreach (var column in grid.Columns) Visit(column);
            foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>().ToArray()) Visit(child);
            if (element is Visual || element is System.Windows.Media.Media3D.Visual3D)
                for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++) Visit(VisualTreeHelper.GetChild(element, index));
            if (element is FrameworkElement framework)
            {
                if (framework.ContextMenu != null) Visit(framework.ContextMenu);
                if (framework.ToolTip is DependencyObject tip) Visit(tip);
            }
        }
        Visit(root);
    }

    private sealed class ProductLabelConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var text = values[0]?.ToString() ?? "";
            return TextCatalog.Refresh(text, values[1] as CultureInfo ?? culture);
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}

/// <summary>Markup for product-owned labels. Data bindings and editable content remain untouched.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension : MarkupExtension
{
    public TextExtension() { }
    public TextExtension(string source) { Source = source; }
    public string Source { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider) => L.Get(Source);
}

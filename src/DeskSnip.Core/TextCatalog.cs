using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace DeskSnip.Core;

/// <summary>Local product text. Source text and user-supplied format arguments stay separate.</summary>
public static class TextCatalog
{
    private sealed class Phrase
    {
        internal string Source { get; }
        internal object?[]? Arguments { get; }
        internal Phrase(string source, object?[]? arguments = null) { Source = source; Arguments = arguments; }
        internal string Render(CultureInfo culture)
        {
            var template = culture.TwoLetterISOLanguageName == "en" && English.TryGetValue(Source, out var value) ? value : Source;
            var text = Arguments == null ? template : string.Format(culture, template, Arguments.Select(argument => argument is string label ? Refresh(label, culture) : argument).ToArray());
            var result = new string(text.ToCharArray());
            if (result.Length == 0) return result;
            phrases.Add(result, this);
            return result;
        }
    }
    private static readonly ConditionalWeakTable<string, Phrase> phrases = new ConditionalWeakTable<string, Phrase>();
    private static readonly Lazy<IReadOnlyDictionary<string, string>> english = new Lazy<IReadOnlyDictionary<string, string>>(Load);
    public static IReadOnlyDictionary<string, string> English => english.Value;

    private static IReadOnlyDictionary<string, string> Load()
    {
        using (var stream = typeof(TextCatalog).Assembly.GetManifestResourceStream("DeskSnip.Core.Resources.English.xml"))
        {
            if (stream == null) throw new InvalidOperationException("Product language resources are missing.");
            return XDocument.Load(stream, LoadOptions.PreserveWhitespace).Root!.Elements("text")
                .ToDictionary(element => element.Element("source")!.Value, element => element.Element("translation")!.Value, StringComparer.Ordinal);
        }
    }

    public static string Get(string source, CultureInfo? culture = null)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        var target = culture ?? CultureInfo.CurrentUICulture;
        if (phrases.TryGetValue(source, out var existing)) return existing.Render(target);
        return English.ContainsKey(source) ? new Phrase(source).Render(target) : source;
    }

    public static string Format(string source, params object?[] arguments) => Format(source, CultureInfo.CurrentUICulture, arguments);
    public static string Format(string source, CultureInfo culture, params object?[] arguments)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (arguments == null) throw new ArgumentNullException(nameof(arguments));
        return new Phrase(source, (object?[])arguments.Clone()).Render(culture);
    }
    public static string Join(object? left, object? right) => Format("{0}{1}", left, right);
    public static string Refresh(string value, CultureInfo culture) => phrases.TryGetValue(value, out var phrase) ? phrase.Render(culture) : value;
    public static bool IsProductText(string value) => phrases.TryGetValue(value, out _);
}

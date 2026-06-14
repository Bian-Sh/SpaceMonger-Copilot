using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace SpaceMonger.App.Localization;

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return L.Text(Key);
    }
}

public static class L
{
    private const string LanguageEnvironmentVariable = "SPACEMONGER_LANGUAGE";
    public const string AutoLanguage = "auto";

    private static readonly ResourceManager ResourceManager = new(
        "SpaceMonger.App.Localization.Strings",
        typeof(L).Assembly);

    static L()
    {
        var configuredLanguage = Environment.GetEnvironmentVariable(LanguageEnvironmentVariable);

        SetLanguage(configuredLanguage);
    }

    public static string CurrentLanguageName => CultureInfo.CurrentUICulture.Name;

    public static void SetLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language) || string.Equals(language, AutoLanguage, StringComparison.OrdinalIgnoreCase))
            return;

        var culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public static string Text(string key)
    {
        return ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? $"!{key}!";
    }

    public static string Format(string key, params object?[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Text(key), args);
    }
}

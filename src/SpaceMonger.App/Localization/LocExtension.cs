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

    private static readonly ResourceManager ResourceManager = new(
        "SpaceMonger.App.Localization.Strings",
        typeof(L).Assembly);

    static L()
    {
        var configuredLanguage = Environment.GetEnvironmentVariable(LanguageEnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(configuredLanguage))
        {
            var culture = CultureInfo.GetCultureInfo(configuredLanguage);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
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

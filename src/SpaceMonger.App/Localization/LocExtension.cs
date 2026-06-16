using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows;
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
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget target &&
            target.TargetObject is DependencyObject dependencyObject &&
            target.TargetProperty is DependencyProperty dependencyProperty)
        {
            return new LocBinding(Key, dependencyObject, dependencyProperty).Value;
        }

        return L.Text(Key);
    }
}

public sealed class LocBinding : INotifyPropertyChanged
{
    private readonly string _key;
    private readonly DependencyObject _targetObject;
    private readonly DependencyProperty _targetProperty;

    public LocBinding(string key, DependencyObject targetObject, DependencyProperty targetProperty)
    {
        _key = key;
        _targetObject = targetObject;
        _targetProperty = targetProperty;
        L.LanguageChanged += OnLanguageChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Value => L.Text(_key);

    private void OnLanguageChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        _targetObject.Dispatcher.InvokeAsync(() => _targetObject.SetValue(_targetProperty, Value));
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

    public static event Action? LanguageChanged;

    public static void SetLanguage(string? language)
    {
        CultureInfo culture;
        if (string.IsNullOrWhiteSpace(language) || string.Equals(language, AutoLanguage, StringComparison.OrdinalIgnoreCase))
        {
            culture = CultureInfo.InstalledUICulture;
        }
        else
        {
            culture = CultureInfo.GetCultureInfo(language);
        }

        var changed = CultureInfo.CurrentUICulture.Name != culture.Name;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        if (changed)
            LanguageChanged?.Invoke();
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

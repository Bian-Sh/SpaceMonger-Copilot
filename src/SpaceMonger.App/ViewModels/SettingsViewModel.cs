using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceMonger.App.Localization;
using SpaceMonger.App.Services;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models.Theme;
using SpaceMonger.Core.Services.Llm;
using SpaceMonger.Core.Services.Settings;

namespace SpaceMonger.App.ViewModels;

public enum ValidationState
{
    None,
    Validating,
    Valid,
    Invalid
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ILlmClient _llmClient;
    private readonly ThemeManager _themeManager;
    private bool _isLoadingSettings;

    [ObservableProperty]
    private string? _apiKey;

    [ObservableProperty]
    private string? _anthropicBaseUrl;

    [ObservableProperty]
    private string? _analysisModelName;

    [ObservableProperty]
    private string? _chatModelName;

    [ObservableProperty]
    private bool _enableThinking;

    [ObservableProperty]
    private string _language = "zh-CN";

    [ObservableProperty]
    private string? _saveToastMessage;

    [ObservableProperty]
    private bool _isSaveToastVisible;

    [ObservableProperty]
    private ValidationState _validationState = ValidationState.None;

    [ObservableProperty]
    private DeletionMode _selectedDeletionMode = DeletionMode.MoveToRecycleBin;

    [ObservableProperty]
    private bool _isApiKeyValid;

    [ObservableProperty]
    private string? _validationMessage;

    [ObservableProperty]
    private string _validationStatusText = string.Empty;

    // ── Theme Properties ─────────────────────────────────────────

    [ObservableProperty]
    private ThemeProfile _selectedThemeProfile = ThemeProfile.VisionProDark;

    [ObservableProperty]
    private string _accentColorHex = "#FF2562A7";

    [ObservableProperty]
    private Color _accentColor = Color.FromArgb(255, 37, 98, 167);

    partial void OnAccentColorChanged(Color value)
    {
        AccentColorHex = $"#{value.A:X2}{value.R:X2}{value.G:X2}{value.B:X2}";
        if (!_isLoadingSettings)
            SyncAndSaveTheme();
    }

    partial void OnAccentColorHexChanged(string value)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                var hex = value.Trim();
                if (!hex.StartsWith("#")) hex = "#" + hex;
                var c = (Color)ColorConverter.ConvertFromString(hex);
                if (c != AccentColor)
                    AccentColor = c;
            }
        }
        catch { }
    }

    [ObservableProperty]
    private bool _glassEnabled;

    partial void OnGlassEnabledChanged(bool value)
    {
        if (!_isLoadingSettings) SyncAndSaveTheme();
    }

    [ObservableProperty]
    private int _glassBackdropType;

    partial void OnGlassBackdropTypeChanged(int value)
    {
        if (!_isLoadingSettings) SyncAndSaveTheme();
    }

    [ObservableProperty]
    private double _blurRadius;

    partial void OnBlurRadiusChanged(double value)
    {
        if (!_isLoadingSettings) SyncAndSaveTheme();
    }

    [ObservableProperty]
    private double _glassOpacity = 0.85;

    partial void OnGlassOpacityChanged(double value)
    {
        if (!_isLoadingSettings) SyncAndSaveTheme();
    }

    [ObservableProperty]
    private bool _autoTextContrast;

    partial void OnAutoTextContrastChanged(bool value)
    {
        if (!_isLoadingSettings) SyncAndSaveTheme();
    }

    public static IReadOnlyList<ThemeProfile> AvailableThemePresets { get; } = ThemeProfile.BuiltInPresets;

    public SettingsViewModel(ISettingsService settingsService, ILlmClient llmClient, ThemeManager themeManager)
    {
        _settingsService = settingsService;
        _llmClient = llmClient;
        _themeManager = themeManager;
        L.LanguageChanged += RefreshValidationStatusText;

        LoadSettings();
    }

    partial void OnValidationStateChanged(ValidationState value)
    {
        RefreshValidationStatusText();
    }

    partial void OnLanguageChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var language = value.Trim();
        L.SetLanguage(language);

        if (!_isLoadingSettings)
        {
            SaveLanguagePreference(language);
        }
    }

    private void SaveLanguagePreference(string language)
    {
        var settings = _settingsService.LoadSettings();
        settings.Language = language;
        _settingsService.SaveSettings(settings);
    }

    private void RefreshValidationStatusText()
    {
        ValidationStatusText = ValidationState switch
        {
            ValidationState.Validating => L.Text("SettingsValidationValidating"),
            ValidationState.Valid => L.Text("SettingsValidationValid"),
            ValidationState.Invalid => L.Text("SettingsValidationInvalid"),
            _ => string.Empty
        };
    }

    public static IReadOnlyList<LanguageOption> LanguageOptions { get; } =
    [
        new("zh-CN", "简体中文"),
        new("en", "English"),
        new("auto", "Auto")
    ];

    // ── Theme Actions ─────────────────────────────────────────────

    [RelayCommand]
    private void ApplyThemePreset(ThemeProfile? preset)
    {
        if (preset == null) return;
        _themeManager.ApplyTheme(preset);
        LoadThemeFromCurrent();
    }

    [RelayCommand]
    private void ApplyCustomTheme()
    {
        var profile = new ThemeProfile
        {
            Name = "Custom",
            IsBuiltIn = false,
            AccentColor = AccentColorHex,
            GlassEnabled = GlassEnabled,
            GlassBackdropType = GlassBackdropType,
            BlurRadius = BlurRadius,
            GlassOpacity = GlassOpacity,
            AutoTextContrast = AutoTextContrast,
        };
        _themeManager.ApplyTheme(profile);
        LoadThemeFromCurrent();
        SaveWithToast(L.Text("ThemeAppliedToast"));
    }

    private void LoadThemeFromCurrent()
    {
        var p = _themeManager.CurrentProfile;
        SelectedThemeProfile = p;
        AccentColorHex = p.AccentColor;
        GlassEnabled = p.GlassEnabled;
        GlassBackdropType = p.GlassBackdropType;
        BlurRadius = p.BlurRadius;
        GlassOpacity = p.GlassOpacity;
        AutoTextContrast = p.AutoTextContrast;
    }

    // ── Existing ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task ValidateAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            ValidationState = ValidationState.Invalid;
            ValidationMessage = L.Text("ApiKeyEmptyMessage");
            IsApiKeyValid = false;
            return;
        }

        ValidationState = ValidationState.Validating;
        ValidationMessage = null;

        try
        {
            var isValid = await _llmClient.ValidateApiKeyAsync(ApiKey, AnthropicBaseUrl);

            if (isValid)
            {
                ValidationState = ValidationState.Valid;
                IsApiKeyValid = true;
                ValidationMessage = null;
            }
            else
            {
                ValidationState = ValidationState.Invalid;
                IsApiKeyValid = false;
                ValidationMessage = L.Text("ApiKeyInvalidMessage");
            }
        }
        catch (Exception ex)
        {
            ValidationState = ValidationState.Invalid;
            IsApiKeyValid = false;
            ValidationMessage = L.Format("ValidationFailedMessage", ex.Message);
        }
    }

    [RelayCommand]
    public void Save()
    {
        var settings = _settingsService.LoadSettings();

        if (!string.IsNullOrEmpty(ApiKey))
        {
            settings.EncryptedApiKey = _settingsService.EncryptApiKey(ApiKey);
        }

        settings.IsApiKeyValid = IsApiKeyValid;
        settings.DeletionMode = SelectedDeletionMode;
        settings.AnthropicBaseUrl = string.IsNullOrWhiteSpace(AnthropicBaseUrl)
            ? null
            : AnthropicBaseUrl.Trim();
        settings.AnalysisModelName = string.IsNullOrWhiteSpace(AnalysisModelName)
            ? null
            : AnalysisModelName.Trim();
        settings.ChatModelName = string.IsNullOrWhiteSpace(ChatModelName)
            ? null
            : ChatModelName.Trim();
        settings.EnableThinking = EnableThinking;
        settings.Language = string.IsNullOrWhiteSpace(Language) ? "zh-CN" : Language.Trim();
        L.SetLanguage(settings.Language);

        SyncThemeToManager();

        _settingsService.SaveSettings(settings);
    }

    private void SyncThemeToManager()
    {
        var current = _themeManager.CurrentProfile;
        var accentColor = ThemeManager.ParseColor(AccentColorHex);
        var hoverColor = Color.FromArgb(accentColor.A,
            (byte)Math.Min(255, accentColor.R + 20),
            (byte)Math.Min(255, accentColor.G + 20),
            (byte)Math.Min(255, accentColor.B + 20));

        var profile = new ThemeProfile
        {
            Name = "Custom",
            IsBuiltIn = false,
            AccentColor = AccentColorHex,
            AccentHoverColor = ThemeManager.ToHex(hoverColor),
            BackgroundColor = current.BackgroundColor,
            BackgroundAltColor = current.BackgroundAltColor,
            TextPrimaryColor = current.TextPrimaryColor,
            TextSecondaryColor = current.TextSecondaryColor,
            TextTertiaryColor = current.TextTertiaryColor,
            DangerColor = current.DangerColor,
            DangerPressedColor = current.DangerPressedColor,
            SuccessColor = current.SuccessColor,
            WarningColor = current.WarningColor,
            GlassEnabled = GlassEnabled,
            GlassBackdropType = GlassBackdropType,
            BlurRadius = BlurRadius,
            GlassOpacity = GlassOpacity,
            AutoTextContrast = AutoTextContrast,
            SurfaceAlpha = current.SurfaceAlpha,
            SurfaceHoverAlpha = current.SurfaceHoverAlpha,
            SurfaceActiveAlpha = current.SurfaceActiveAlpha,
            SettingsCardAlpha = current.SettingsCardAlpha,
            CardAlpha = current.CardAlpha,
            BorderLightAlpha = current.BorderLightAlpha,
            BorderSubtleAlpha = current.BorderSubtleAlpha,
            OverlayAlpha = current.OverlayAlpha,
        };
        _themeManager.ApplyTheme(profile);
    }

    private void SyncAndSaveTheme()
    {
        SyncThemeToManager();
        _themeManager.Persist();
    }

    public void SaveWithToast(string? message = null)
    {
        Save();
        SaveToastMessage = string.IsNullOrWhiteSpace(message)
            ? L.Text("SettingsSavedToast")
            : message;
        IsSaveToastVisible = true;
    }

    public void HideSaveToast()
    {
        IsSaveToastVisible = false;
    }

    public void LoadSettings()
    {
        var settings = _settingsService.LoadSettings();

        _isLoadingSettings = true;
        try
        {
            if (settings.EncryptedApiKey is not null)
            {
                ApiKey = _settingsService.GetApiKey(settings);
            }

            AnthropicBaseUrl = settings.AnthropicBaseUrl;
            AnalysisModelName = settings.AnalysisModelName;
            ChatModelName = settings.ChatModelName;
            EnableThinking = settings.EnableThinking;
            Language = string.IsNullOrWhiteSpace(settings.Language) ? "zh-CN" : settings.Language;
            SelectedDeletionMode = settings.DeletionMode;
            IsApiKeyValid = settings.IsApiKeyValid;

            ValidationState = IsApiKeyValid ? ValidationState.Valid : ValidationState.None;

            LoadThemeFromCurrent();
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }
}

public sealed record LanguageOption(string Code, string DisplayName);



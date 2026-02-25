using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceMonger.Core.Enums;
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

    [ObservableProperty]
    private string? _apiKey;

    [ObservableProperty]
    private ValidationState _validationState = ValidationState.None;

    [ObservableProperty]
    private DeletionMode _selectedDeletionMode = DeletionMode.MoveToRecycleBin;

    [ObservableProperty]
    private bool _isApiKeyValid;

    [ObservableProperty]
    private string? _validationMessage;

    public SettingsViewModel(ISettingsService settingsService, ILlmClient llmClient)
    {
        _settingsService = settingsService;
        _llmClient = llmClient;

        LoadSettings();
    }

    [RelayCommand]
    private async Task ValidateAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            ValidationState = ValidationState.Invalid;
            ValidationMessage = "API key cannot be empty.";
            IsApiKeyValid = false;
            return;
        }

        ValidationState = ValidationState.Validating;
        ValidationMessage = null;

        try
        {
            var isValid = await _llmClient.ValidateApiKeyAsync(ApiKey);

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
                ValidationMessage = "API key is invalid.";
            }
        }
        catch (Exception ex)
        {
            ValidationState = ValidationState.Invalid;
            IsApiKeyValid = false;
            ValidationMessage = $"Validation failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Save()
    {
        var settings = _settingsService.LoadSettings();

        if (!string.IsNullOrEmpty(ApiKey))
        {
            settings.EncryptedApiKey = _settingsService.EncryptApiKey(ApiKey);
        }

        settings.IsApiKeyValid = IsApiKeyValid;
        settings.DeletionMode = SelectedDeletionMode;

        _settingsService.SaveSettings(settings);
    }

    public void LoadSettings()
    {
        var settings = _settingsService.LoadSettings();

        if (settings.EncryptedApiKey is not null)
        {
            ApiKey = _settingsService.GetApiKey(settings);
        }

        SelectedDeletionMode = settings.DeletionMode;
        IsApiKeyValid = settings.IsApiKeyValid;

        ValidationState = IsApiKeyValid ? ValidationState.Valid : ValidationState.None;
    }
}

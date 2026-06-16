using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceMonger.App.Diagnostics;
using SpaceMonger.App.Converters;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Analysis;

namespace SpaceMonger.App.ViewModels;

public partial class RecommendationsViewModel : ObservableObject
{
    private readonly IRecommendationEngine _recommendationEngine;
    private ScanSession? _currentSession;
    private string? _apiKey;
    private string? _baseUrl;
    private string? _modelName;
    private bool _enableThinking;
    private string? _responseLanguage;
    private FileEntry? _focusEntry;

    [ObservableProperty]
    private ObservableCollection<CleanupRecommendation> _recommendations = new();

    [ObservableProperty]
    private ObservableCollection<CleanupRecommendation> _filteredRecommendations = new();

    [ObservableProperty]
    private object? _selectedCategoryFilter = string.Empty;

    [ObservableProperty]
    private object? _selectedSafetyFilter = string.Empty;

    [ObservableProperty]
    private string _totalRecoverableSpace = FileSizeConverter.FormatSize(0);

    [ObservableProperty]
    private int _totalSelectedCount;

    [ObservableProperty]
    private int _totalItemCount;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private string? _analysisError;

    [ObservableProperty]
    private bool _hasRecommendations;

    [ObservableProperty]
    private AnalysisDiagnostics? _lastDiagnostics;

    /// <summary>
    /// Returns true if any recommendation has been accepted by the user.
    /// Used to warn before re-running analysis that would replace accepted items.
    /// </summary>
    public bool HasAcceptedRecommendations =>
        Recommendations.Any(r => r.IsAccepted);

    public RecommendationsViewModel(IRecommendationEngine recommendationEngine)
    {
        _recommendationEngine = recommendationEngine;
    }

    public void SetContext(ScanSession session, string apiKey, string? baseUrl, string? modelName, bool enableThinking, string? responseLanguage, FileEntry? focusEntry = null)
    {
        _currentSession = session;
        _apiKey = apiKey;
        _baseUrl = baseUrl;
        _modelName = modelName;
        _enableThinking = enableThinking;
        _responseLanguage = responseLanguage;
        _focusEntry = focusEntry;
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (_currentSession is null || _apiKey is null)
            return;

        IsAnalyzing = true;
        AnalysisError = null;

        // Clear previous recommendations immediately so stale results from
        // a different scope (e.g. subfolder) don't persist if this call fails.
        Recommendations = new ObservableCollection<CleanupRecommendation>();
        ApplyFilters();
        UpdateTotals();

        try
        {
            DebugBreakpoints.Hit("recommendation-engine-before");
            var result = await _recommendationEngine.AnalyzeWithDiagnosticsAsync(
                _currentSession, _apiKey, _baseUrl, _modelName, _enableThinking, _responseLanguage, CancellationToken.None, _focusEntry);
            DebugBreakpoints.Hit("recommendation-engine-after");

            LastDiagnostics = result.Diagnostics;
            Recommendations = new ObservableCollection<CleanupRecommendation>(result.Recommendations);
            ApplyFilters();
            UpdateTotals();
            HasRecommendations = Recommendations.Count > 0;
            DebugBreakpoints.Hit("recommendations-applied");
        }
        catch (Exception ex)
        {
            AnalysisError = ex.Message;
            LastDiagnostics = null;
            HasRecommendations = false;
            DebugBreakpoints.Hit("recommendation-error");
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    [RelayCommand]
    private void Accept(CleanupRecommendation rec)
    {
        rec.IsAccepted = true;
        UpdateTotals();
    }

    [RelayCommand]
    private void Dismiss(CleanupRecommendation rec)
    {
        rec.IsDismissed = true;
        UpdateTotals();
    }

    [RelayCommand]
    private void SelectAllSafe()
    {
        foreach (var rec in Recommendations.Where(r => r.SafetyRating == SafetyRating.Safe))
        {
            rec.IsAccepted = true;
        }

        UpdateTotals();
    }

    [RelayCommand]
    private void DeselectAllCaution()
    {
        foreach (var rec in Recommendations.Where(r => r.SafetyRating == SafetyRating.Caution))
        {
            rec.IsDismissed = true;
        }

        UpdateTotals();
    }

    [RelayCommand]
    private void SelectAllInCategory(RecommendationCategory category)
    {
        foreach (var rec in Recommendations.Where(r => r.Category == category))
        {
            rec.IsAccepted = true;
        }

        UpdateTotals();
    }

    [RelayCommand]
    private void DeselectAllInCategory(RecommendationCategory category)
    {
        foreach (var rec in Recommendations.Where(r => r.Category == category))
        {
            rec.IsDismissed = true;
        }

        UpdateTotals();
    }

    partial void OnSelectedCategoryFilterChanged(object? value)
    {
        ApplyFilters();
    }

    partial void OnSelectedSafetyFilterChanged(object? value)
    {
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var filtered = Recommendations.AsEnumerable();

        if (SelectedCategoryFilter is RecommendationCategory category)
        {
            filtered = filtered.Where(r => r.Category == category);
        }

        if (SelectedSafetyFilter is SafetyRating safetyRating)
        {
            filtered = filtered.Where(r => r.SafetyRating == safetyRating);
        }

        FilteredRecommendations = new ObservableCollection<CleanupRecommendation>(filtered);
    }

    public void RefreshAfterCleanup()
    {
        ApplyFilters();
        UpdateTotals();
    }

    public void UpdateTotals()
    {
        TotalSelectedCount = Recommendations.Count(r => r.IsAccepted);
        TotalItemCount = Recommendations.Count;
        TotalRecoverableSpace = FileSizeConverter.FormatSize(
            Recommendations.Where(r => r.IsAccepted).Sum(r => r.Size));
    }
}


using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private FileEntry? _focusEntry;

    [ObservableProperty]
    private ObservableCollection<CleanupRecommendation> _recommendations = new();

    [ObservableProperty]
    private ObservableCollection<CleanupRecommendation> _filteredRecommendations = new();

    [ObservableProperty]
    private RecommendationCategory? _selectedCategoryFilter;

    [ObservableProperty]
    private SafetyRating? _selectedSafetyFilter;

    [ObservableProperty]
    private string _totalRecoverableSpace = FileSizeConverter.FormatSize(0);

    [ObservableProperty]
    private int _totalSelectedCount;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private string? _analysisError;

    [ObservableProperty]
    private bool _hasRecommendations;

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

    public void SetContext(ScanSession session, string apiKey, FileEntry? focusEntry = null)
    {
        _currentSession = session;
        _apiKey = apiKey;
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

        try
        {
            var results = await _recommendationEngine.AnalyzeAsync(
                _currentSession, _apiKey, CancellationToken.None, _focusEntry);

            Recommendations = new ObservableCollection<CleanupRecommendation>(results);
            ApplyFilters();
            HasRecommendations = Recommendations.Count > 0;
        }
        catch (Exception ex)
        {
            AnalysisError = ex.Message;
            HasRecommendations = false;
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

    partial void OnSelectedCategoryFilterChanged(RecommendationCategory? value)
    {
        ApplyFilters();
    }

    partial void OnSelectedSafetyFilterChanged(SafetyRating? value)
    {
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var filtered = Recommendations.AsEnumerable();

        if (SelectedCategoryFilter is not null)
        {
            filtered = filtered.Where(r => r.Category == SelectedCategoryFilter.Value);
        }

        if (SelectedSafetyFilter is not null)
        {
            filtered = filtered.Where(r => r.SafetyRating == SelectedSafetyFilter.Value);
        }

        FilteredRecommendations = new ObservableCollection<CleanupRecommendation>(filtered);
    }

    public void RefreshAfterCleanup()
    {
        ApplyFilters();
        UpdateTotals();
    }

    private void UpdateTotals()
    {
        TotalSelectedCount = Recommendations.Count(r => r.IsAccepted);
        TotalRecoverableSpace = FileSizeConverter.FormatSize(
            Recommendations.Where(r => r.IsAccepted).Sum(r => r.Size));
    }
}

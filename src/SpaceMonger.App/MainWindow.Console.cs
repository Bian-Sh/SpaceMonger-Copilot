using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Data;
using Microsoft.Extensions.DependencyInjection;
using SpaceMonger.App.Diagnostics;
using SpaceMonger.App.Helpers;
using SpaceMonger.App.Localization;
using SpaceMonger.App.Converters;
using SpaceMonger.App.ViewModels;
using SpaceMonger.App.Views;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Cleanup;

namespace SpaceMonger.App;

public partial class MainWindow
{
    private void AppendConsoleLine(string message, ConsoleLogLevel level = ConsoleLogLevel.Info)
    {
        var entry = new ConsoleLogEntry(DateTime.Now, level, message);
        _consoleEntries.Add(entry);
        File.AppendAllText(_consoleLogPath, entry.ToLogLine() + Environment.NewLine);
        RefreshConsoleText();
    }

    private void RefreshConsoleText()
    {
        _consoleLog.Clear();
        foreach (var entry in _consoleEntries.Where(e => _visibleConsoleLevels.HasFlag(e.Level)))
        {
            _consoleLog.AppendLine(entry.ToLogLine());
        }

        ConsoleTextBox.Text = _consoleLog.ToString();
        ConsoleTextBox.ScrollToEnd();
    }

    private void ConsoleLogLevel_Click(object sender, RoutedEventArgs e)
    {
        _visibleConsoleLevels = ConsoleLogLevel.None;

        if (ConsoleLevelVerboseMenuItem.IsChecked)
            _visibleConsoleLevels |= ConsoleLogLevel.Verbose;
        if (ConsoleLevelInfoMenuItem.IsChecked)
            _visibleConsoleLevels |= ConsoleLogLevel.Info;
        if (ConsoleLevelWarningMenuItem.IsChecked)
            _visibleConsoleLevels |= ConsoleLogLevel.Warning;
        if (ConsoleLevelErrorMenuItem.IsChecked)
            _visibleConsoleLevels |= ConsoleLogLevel.Error;

        RefreshConsoleText();
    }

    private void ConsoleFilterButton_Click(object sender, RoutedEventArgs e)
    {
        ConsoleFilterButton.ContextMenu.PlacementTarget = ConsoleFilterButton;
        ConsoleFilterButton.ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void StatusConsoleLink_Click(object sender, RoutedEventArgs e)
    {
        EnsureBottomPanelVisible();
        ShowConsolePanel();
    }

    private void AppendAnalysisDiagnostics(AnalysisDiagnostics? diagnostics)
    {
        if (diagnostics is null)
        {
            AppendConsoleLine("DIAG: no diagnostics were produced; request likely failed before response parsing.");
            return;
        }

        AppendConsoleLine("DIAG: target=" + diagnostics.TargetPath);
        AppendConsoleLine("DIAG: scope=" + diagnostics.ScopePath + " focused=" + diagnostics.IsFocusedScope);
        AppendConsoleLine("DIAG: metadata_chars=" + diagnostics.MetadataLength + " response_chars=" + diagnostics.ResponseLength + " extracted_json_chars=" + diagnostics.ExtractedJsonLength);
        AppendConsoleLine("DIAG: parsed_recs=" + diagnostics.ParsedRecommendationCount + " protected_filtered=" + diagnostics.ProtectedFilteredCount + " missing_entry=" + diagnostics.MissingEntryCount + " malformed=" + diagnostics.MalformedRecommendationCount + " missing_fields=" + diagnostics.MissingFieldRecommendationCount);

        if (!string.IsNullOrWhiteSpace(diagnostics.ParseError))
        {
            AppendConsoleLine("DIAG: parse_error=" + diagnostics.ParseError, ConsoleLogLevel.Warning);
        }

        if (!string.IsNullOrWhiteSpace(diagnostics.RawResponsePath))
        {
            AppendConsoleLine("DIAG: raw_response_path=" + diagnostics.RawResponsePath);
        }

        if (!string.IsNullOrWhiteSpace(diagnostics.ResponseEnvelopePath))
        {
            AppendConsoleLine("DIAG: response_envelope_path=" + diagnostics.ResponseEnvelopePath);
        }

        if (!string.IsNullOrWhiteSpace(diagnostics.StopReason))
        {
            AppendConsoleLine("DIAG: stop_reason=" + diagnostics.StopReason);
        }

        if (!string.IsNullOrWhiteSpace(diagnostics.ThinkingPath))
        {
            AppendConsoleLine("DIAG: thinking_path=" + diagnostics.ThinkingPath);
        }

        if (!string.IsNullOrWhiteSpace(diagnostics.ExtractedJsonPreview))
        {
            AppendConsoleLine("DIAG: extracted_json_preview=" + diagnostics.ExtractedJsonPreview);
        }
        else if (!string.IsNullOrWhiteSpace(diagnostics.ResponsePreview))
        {
            AppendConsoleLine("DIAG: response_preview=" + diagnostics.ResponsePreview);
        }
    }

    private async void OnAnalyzeRequested()
    {
        if (_recommendationsViewModel is null || _settingsViewModel is null || _recommendationsViewModel.IsAnalyzing)
            return;

        var mainVm = DataContext as MainViewModel;
        if (mainVm?.CurrentSession is null)
        {
            await ShowAppMessageAsync(
                L.Text("AnalyzeNoScanMessage"),
                L.Text("AnalyzeNoScanTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Retrieve the API key from settings (checks actual saved key, not just validation flag)
        var settingsService = App.Services!.GetRequiredService<SpaceMonger.Core.Services.Settings.ISettingsService>();
        var loadedSettings = settingsService.LoadSettings();
        var apiKey = settingsService.GetApiKey(loadedSettings);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var result = await ShowAppMessageAsync(
                L.Text("ApiKeyRequiredMessage"),
                L.Text("ApiKeyRequiredTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                OpenSettingsDialog();
            }

            // Re-check after settings dialog
            loadedSettings = settingsService.LoadSettings();
            apiKey = settingsService.GetApiKey(loadedSettings);
            if (string.IsNullOrWhiteSpace(apiKey))
                return;
        }

        // FR-029: Warn if re-running analysis will replace accepted recommendations
        if (_recommendationsViewModel.HasAcceptedRecommendations)
        {
            var confirmResult = await ShowAppMessageAsync(
                L.Text("ConfirmReanalysisMessage"),
                L.Text("ConfirmReanalysisTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmResult != MessageBoxResult.Yes)
                return;
        }

        // Show the panel immediately so the user sees the loading indicator
        EnsureBottomPanelVisible();
        ShowRecommendationsPanel();
        DebugBreakpoints.Hit("analyze-click");

        // If the user has drilled into a folder, scope the analysis to that subtree.
        // At the top level (CurrentRoot == scan root), analyze the whole drive.
        FileEntry? focusEntry = null;
        if (_treemapViewModel?.CurrentRoot is not null
            && _treemapViewModel.CurrentRoot != mainVm.CurrentSession.RootEntry)
        {
            focusEntry = _treemapViewModel.CurrentRoot;
        }

        _recommendationsViewModel.SetContext(
            mainVm.CurrentSession,
            apiKey,
            loadedSettings.AnthropicBaseUrl,
            loadedSettings.AnalysisModelName,
            loadedSettings.EnableThinking,
            loadedSettings.Language,
            focusEntry);
        DebugBreakpoints.Hit("analyze-context-ready");

        var scopeLabel = focusEntry is not null
            ? L.Format("AnalyzingFolderStatus", focusEntry.Name)
            : L.Text("AnalyzingScanResultsStatus");
        mainVm.ScanProgressText = scopeLabel;
        AppendConsoleLine(scopeLabel);
        AppendConsoleLine(focusEntry is not null
            ? $"Analysis scope: {focusEntry.Path}"
            : $"Analysis scope: {mainVm.CurrentSession.TargetPath}");
        await _recommendationsViewModel.AnalyzeCommand.ExecuteAsync(null);
        DebugBreakpoints.Hit("analyze-command-returned");

        if (_recommendationsViewModel.AnalysisError is not null)
        {
            mainVm.ScanProgressText = L.Format("AnalysisFailedStatus", _recommendationsViewModel.AnalysisError);
            AppendConsoleLine(mainVm.ScanProgressText, ConsoleLogLevel.Error);
            AppendAnalysisDiagnostics(_recommendationsViewModel.LastDiagnostics);
            ShowConsolePanel();
        }
        else
        {
            var count = _recommendationsViewModel.Recommendations.Count;
            mainVm.ScanProgressText = L.Format("AnalysisCompleteStatus", count, count == 1 ? "" : "s");
            AppendConsoleLine(mainVm.ScanProgressText);
            AppendAnalysisDiagnostics(_recommendationsViewModel.LastDiagnostics);
            if (count == 0)
            {
                AppendConsoleLine("DIAG: zero final recommendations. Inspect parsed_recs/protected_filtered/parse_error above to determine whether this was an empty model result, parse failure, or post-filtering.");
                ShowConsolePanel();
            }
        }
    }

}

[Flags]
public enum ConsoleLogLevel
{
    None = 0,
    Verbose = 1 << 0,
    Info = 1 << 1,
    Warning = 1 << 2,
    Error = 1 << 3,
}

public sealed record ConsoleLogEntry(DateTime Timestamp, ConsoleLogLevel Level, string Message)
{
    public string ToLogLine() => $"[{Timestamp:HH:mm:ss}] [{Level}] {Message}";

}

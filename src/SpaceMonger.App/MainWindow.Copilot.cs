using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SpaceMonger.App.Localization;
using SpaceMonger.App.Services.Copilot;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Copilot;
using SpaceMonger.Core.Services.Settings;

namespace SpaceMonger.App;

public partial class MainWindow : IAiDiskActionExecutor
{
    public bool HasExistingRecommendations => _recommendationsViewModel?.Recommendations.Count > 0;

    public Task<AiActionResult> ExecuteAsync(AiActionRequest request, CancellationToken cancellationToken)
    {
        return Dispatcher.InvokeAsync(async () => request.Kind switch
        {
            AiActionKind.StartScan => await ExecuteCopilotScanAsync(request),
            AiActionKind.AnalyzeCleanup => await ExecuteCopilotAnalyzeCleanupAsync(request),
            AiActionKind.NavigateToScannedPath => ExecuteCopilotNavigate(request),
            AiActionKind.SelectRecommendation => ExecuteCopilotRecommendationSelection(request, true),
            AiActionKind.DeselectRecommendation => ExecuteCopilotRecommendationSelection(request, false),
            _ => AiActionResult.Fail(Localized("Unsupported AI disk management action.", "不支持的 AI 磁盘管理动作。"))
        }).Task.Unwrap();
    }

    private async Task<AiActionResult> ExecuteCopilotScanAsync(AiActionRequest request)
    {
        var path = request.Path?.Trim();
        if (string.IsNullOrWhiteSpace(path))
            return AiActionResult.Fail(Localized("Missing path to scan.", "缺少要扫描的路径。"));

        if (DataContext is not MainViewModel mainVm)
            return AiActionResult.Fail(Localized("The main window is not connected to the scan view model yet.", "主窗口尚未连接扫描视图模型。"));

        if (mainVm.IsScanning)
            return AiActionResult.Fail(Localized("A scan is already running.", "当前已经有扫描任务在进行中。"));

        mainVm.SelectedPath = path;
        if (!mainVm.ScanCommand.CanExecute(null))
            return AiActionResult.Fail(Localized("This path cannot be scanned right now.", "当前路径暂时不能扫描。"), path);

        mainVm.ScanCommand.Execute(null);
        await WaitForScanToFinishAsync(mainVm);

        return mainVm.CurrentSession is null
            ? AiActionResult.Fail(Localized("The scan did not produce a usable result.", "扫描没有产生可用结果。"), LocalizeScanProgress(mainVm.ScanProgressText))
            : AiActionResult.Ok(Localized("Scan complete. TreeView, Treemap, and chat context have been refreshed.", "扫描完成，已刷新 TreeView、Treemap 和聊天上下文。"), LocalizeScanProgress(mainVm.ScanProgressText));
    }

    private async Task<AiActionResult> ExecuteCopilotAnalyzeCleanupAsync(AiActionRequest request)
    {
        if (_recommendationsViewModel is null || _settingsViewModel is null)
            return AiActionResult.Fail(Localized("Cleanup recommendations are not ready yet.", "推荐清理视图模型尚未准备好。"));

        if (_recommendationsViewModel.IsAnalyzing)
            return AiActionResult.Fail(Localized("Cleanup recommendation analysis is already running.", "推荐清理分析正在进行中。"));

        if (DataContext is not MainViewModel mainVm || mainVm.CurrentSession is null)
            return AiActionResult.Fail(Localized("Complete a scan before analyzing cleanup candidates.", "需要先完成一次扫描，才能分析可清理内容。"));

        var settingsService = App.Services!.GetRequiredService<ISettingsService>();
        var loadedSettings = settingsService.LoadSettings();
        var apiKey = settingsService.GetApiKey(loadedSettings);
        if (string.IsNullOrWhiteSpace(apiKey))
            return AiActionResult.Fail(Localized("Configure a model service API Key before running cleanup recommendation analysis.", "需要先配置模型服务 API Key，才能运行推荐清理分析。"));

        EnsureBottomPanelVisible();
        ShowRecommendationsPanel();

        FileEntry? focusEntry = null;
        if (!string.IsNullOrWhiteSpace(request.Path) && _treemapViewModel?.CurrentRoot is not null)
        {
            if (_treemapViewModel.NavigateToPath(request.Path))
                focusEntry = _treemapViewModel.CurrentRoot;
        }

        focusEntry ??= _treemapViewModel?.CurrentRoot is not null && _treemapViewModel.CurrentRoot != mainVm.CurrentSession.RootEntry
            ? _treemapViewModel.CurrentRoot
            : null;

        _recommendationsViewModel.SetContext(
            mainVm.CurrentSession,
            apiKey,
            loadedSettings.AnthropicBaseUrl,
            loadedSettings.AnalysisModelName,
            loadedSettings.EnableThinking,
            loadedSettings.Language,
            focusEntry);

        var scopeLabel = focusEntry is not null
            ? L.Format("AnalyzingFolderStatus", focusEntry.Name)
            : L.Text("AnalyzingScanResultsStatus");
        mainVm.ScanProgressText = scopeLabel;
        AppendConsoleLine(scopeLabel);
        await _recommendationsViewModel.AnalyzeCommand.ExecuteAsync(null);

        if (_recommendationsViewModel.AnalysisError is not null)
        {
            return AiActionResult.Fail(Localized("Cleanup recommendation analysis failed.", "推荐清理分析失败。"), _recommendationsViewModel.AnalysisError);
        }

        var count = _recommendationsViewModel.Recommendations.Count;
        mainVm.ScanProgressText = L.Format("AnalysisCompleteStatus", count, count == 1 ? "" : "s");
        AppendConsoleLine(mainVm.ScanProgressText);
        return AiActionResult.Ok(Localized($"Cleanup recommendation analysis complete. Generated {count} recommendation{(count == 1 ? "" : "s")}.", $"推荐清理分析完成，共生成 {count} 条建议。"), mainVm.ScanProgressText);
    }

    private AiActionResult ExecuteCopilotNavigate(AiActionRequest request)
    {
        var path = request.Path?.Trim();
        if (string.IsNullOrWhiteSpace(path))
            return AiActionResult.Fail(Localized("Missing path to locate.", "缺少要定位的路径。"));

        if (_treemapViewModel is null)
            return AiActionResult.Fail(Localized("Treemap is not ready yet.", "Treemap 尚未准备好。"));

        return _treemapViewModel.NavigateToPath(path)
            ? AiActionResult.Ok(Localized("Located the path in the current scan tree.", "已在当前扫描树中定位路径。"), path)
            : AiActionResult.Fail(Localized("This path is not in the current scan tree. You can confirm whether to scan it first.", "当前扫描树中没有这个路径。可以先确认是否需要扫描该路径。"), path);
    }

    private AiActionResult ExecuteCopilotRecommendationSelection(AiActionRequest request, bool accept)
    {
        if (_recommendationsViewModel is null || string.IsNullOrWhiteSpace(request.RecommendationId))
            return AiActionResult.Fail(Localized("Missing recommendation ID.", "缺少推荐项 ID。"));

        var recommendation = _recommendationsViewModel.Recommendations
            .FirstOrDefault(item => string.Equals(item.Id, request.RecommendationId, StringComparison.OrdinalIgnoreCase));
        if (recommendation is null)
            return AiActionResult.Fail(Localized("The matching recommendation was not found.", "没有找到对应的推荐项。"), request.RecommendationId);

        recommendation.IsAccepted = accept;
        recommendation.IsDismissed = !accept;
        _recommendationsViewModel.RefreshFilteredRecommendations();
        return AiActionResult.Ok(accept ? Localized("Recommendation selected.", "已选中推荐项。") : Localized("Recommendation deselected/dismissed.", "已取消/忽略推荐项。"), recommendation.TargetPath);
    }


    private static string Localized(string english, string chinese)
        => L.CurrentLanguageName.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? english : chinese;

    private static string? LocalizeScanProgress(string? progressText)
    {
        if (string.IsNullOrWhiteSpace(progressText) || !L.CurrentLanguageName.StartsWith("en", StringComparison.OrdinalIgnoreCase))
        {
            return progressText;
        }

        return progressText
            .Replace("扫描完成，已刷新 TreeView、Treemap 和聊天上下文。", "Scan complete. TreeView, Treemap, and chat context have been refreshed.", StringComparison.Ordinal)
            .Replace("计算大小", "Calculating sizes", StringComparison.Ordinal)
            .Replace("个文件", "files", StringComparison.Ordinal)
            .Replace("个文件夹", "folders", StringComparison.Ordinal);
    }

    private static async Task WaitForScanToFinishAsync(MainViewModel mainVm)
    {
        while (mainVm.IsScanning)
        {
            await Task.Delay(250);
        }
    }
}

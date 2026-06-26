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
            _ => AiActionResult.Fail("不支持的 AI 磁盘管理动作。")
        }).Task.Unwrap();
    }

    private async Task<AiActionResult> ExecuteCopilotScanAsync(AiActionRequest request)
    {
        var path = request.Path?.Trim();
        if (string.IsNullOrWhiteSpace(path))
            return AiActionResult.Fail("缺少要扫描的路径。");

        if (DataContext is not MainViewModel mainVm)
            return AiActionResult.Fail("主窗口尚未连接扫描视图模型。");

        if (mainVm.IsScanning)
            return AiActionResult.Fail("当前已经有扫描任务在进行中。");

        mainVm.SelectedPath = path;
        if (!mainVm.ScanCommand.CanExecute(null))
            return AiActionResult.Fail("当前路径暂时不能扫描。", path);

        mainVm.ScanCommand.Execute(null);
        await WaitForScanToFinishAsync(mainVm);

        return mainVm.CurrentSession is null
            ? AiActionResult.Fail("扫描没有产生可用结果。", mainVm.ScanProgressText)
            : AiActionResult.Ok("扫描完成，已刷新 TreeView、Treemap 和聊天上下文。", mainVm.ScanProgressText);
    }

    private async Task<AiActionResult> ExecuteCopilotAnalyzeCleanupAsync(AiActionRequest request)
    {
        if (_recommendationsViewModel is null || _settingsViewModel is null)
            return AiActionResult.Fail("推荐清理视图模型尚未准备好。");

        if (_recommendationsViewModel.IsAnalyzing)
            return AiActionResult.Fail("推荐清理分析正在进行中。");

        if (DataContext is not MainViewModel mainVm || mainVm.CurrentSession is null)
            return AiActionResult.Fail("需要先完成一次扫描，才能分析可清理内容。");

        var settingsService = App.Services!.GetRequiredService<ISettingsService>();
        var loadedSettings = settingsService.LoadSettings();
        var apiKey = settingsService.GetApiKey(loadedSettings);
        if (string.IsNullOrWhiteSpace(apiKey))
            return AiActionResult.Fail("需要先配置模型服务 API Key，才能运行推荐清理分析。");

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
            return AiActionResult.Fail("推荐清理分析失败。", _recommendationsViewModel.AnalysisError);
        }

        var count = _recommendationsViewModel.Recommendations.Count;
        mainVm.ScanProgressText = L.Format("AnalysisCompleteStatus", count, count == 1 ? "" : "s");
        AppendConsoleLine(mainVm.ScanProgressText);
        return AiActionResult.Ok($"推荐清理分析完成，共生成 {count} 条建议。", mainVm.ScanProgressText);
    }

    private AiActionResult ExecuteCopilotNavigate(AiActionRequest request)
    {
        var path = request.Path?.Trim();
        if (string.IsNullOrWhiteSpace(path))
            return AiActionResult.Fail("缺少要定位的路径。");

        if (_treemapViewModel is null)
            return AiActionResult.Fail("Treemap 尚未准备好。");

        return _treemapViewModel.NavigateToPath(path)
            ? AiActionResult.Ok("已在当前扫描树中定位路径。", path)
            : AiActionResult.Fail("当前扫描树中没有这个路径。可以先确认是否需要扫描该路径。", path);
    }

    private AiActionResult ExecuteCopilotRecommendationSelection(AiActionRequest request, bool accept)
    {
        if (_recommendationsViewModel is null || string.IsNullOrWhiteSpace(request.RecommendationId))
            return AiActionResult.Fail("缺少推荐项 ID。");

        var recommendation = _recommendationsViewModel.Recommendations
            .FirstOrDefault(item => string.Equals(item.Id, request.RecommendationId, StringComparison.OrdinalIgnoreCase));
        if (recommendation is null)
            return AiActionResult.Fail("没有找到对应的推荐项。", request.RecommendationId);

        recommendation.IsAccepted = accept;
        recommendation.IsDismissed = !accept;
        _recommendationsViewModel.RefreshFilteredRecommendations();
        return AiActionResult.Ok(accept ? "已选中推荐项。" : "已取消/忽略推荐项。", recommendation.TargetPath);
    }

    private static async Task WaitForScanToFinishAsync(MainViewModel mainVm)
    {
        while (mainVm.IsScanning)
        {
            await Task.Delay(250);
        }
    }
}

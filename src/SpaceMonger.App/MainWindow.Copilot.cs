using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SpaceMonger.App.Localization;
using SpaceMonger.App.Services.Copilot;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Analysis;
using SpaceMonger.Core.Services.Copilot;
using SpaceMonger.Core.Services.Scanning;
using SpaceMonger.Core.Services.Settings;
using Serilog;

namespace SpaceMonger.App;

public partial class MainWindow : IAiDiskActionExecutor
{
    public bool HasExistingRecommendations => _recommendationsViewModel?.Recommendations.Count > 0;

    public Task<AiActionResult> ExecuteAsync(AiActionRequest request, CancellationToken cancellationToken, IProgress<AiActionProgress>? progress = null)
    {
        return Dispatcher.InvokeAsync(async () => request.Kind switch
        {
            AiActionKind.StartScan => await ExecuteCopilotScanAsync(request),
            AiActionKind.AnalyzeCleanup => await ExecuteCopilotAnalyzeCleanupAsync(request),
            AiActionKind.DiscoverUnityLibraries => await ExecuteCopilotDiscoverUnityLibrariesAsync(request, cancellationToken, progress),
            AiActionKind.NavigateToScannedPath => ExecuteCopilotNavigate(request),
            AiActionKind.SelectRecommendation => ExecuteCopilotRecommendationSelection(request, true),
            AiActionKind.DeselectRecommendation => ExecuteCopilotRecommendationSelection(request, false),
            _ => AiActionResult.Fail(Localized("Unsupported AI disk management action.", "不支持的 AI 磁盘管理动作。"))
        }).Task.Unwrap();
    }

    private async Task<AiActionResult> ExecuteCopilotDiscoverUnityLibrariesAsync(AiActionRequest request, CancellationToken cancellationToken, IProgress<AiActionProgress>? progress)
    {
        if (_recommendationsViewModel is null)
            return AiActionResult.Fail(Localized("Cleanup recommendations are not ready yet.", "清理建议视图尚未准备好。"));

        if (DataContext is not MainViewModel mainVm)
            return AiActionResult.Fail(Localized("The main window is not connected to the scan view model yet.", "主窗口尚未连接扫描视图模型。"));

        if (mainVm.IsScanning)
            return AiActionResult.Fail(Localized("A scan is already running.", "当前已有扫描任务正在运行。"));

        var targets = BuildUnityDiscoveryTargets(request).ToList();
        if (targets.Count == 0)
            return AiActionResult.Fail(Localized("No ready drives or valid scan roots were found.", "没有找到可扫描磁盘或有效扫描根目录。"));

        EnsureBottomPanelVisible();
        ShowRecommendationsPanel();
        _recommendationsViewModel.BeginExternalRecommendationLoad();

        try
        {
            var recommendations = new List<CleanupRecommendation>();
            var scannedCount = 0;
            var failedTargets = new List<string>();
            var enumerateTitle = string.IsNullOrWhiteSpace(request.Path)
                ? Localized("AI is checking ready drives", "AI 正在确认可扫描磁盘")
                : Localized("AI is checking the Unity scan root", "AI 正在确认 Unity 扫描根目录");
            progress?.Report(new AiActionProgress("enumerate_drives", enumerateTitle, AiActionProgressStatus.Completed));

            using var scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var externalScan = mainVm.BeginExternalScan(enumerateTitle, string.Empty, scanCancellation.Cancel);
            var fileScanner = App.Services!.GetRequiredService<IFileScanner>();

            foreach (var target in targets)
            {
                scanCancellation.Token.ThrowIfCancellationRequested();
                var title = L.Format("AiScanTitleFormat", FormatAiScanTargetLabel(target.Path));
                progress?.Report(new AiActionProgress(target.StepId, title, AiActionProgressStatus.Running));
                mainVm.ScanTitleText = title;
                mainVm.ScanProgressText = string.Empty;
                Log.Information("{Message}", title);

                try
                {
                    var scanSession = await fileScanner.ScanAsync(
                        target.Path,
                        new Progress<ScanProgress>(scanProgress =>
                            Dispatcher.BeginInvoke(() => mainVm.ScanProgressText = scanProgress.FileCount > 0 || scanProgress.FolderCount > 0
                                ? L.Format("ScanProgressStatus", scanProgress.CurrentPath, scanProgress.FileCount, scanProgress.FolderCount)
                                : scanProgress.CurrentPath)),
                        scanCancellation.Token);

                    scannedCount++;
                    CacheAiScanSession(scanSession);
                    if (scanSession.RootEntry is null)
                    {
                        progress?.Report(new AiActionProgress(target.StepId, title, AiActionProgressStatus.Completed));
                        continue;
                    }

                    var targetRecommendations = RecommendationEngine
                        .BuildUnityLibraryRecommendations(scanSession.RootEntry)
                        .ToList();
                    if (targetRecommendations.Count > 0)
                    {
                        recommendations.AddRange(targetRecommendations);
                    }

                    progress?.Report(new AiActionProgress(target.StepId, title, AiActionProgressStatus.Completed));
                }
                catch (OperationCanceledException)
                {
                    progress?.Report(new AiActionProgress(target.StepId, title, AiActionProgressStatus.Failed));
                    throw;
                }
                catch (Exception ex)
                {
                    failedTargets.Add($"{target.Path} ({ex.Message})");
                    progress?.Report(new AiActionProgress(target.StepId, title, AiActionProgressStatus.Failed));
                }
            }

            var distinctRecommendations = recommendations
                .GroupBy(item => item.TargetPath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(item => item.Size)
                .ToList();

            var writeTitle = Localized("AI is writing Unity cleanup recommendations", "AI 正在写入 Unity 清理建议");
            progress?.Report(new AiActionProgress("write_unity_recommendations", writeTitle, AiActionProgressStatus.Running));
            _recommendationsViewModel.SetExternalRecommendations(distinctRecommendations);
            progress?.Report(new AiActionProgress("write_unity_recommendations", writeTitle, AiActionProgressStatus.Completed));

            var details = Localized(
                string.IsNullOrWhiteSpace(request.Path)
                    ? $"Scanned {scannedCount}/{targets.Count} ready drive(s); found {distinctRecommendations.Count} Unity Library folder(s)."
                    : $"Scanned {scannedCount}/{targets.Count} path(s); found {distinctRecommendations.Count} Unity Library folder(s).",
                string.IsNullOrWhiteSpace(request.Path)
                    ? $"已扫描 {scannedCount}/{targets.Count} 个可用磁盘；找到 {distinctRecommendations.Count} 个 Unity Library 文件夹。"
                    : $"已扫描 {scannedCount}/{targets.Count} 个路径；找到 {distinctRecommendations.Count} 个 Unity Library 文件夹。");
            if (failedTargets.Count > 0)
            {
                details += Environment.NewLine + Localized("Skipped/failed targets: ", "跳过/失败的目标：") + string.Join(", ", failedTargets);
            }

            mainVm.ScanProgressText = details;
            Log.Information("{Message}", details);
            return AiActionResult.Ok(Localized("Unity Library discovery complete. Review the recommendations panel before deleting anything.", "Unity Library 发现完成。删除前请先在推荐清理面板中复核。"), details);
        }
        finally
        {
            _recommendationsViewModel.EndExternalRecommendationLoad();
        }
    }

    private static IEnumerable<UnityDiscoveryTarget> BuildUnityDiscoveryTargets(AiActionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Path) && Directory.Exists(request.Path))
        {
            yield return new UnityDiscoveryTarget(request.Path, "scan_scope:" + request.Path);
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(request.Path))
        {
            yield break;
        }

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady).OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase))
        {
            yield return new UnityDiscoveryTarget(drive.Name, "scan_drive:" + drive.Name.TrimEnd('\\'));
        }
    }

    private async Task<AiActionResult> ExecuteCopilotScanAsync(AiActionRequest request)
    {
        var path = request.Path?.Trim();
        if (string.IsNullOrWhiteSpace(path))
            return AiActionResult.Fail(Localized("Missing path to scan.", "缺少要扫描的路径。"));

        if (DataContext is not MainViewModel mainVm)
            return AiActionResult.Fail(Localized("The main window is not connected to the scan view model yet.", "主窗口尚未连接扫描视图模型。"));

        if (mainVm.IsScanning)
            return AiActionResult.Fail(Localized("A scan is already running.", "当前已有扫描任务正在运行。"));

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
            return AiActionResult.Fail(Localized("Cleanup recommendations are not ready yet.", "清理建议视图尚未准备好。"));

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
        Log.Information("{Message}", scopeLabel);
        await _recommendationsViewModel.AnalyzeCommand.ExecuteAsync(null);

        if (_recommendationsViewModel.AnalysisError is not null)
        {
            return AiActionResult.Fail(Localized("Cleanup recommendation analysis failed.", "推荐清理分析失败。"), _recommendationsViewModel.AnalysisError);
        }

        var count = _recommendationsViewModel.Recommendations.Count;
        mainVm.ScanProgressText = L.Format("AnalysisCompleteStatus", count, count == 1 ? "" : "s");
        Log.Information("{Message}", mainVm.ScanProgressText);
        return AiActionResult.Ok(Localized($"Cleanup recommendation analysis complete. Generated {count} recommendation{(count == 1 ? "" : "s") }.", $"推荐清理分析完成，共生成 {count} 条建议。"), mainVm.ScanProgressText);
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

    private static string FormatAiScanTargetLabel(string path)
    {
        var root = Path.GetPathRoot(path.Trim());
        if (!string.IsNullOrWhiteSpace(root) && string.Equals(Path.GetFullPath(path), root, StringComparison.OrdinalIgnoreCase))
        {
            var drive = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (drive.EndsWith(":", StringComparison.Ordinal))
            {
                var letter = drive.TrimEnd(':');
                return L.CurrentLanguageName.StartsWith("en", StringComparison.OrdinalIgnoreCase)
                    ? $"{letter} drive"
                    : $"{letter} 盘";
            }
        }

        return path;
    }

    private static string Localized(string english, string chinese)
        => L.CurrentLanguageName.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? english : chinese;

    private static string? LocalizeScanProgress(string? progressText)
    {
        return progressText;
    }

    private static async Task WaitForScanToFinishAsync(MainViewModel mainVm)
    {
        while (mainVm.IsScanning)
        {
            await Task.Delay(250);
        }
    }

    private static async Task WaitForScannerReadyAsync(IFileScanner scanner, CancellationToken cancellationToken)
    {
        while (!scanner.IsReady)
        {
            await Task.Delay(250, cancellationToken);
        }
    }

    private sealed record UnityDiscoveryTarget(string Path, string StepId);
}

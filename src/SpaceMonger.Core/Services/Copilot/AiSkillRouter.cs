using System.Text.RegularExpressions;
using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Copilot;

public sealed class AiSkillRouter : IAiSkillRouter
{
    private static readonly Regex WindowsPathRegex = new(@"[A-Za-z]:[\\/][^\r\n\""']+", RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<AiIntent, AiSkill> SkillRegistry = new Dictionary<AiIntent, AiSkill>
    {
        [AiIntent.DiskScan] = new(
            "disk_scan",
            AiIntent.DiskScan,
            "Scan a user-confirmed folder or drive and refresh the disk space views.",
            "Skill disk_scan: When the user asks to scan a path, explain the target and ask for one confirmation before calling the app scan action. Do not claim a scan has run until the action result says so."),

        [AiIntent.FolderCleanupAnalysis] = new(
            "folder_cleanup_analysis",
            AiIntent.FolderCleanupAnalysis,
            "Analyze the currently scanned folder for cleanup opportunities.",
            "Skill folder_cleanup_analysis: For cleanup questions, use the scanned tree context first. If recommendation analysis is needed, ask for one confirmation in the same interaction card. Mention that existing recommendation data will be replaced when applicable."),

        [AiIntent.FileTreeQuery] = new(
            "file_tree_query",
            AiIntent.FileTreeQuery,
            "Query the already scanned file tree for size, children, names, and large files.",
            "Skill file_tree_query: Use read-only file tree tools for path lookup, name lookup, child listing, subtree summaries, and largest-file questions. Never access unscanned disk paths directly."),

        [AiIntent.RecommendationCleanup] = new(
            "recommendation_cleanup",
            AiIntent.RecommendationCleanup,
            "Work with generated cleanup recommendations without deleting files directly.",
            "Skill recommendation_cleanup: You may explain, select, or deselect recommendations through app actions. Never delete or move files directly; final cleanup remains controlled by the app's existing cleanup flow."),

        [AiIntent.TreemapNavigation] = new(
            "treemap_navigation",
            AiIntent.TreemapNavigation,
            "Navigate inside the already scanned tree and synchronize Treemap/TreeView focus.",
            "Skill treemap_navigation: Navigate only to paths present in the current scan. If a path is outside the scan, suggest scanning that path first."),

        [AiIntent.Identity] = new(
            "identity",
            AiIntent.Identity,
            "Answer lightweight identity and project-origin questions.",
            "Skill identity: You are SpaceMonger Copilot, the disk space management assistant inside SpaceMonger Copilot. The app helps users scan drives, understand space usage, and review cleanup recommendations. Keep identity answers brief and do not introduce unrelated app-control abilities.")
    };

    public AiSkillRoutingResult Route(string userMessage, FileEntry? linkedEntry, FileEntry? currentViewRoot, bool hasExistingRecommendations)
    {
        var text = userMessage.Trim();
        var lower = text.ToLowerInvariant();
        var intents = new List<AiIntent>();

        AddIf(intents, AiIntent.Identity, ContainsAny(lower, "你是谁", "作者", "创造者", "前世今生", "项目是什么", "who are you", "creator", "author"));
        AddIf(intents, AiIntent.DiskScan, ContainsAny(lower, "扫描", "scan", "重新扫", "rescan"));
        AddIf(intents, AiIntent.FolderCleanupAnalysis, ContainsAny(lower, "可清理", "清理什么", "有什么能清", "释放空间", "cleanup", "clean up", "cleanable"));
        AddIf(intents, AiIntent.RecommendationCleanup, ContainsAny(lower, "推荐清理", "清理推荐", "recommendation", "recommendations"));
        AddIf(intents, AiIntent.FileTreeQuery, ContainsAny(lower, "大文件", "最大", "占用", "大小", "找", "搜索", "largest", "biggest", "size", "find", "search"));
        AddIf(intents, AiIntent.TreemapNavigation, ContainsAny(lower, "跳转", "打开这个路径", "定位", "导航", "navigate", "go to", "locate"));

        var action = BuildAction(text, intents, linkedEntry, currentViewRoot, hasExistingRecommendations);
        var localAnswer = BuildLocalAnswer(intents);
        var skills = intents.Distinct().Select(intent => SkillRegistry[intent]).ToList();

        return new AiSkillRoutingResult(intents.Distinct().ToList(), skills, action, localAnswer);
    }

    private static AiActionRequest? BuildAction(
        string text,
        IReadOnlyCollection<AiIntent> intents,
        FileEntry? linkedEntry,
        FileEntry? currentViewRoot,
        bool hasExistingRecommendations)
    {
        var path = ExtractPath(text) ?? linkedEntry?.Path;

        if (intents.Contains(AiIntent.DiskScan) && !string.IsNullOrWhiteSpace(path))
        {
            return new AiActionRequest(AiActionKind.StartScan, Path: path, ScopeLabel: path);
        }

        if (intents.Contains(AiIntent.FolderCleanupAnalysis) || intents.Contains(AiIntent.RecommendationCleanup))
        {
            var scopePath = path ?? currentViewRoot?.Path;
            return new AiActionRequest(
                AiActionKind.AnalyzeCleanup,
                Path: scopePath,
                WillOverwriteExistingData: hasExistingRecommendations,
                ScopeLabel: string.IsNullOrWhiteSpace(scopePath) ? "当前扫描范围" : scopePath);
        }

        if (intents.Contains(AiIntent.TreemapNavigation) && !string.IsNullOrWhiteSpace(path))
        {
            return new AiActionRequest(AiActionKind.NavigateToScannedPath, Path: path, ScopeLabel: path);
        }

        return null;
    }

    private static string? BuildLocalAnswer(IReadOnlyCollection<AiIntent> intents)
    {
        return intents.Count == 1 && intents.Contains(AiIntent.Identity)
            ? "我是 SpaceMonger Copilot，SpaceMonger Copilot 里的磁盘空间管理助手。我专注于帮你扫描磁盘、理解目录占用、定位大文件，并在你确认后触发推荐清理分析。"
            : null;
    }

    private static string? ExtractPath(string text)
    {
        var match = WindowsPathRegex.Match(text);
        return match.Success ? match.Value.Trim().TrimEnd('.', ',', '，', '。', ';', '；') : null;
    }

    private static void AddIf(ICollection<AiIntent> intents, AiIntent intent, bool condition)
    {
        if (condition)
            intents.Add(intent);
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }
}

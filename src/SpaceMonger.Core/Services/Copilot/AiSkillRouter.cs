using System.Text.RegularExpressions;
using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Copilot;

public sealed class AiSkillRouter : IAiSkillRouter
{
    private static readonly Regex WindowsPathRegex = new(@"[A-Za-z]:[\\/][^\r\n\""']+", RegexOptions.Compiled);
    private static readonly Regex DriveLetterRegex = new(@"\b([A-Za-z])\s*(?:盘|drive)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SkillMentionRegex = new(@"(?<!\S)@([A-Za-z0-9][A-Za-z0-9._-]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private const string ModuleHelpPromptFallback = "Skill module_help: Answer SpaceMonger Copilot app/module explanation questions only. This is explanatory; do not trigger app actions unless the user separately asks to execute them.";
    private const string IdentityPromptFallback = "Skill identity: You are SpaceMonger Copilot, the disk space management assistant inside SpaceMonger Copilot. Keep identity answers brief and focused on the product identity and purpose.";
    private const string DiskManagementPromptFallback = "Skill disk-management: Handle scan, scanned tree inspection, navigation, and cleanup recommendation analysis only within disk space management scope.";
    private const string UnityProjectCleanupPromptFallback = "Skill unity-project-cleanup: Identify Unity projects and rank Library/cache cleanup risk using Unity project markers, Unity Hub inventory, timestamps, and conservative safety floors.";

    private readonly ISkillPromptProvider _skillPromptProvider;
    private readonly Lazy<IReadOnlyDictionary<AiIntent, AiSkill>> _skillRegistry;

    public AiSkillRouter()
        : this(new FileSkillPromptProvider())
    {
    }

    public AiSkillRouter(ISkillPromptProvider skillPromptProvider)
    {
        _skillPromptProvider = skillPromptProvider;
        _skillRegistry = new Lazy<IReadOnlyDictionary<AiIntent, AiSkill>>(BuildSkillRegistry);
    }

    public string? GetSkillSource(string skillId) => _skillPromptProvider.GetRawContent(skillId);

    public IReadOnlyList<AiSkillCatalogItem> GetSkillCatalog() =>
    [
        new("app-guide", "app-guide", "Guide the user through SpaceMonger Copilot modules and usage."),
        new("disk-management", "disk-management", "Scan, inspect, navigate, and explain disk cleanup opportunities."),
        new("unity-project-cleanup", "unity-project-cleanup", "Identify Unity projects and safely reason about Library/cache cleanup.")
    ];

    public AiSkillRoutingResult Route(string userMessage, FileEntry? linkedEntry, FileEntry? currentViewRoot, bool hasExistingRecommendations, string? responseLanguage = null)
    {
        var text = userMessage.Trim();
        var selectedSkillIds = ExtractSelectedSkillIds(text);
        var intents = new List<AiIntent>();
        var mentionedPath = ExtractPath(text);
        var asksCleanup = ContainsAny(text, "清理", "可清理", "cleanup", "recommend", "recommendation");
        var asksAnalysis = ContainsAny(text, "分析", "analyze", "analyse");

        AddIf(intents, AiIntent.Identity, ContainsAny(text, "你是谁", "你叫什么", "你是哪个 copilot", "你是哪个 Copilot", "自我介绍", "介绍一下你自己", "介绍你自己", "说说你自己", "你自己", "who are you", "what's your name", "tell me about yourself", "introduce yourself", "are you spacemonger copilot"));
        AddIf(intents, AiIntent.ModuleHelp, IsModuleHelpQuestion(text));
        AddIf(intents, AiIntent.UnityProjectCleanup, IsUnityProjectCleanupQuestion(text));
        AddIf(intents, AiIntent.DiskScan, mentionedPath is not null && (ContainsAny(text, "扫描", "scan") || asksCleanup || asksAnalysis));
        AddIf(intents, AiIntent.FolderCleanupAnalysis, asksCleanup && (mentionedPath is not null || asksAnalysis || ContainsAny(text, "文件夹", "目录", "folder", "path")));
        AddIf(intents, AiIntent.FileTreeQuery, ContainsAny(text, "最大", "largest", "占用", "size", "contains", "里面有什么", "多大"));
        AddIf(intents, AiIntent.RecommendationCleanup, ContainsAny(text, "推荐清理", "清理推荐", "recommendation"));
        AddIf(intents, AiIntent.TreemapNavigation, ContainsAny(text, "定位", "导航", "跳到", "navigate", "locate"));
        AddSelectedSkillIntents(intents, selectedSkillIds);

        var distinctIntents = intents.Distinct().ToList();
        var skills = distinctIntents
            .Where(_skillRegistry.Value.ContainsKey)
            .Select(intent => _skillRegistry.Value[intent])
            .Concat(BuildSelectedSkills(selectedSkillIds))
            .DistinctBy(skill => skill.Id)
            .ToList();
        var action = BuildSuggestedAction(text, distinctIntents, mentionedPath, linkedEntry, currentViewRoot, hasExistingRecommendations);
        var localAnswer = BuildLocalAnswer(text, distinctIntents, responseLanguage);
        var canRunWithoutScanContext = distinctIntents.All(intent => intent is AiIntent.Identity or AiIntent.ModuleHelp);
        var preferModelAnswer = distinctIntents.Count > 0 || selectedSkillIds.Count > 0;

        return new AiSkillRoutingResult(distinctIntents, skills, action, localAnswer, canRunWithoutScanContext, preferModelAnswer)
        {
            SelectedSkillIds = selectedSkillIds
        };
    }

    private IReadOnlyDictionary<AiIntent, AiSkill> BuildSkillRegistry() => new Dictionary<AiIntent, AiSkill>
    {
        [AiIntent.ModuleHelp] = new("module_help", AiIntent.ModuleHelp, "Explain disk-management modules only when the user asks what they do or how to use them.", _skillPromptProvider.GetPrompt("app-guide", ModuleHelpPromptFallback)),
        [AiIntent.DiskScan] = new("disk_scan", AiIntent.DiskScan, "Scan a user-confirmed folder or drive and refresh the disk space views.", _skillPromptProvider.GetPrompt("disk-management", DiskManagementPromptFallback)),
        [AiIntent.FolderCleanupAnalysis] = new("folder_cleanup_analysis", AiIntent.FolderCleanupAnalysis, "Analyze the currently scanned folder for cleanup opportunities.", _skillPromptProvider.GetPrompt("disk-management", DiskManagementPromptFallback)),
        [AiIntent.FileTreeQuery] = new("file_tree_query", AiIntent.FileTreeQuery, "Query the already scanned file tree for size, children, names, and large files.", _skillPromptProvider.GetPrompt("disk-management", DiskManagementPromptFallback)),
        [AiIntent.RecommendationCleanup] = new("recommendation_cleanup", AiIntent.RecommendationCleanup, "Work with generated cleanup recommendations without deleting files directly.", _skillPromptProvider.GetPrompt("disk-management", DiskManagementPromptFallback)),
        [AiIntent.TreemapNavigation] = new("treemap_navigation", AiIntent.TreemapNavigation, "Navigate within the current scan views without scanning new locations implicitly.", _skillPromptProvider.GetPrompt("disk-management", DiskManagementPromptFallback)),
        [AiIntent.UnityProjectCleanup] = new("unity_project_cleanup", AiIntent.UnityProjectCleanup, "Identify Unity projects and conservatively rank Library/cache cleanup risk.", _skillPromptProvider.GetPrompt("unity-project-cleanup", UnityProjectCleanupPromptFallback)),
        [AiIntent.Identity] = new("identity", AiIntent.Identity, "Answer who the assistant and project are in a lightweight way.", _skillPromptProvider.GetPrompt("app-guide", IdentityPromptFallback))
    };

    private IEnumerable<AiSkill> BuildSelectedSkills(IReadOnlyList<string> skillIds)
    {
        foreach (var skillId in skillIds)
        {
            var catalogItem = GetSkillCatalog().FirstOrDefault(item => string.Equals(item.Id, skillId, StringComparison.OrdinalIgnoreCase));
            if (catalogItem is null)
            {
                continue;
            }

            yield return new AiSkill(catalogItem.Id, GetSelectedSkillIntent(catalogItem.Id), catalogItem.Description, _skillPromptProvider.GetPrompt(catalogItem.Id, $"Skill {catalogItem.Id}: {catalogItem.Description}"));
        }
    }

    private IReadOnlyList<string> ExtractSelectedSkillIds(string text)
    {
        var knownIds = GetSkillCatalog().Select(skill => skill.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return SkillMentionRegex.Matches(text)
            .Select(match => match.Groups[1].Value)
            .Where(knownIds.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddSelectedSkillIntents(ICollection<AiIntent> intents, IReadOnlyList<string> skillIds)
    {
        foreach (var skillId in skillIds)
        {
            var intent = GetSelectedSkillIntent(skillId);
            if (intent != AiIntent.GeneralChat)
            {
                intents.Add(intent);
            }
        }
    }

    private static AiIntent GetSelectedSkillIntent(string skillId) => skillId.ToLowerInvariant() switch
    {
        "app-guide" => AiIntent.ModuleHelp,
        "disk-management" => AiIntent.FileTreeQuery,
        "unity-project-cleanup" => AiIntent.UnityProjectCleanup,
        _ => AiIntent.GeneralChat
    };

    private static AiActionRequest? BuildSuggestedAction(string text, IReadOnlyList<AiIntent> intents, string? mentionedPath, FileEntry? linkedEntry, FileEntry? currentViewRoot, bool hasExistingRecommendations)
    {
        if (intents.Contains(AiIntent.UnityProjectCleanup) && ShouldDiscoverUnityLibraries(text))
        {
            return new AiActionRequest(AiActionKind.DiscoverUnityLibraries, Path: mentionedPath, WillOverwriteExistingData: hasExistingRecommendations, ScopeLabel: mentionedPath ?? "Unity Library");
        }

        if (intents.Contains(AiIntent.DiskScan) && !string.IsNullOrWhiteSpace(mentionedPath))
        {
            return new AiActionRequest(AiActionKind.StartScan, Path: mentionedPath, ScopeLabel: mentionedPath);
        }

        if (intents.Contains(AiIntent.FolderCleanupAnalysis))
        {
            var targetPath = linkedEntry?.Path ?? currentViewRoot?.Path;
            if (!string.IsNullOrWhiteSpace(targetPath))
            {
                return new AiActionRequest(AiActionKind.AnalyzeCleanup, Path: targetPath, WillOverwriteExistingData: hasExistingRecommendations, ScopeLabel: targetPath);
            }
        }

        return null;
    }

    private static string? BuildLocalAnswer(string text, IReadOnlyList<AiIntent> intents, string? responseLanguage)
    {
        var english = ShouldAnswerInEnglish(responseLanguage);
        if (intents.Contains(AiIntent.Identity))
        {
            return english
                ? "I am SpaceMonger Copilot, the disk space management assistant in this app. I focus on scanning disks, explaining space usage, locating large files, and starting cleanup recommendation analysis after your confirmation."
                : "我是 SpaceMonger Copilot，这个应用里的磁盘空间管理助手。我专注于扫描磁盘、理解目录占用、定位大文件，并在你确认后触发推荐清理分析。";
        }

        if (intents.Contains(AiIntent.ModuleHelp))
        {
            return english ? BuildEnglishModuleHelpAnswer(text) : BuildChineseModuleHelpAnswer(text);
        }

        return null;
    }

    private static string BuildChineseModuleHelpAnswer(string text)
    {
        if (ContainsAny(text, "treemap", "矩形图", "树状图"))
        {
            return "Treemap 用矩形面积展示相对磁盘占用：块越大，文件或目录通常越占空间。";
        }

        if (ContainsAny(text, "treeview", "文件树", "树形列表"))
        {
            return "TreeView 用目录/文件层级展示扫描结果和大小，适合查看精确路径与结构。";
        }

        if (ContainsAny(text, "推荐清理", "清理推荐", "推荐", "analysis", "分析"))
        {
            return "推荐清理会基于当前扫描结果产出一组可复核的清理候选项；如果已有旧推荐，新分析会覆盖旧结果；真正删除文件时仍会走安全确认流程。";
        }

        if (ContainsAny(text, "扫描", "scan", "路径", "目录", "文件夹"))
        {
            return "扫描用于读取指定磁盘或文件夹，并更新 Treemap、TreeView 和 AI 可理解的空间上下文。";
        }

        return "这个应用主要有这些模块：扫描/路径输入、Treemap、TreeView、推荐清理、AI Chat、设置/API Key、白名单/保护路径、控制台/日志。";
    }

    private static string BuildEnglishModuleHelpAnswer(string text)
    {
        if (ContainsAny(text, "treemap", "矩形图", "树状图"))
        {
            return "Treemap visualizes disk usage with rectangle area: larger blocks use more space.";
        }

        if (ContainsAny(text, "treeview", "文件树", "树形列表"))
        {
            return "TreeView shows the scanned result as a folder/file hierarchy with sizes.";
        }

        if (ContainsAny(text, "推荐清理", "清理推荐", "recommendation", "recommendations", "分析"))
        {
            return "Cleanup recommendations analyze the current scan and produce reviewable cleanup candidates. A new analysis replaces old recommendation data.";
        }

        if (ContainsAny(text, "扫描", "scan", "路径", "目录", "文件夹"))
        {
            return "Scan reads a selected drive or folder and updates Treemap, TreeView, and AI context with disk usage data.";
        }

        return "This app has these user-facing modules: Scan/path input, Treemap, TreeView, cleanup recommendations, AI Chat, Settings/API Key, whitelist/protected paths, and Console/logs.";
    }

    private static bool ShouldAnswerInEnglish(string? responseLanguage)
    {
        if (string.IsNullOrWhiteSpace(responseLanguage))
        {
            return false;
        }

        return responseLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            || responseLanguage.Contains("English", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsModuleHelpQuestion(string text)
    {
        return ContainsAny(text, "有什么用", "怎么用", "如何使用", "是什么", "啥意思", "什么意思", "介绍", "说明", "功能", "模块", "what is", "how to use", "what does", "what means", "meaning", "mean", "features", "modules", "what can you do");
    }

    private static bool IsUnityProjectCleanupQuestion(string text)
    {
        var mentionsUnity = ContainsAny(text, "unity", "unityhub", "unity hub", "projectsettings", "assets", "packages", "library folder", "library文件夹");
        var mentionsUnityProject = mentionsUnity && ContainsAny(text, "工程", "项目", "project", "projects");
        var asksCleanupOrStaleness = ContainsAny(text, "library", "清理", "可清理", "整理", "cleanup", "cache", "缓存", "风险", "risk");
        return mentionsUnity && asksCleanupOrStaleness || mentionsUnityProject;
    }

    private static bool ShouldDiscoverUnityLibraries(string text)
    {
        if (IsModuleHelpQuestion(text) && !HasUnityDiscoveryActionVerb(text))
        {
            return false;
        }

        return (IsUnityProjectCleanupQuestion(text) || text.Contains("@unity-project-cleanup", StringComparison.OrdinalIgnoreCase))
            && ContainsAny(text, "library", "缓存", "cache", "清理", "cleanup")
            && HasUnityDiscoveryActionVerb(text);
    }

    private static bool HasUnityDiscoveryActionVerb(string text)
    {
        return ContainsAny(
            text,
            "发现", "查找", "找出", "扫描", "检测", "列出", "生成", "开始", "执行", "帮我清理", "推荐可清理",
            "discover", "find", "scan", "detect", "list", "generate", "run", "start", "clean");
    }

    private static string? ExtractPath(string text)
    {
        var match = WindowsPathRegex.Match(text);
        if (match.Success)
        {
            return TrimNaturalLanguagePathSuffix(match.Value);
        }

        var chineseDrivePath = ExtractChineseDrivePath(text);
        if (chineseDrivePath is not null)
        {
            return chineseDrivePath;
        }

        var driveMatch = DriveLetterRegex.Match(text);
        return driveMatch.Success ? $@"{char.ToUpperInvariant(driveMatch.Groups[1].Value[0])}:\" : null;
    }


    private static string? ExtractChineseDrivePath(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var driveLetter = text[index];
            if (!char.IsAsciiLetter(driveLetter))
            {
                continue;
            }

            var next = index + 1;
            while (next < text.Length && char.IsWhiteSpace(text[next]))
            {
                next++;
            }

            if (next < text.Length && text[next] == '盘')
            {
                return $@"{char.ToUpperInvariant(driveLetter)}:\";
            }
        }

        return null;
    }
    private static string TrimNaturalLanguagePathSuffix(string value)
    {
        var path = value.Trim().TrimEnd('.', ',', '，', '。', ';', '；');
        string[] suffixMarkers = [" 的 ", " 中的 ", " 里的 ", " 裡的 ", " for ", " to clean", " cleanup", " library"];
        foreach (var marker in suffixMarkers)
        {
            var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                path = path[..index].Trim().TrimEnd('.', ',', '，', '。', ';', '；');
            }
        }

        return path;
    }

    private static void AddIf(ICollection<AiIntent> intents, AiIntent intent, bool condition)
    {
        if (condition)
        {
            intents.Add(intent);
        }
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }
}

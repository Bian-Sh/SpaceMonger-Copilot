using System.Text.RegularExpressions;
using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Copilot;

public sealed class AiSkillRouter : IAiSkillRouter
{
    private static readonly Regex WindowsPathRegex = new(@"[A-Za-z]:[\\/][^\r\n\""']+", RegexOptions.Compiled);
    private static readonly Regex DriveLetterRegex = new(@"\b([A-Za-z])\s*(?:盘|drive)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] ModuleHelpQuestionKeywords =
    [
        "有什么用", "怎么用", "如何使用", "如何用", "是干嘛", "干什么", "用途", "使用方法",
        "是什么", "啥是", "介绍", "解释", "说明", "在哪", "入口", "功能", "模块", "用好你",
        "what is", "what's", "how to use", "what does", "features", "modules", "what can you do"
    ];

    private static readonly string[] ModuleHelpSubjectKeywords =
    [
        "扫描", "scan", "路径", "地址栏", "目录", "文件夹", "treemap", "矩形图", "树图",
        "treeview", "树", "文件树", "推荐清理", "清理推荐", "推荐", "分析", "ai", "chat",
        "copilot", "助手", "你", "app", "应用", "设置", "api key", "apikey", "白名单", "保护", "控制台", "日志"
    ];

    private const string ModuleHelpPromptFallback = "Skill module_help: Answer SpaceMonger Copilot app/module explanation questions only. This is explanatory; do not trigger app actions unless the user separately asks to execute them.";
    private const string IdentityPromptFallback = "Skill identity: You are SpaceMonger Copilot, the disk space management assistant inside SpaceMonger Copilot. Keep identity answers brief and focused on the product identity and purpose.";
    private const string DiskManagementPromptFallback = "Skill disk-management: Handle scan, scanned tree inspection, navigation, and cleanup recommendation analysis only within disk space management scope.";

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

    public AiSkillRoutingResult Route(string userMessage, FileEntry? linkedEntry, FileEntry? currentViewRoot, bool hasExistingRecommendations, string? responseLanguage = null)
    {
        var lower = userMessage.Trim();
        var intents = new List<AiIntent>();

        AddIf(intents, AiIntent.Identity, ContainsAny(lower, "你是谁", "你叫什么", "说说你自己", "介绍下你自己", "介绍你自己", "介绍一下你自己", "自我介绍", "你是哪个 copilot", "作者", "创造者", "前世今生", "项目是什么", "who are you", "what's your name", "tell me about yourself", "introduce yourself", "are you spacemonger copilot", "creator", "author"));
        AddIf(intents, AiIntent.ModuleHelp, IsModuleHelpQuestion(lower));
        AddIf(intents, AiIntent.DiskScan, ContainsAny(lower, "扫描", "scan") && ExtractPath(lower) is not null);
        AddIf(intents, AiIntent.FolderCleanupAnalysis, ContainsAny(lower, "清理", "cleanup", "recommend") && ContainsAny(lower, "文件夹", "目录", "folder", "path"));
        AddIf(intents, AiIntent.FileTreeQuery, ContainsAny(lower, "最大", "largest", "占用", "size", "contains", "里面有什么", "多大"));
        AddIf(intents, AiIntent.RecommendationCleanup, ContainsAny(lower, "推荐清理", "清理推荐", "recommendation"));
        AddIf(intents, AiIntent.TreemapNavigation, ContainsAny(lower, "定位", "导航", "跳到", "navigate", "locate"));

        var distinctIntents = intents.Distinct().ToList();
        var skills = distinctIntents.Select(intent => _skillRegistry.Value[intent]).ToList();
        var action = BuildSuggestedAction(lower, distinctIntents, linkedEntry, currentViewRoot, hasExistingRecommendations);
        var localAnswer = BuildLocalAnswer(lower, distinctIntents, responseLanguage);
        var canRunWithoutScanContext = distinctIntents.All(intent => intent is AiIntent.Identity or AiIntent.ModuleHelp);
        var preferModelAnswer = distinctIntents.Count > 0;

        return new AiSkillRoutingResult(distinctIntents, skills, action, localAnswer, canRunWithoutScanContext, preferModelAnswer);
    }

    private IReadOnlyDictionary<AiIntent, AiSkill> BuildSkillRegistry()
    {
        return new Dictionary<AiIntent, AiSkill>
        {
            [AiIntent.ModuleHelp] = new("module_help", AiIntent.ModuleHelp, "Explain disk-management modules only when the user asks what they do or how to use them.", _skillPromptProvider.GetPrompt("app-guide", ModuleHelpPromptFallback)),
            [AiIntent.DiskScan] = new("disk_scan", AiIntent.DiskScan, "Scan a user-confirmed folder or drive and refresh the disk space views.", _skillPromptProvider.GetPrompt("disk-management", DiskManagementPromptFallback)),
            [AiIntent.FolderCleanupAnalysis] = new("folder_cleanup_analysis", AiIntent.FolderCleanupAnalysis, "Analyze the currently scanned folder for cleanup opportunities.", _skillPromptProvider.GetPrompt("disk-management", DiskManagementPromptFallback)),
            [AiIntent.FileTreeQuery] = new("file_tree_query", AiIntent.FileTreeQuery, "Query the already scanned file tree for size, children, names, and large files.", _skillPromptProvider.GetPrompt("disk-management", DiskManagementPromptFallback)),
            [AiIntent.RecommendationCleanup] = new("recommendation_cleanup", AiIntent.RecommendationCleanup, "Work with generated cleanup recommendations without deleting files directly.", _skillPromptProvider.GetPrompt("disk-management", DiskManagementPromptFallback)),
            [AiIntent.TreemapNavigation] = new("treemap_navigation", AiIntent.TreemapNavigation, "Navigate within the current scan views without scanning new locations implicitly.", _skillPromptProvider.GetPrompt("disk-management", DiskManagementPromptFallback)),
            [AiIntent.Identity] = new("identity", AiIntent.Identity, "Answer who the assistant and project are in a lightweight way.", _skillPromptProvider.GetPrompt("app-guide", IdentityPromptFallback))
        };
    }

    private static AiActionRequest? BuildSuggestedAction(string lower, IReadOnlyList<AiIntent> intents, FileEntry? linkedEntry, FileEntry? currentViewRoot, bool hasExistingRecommendations)
    {
        if (intents.Contains(AiIntent.DiskScan))
        {
            var path = ExtractPath(lower);
            if (!string.IsNullOrWhiteSpace(path))
            {
                return new AiActionRequest(AiActionKind.StartScan, Path: path, ScopeLabel: path);
            }
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

    private static string? BuildLocalAnswer(string lower, IReadOnlyList<AiIntent> intents, string? responseLanguage)
    {
        var english = ShouldAnswerInEnglish(responseLanguage);
        if (intents.Contains(AiIntent.Identity))
        {
            return english
                ? "I am SpaceMonger Copilot, the disk space management assistant in this app. I focus on scanning disks, explaining space usage, locating large files, and starting cleanup recommendation analysis after your confirmation."
                : "我是 SpaceMonger Copilot，这个应用里的磁盘空间管理助手。应用名称保持 SpaceMonger Copilot，不翻译成中文。我专注于帮你扫描磁盘、理解目录占用、定位大文件，并在你确认后触发推荐清理分析。";
        }

        if (intents.Contains(AiIntent.ModuleHelp))
        {
            return english ? BuildEnglishModuleHelpAnswer(lower) : BuildChineseModuleHelpAnswer(lower);
        }

        return null;
    }

    private static string BuildChineseModuleHelpAnswer(string lower)
    {
        if (ContainsAny(lower, "treemap", "矩形图", "树图"))
        {
            return "Treemap 用矩形面积展示相对磁盘占用：块越大，文件或目录通常越占空间。用法：先扫描，再点击大块快速下钻定位空间热点；如果你想看精确层级和路径，就切到 TreeView。";
        }

        if (ContainsAny(lower, "treeview", "文件树", "树", "列表"))
        {
            return "TreeView 用目录/文件层级展示扫描结果和大小。用法：扫描后展开目录查看精确结构、路径和占用，并可结合 AI 继续追问某个目录为什么大、有哪些值得清理。";
        }

        if (ContainsAny(lower, "推荐清理", "清理推荐", "推荐", "analysis", "分析"))
        {
            return "推荐清理会基于当前扫描结果产出一组可复核的清理候选项。用法：先扫描，再发起推荐分析；如果已经有旧推荐，新分析会覆盖旧结果。真正清理文件时仍然走应用现有的安全确认流程。";
        }

        if (ContainsAny(lower, "扫描", "scan", "路径", "地址栏", "目录", "文件夹"))
        {
            return "扫描用于读取指定磁盘或文件夹，并更新 Treemap、TreeView 和 AI 可理解的空间上下文。用法：输入或选择路径后开始扫描；你也可以直接说“扫描 D:\\Downloads”，我会先给出一级确认卡片。";
        }

        if (ContainsAny(lower, "ai", "chat", "copilot", "助手"))
        {
            return "AI Chat / Copilot 用来解释扫描结果、回答文件树问题，并在你确认后发起扫描、推荐分析或导航类操作。它只聚焦磁盘空间管理；会改动状态的动作会先在同一张确认卡片里说清影响。";
        }

        if (ContainsAny(lower, "白名单", "保护"))
        {
            return "白名单/保护路径用于降低敏感目录被扫描、暴露、推荐或纳入清理流程的风险。用法：把不希望清理导向流程触达的位置加入保护列表。";
        }

        if (ContainsAny(lower, "设置", "api key", "apikey"))
        {
            return "设置页用于配置模型服务、API Key 和应用偏好。用法：如果 AI 提示缺少模型配置，就到这里填写有效的模型服务；没有模型时，应用只能提供有限的本地说明，不能进行真正的 AI 分析对话。";
        }

        if (ContainsAny(lower, "控制台", "日志"))
        {
            return "控制台用于查看扫描、分析和清理过程中的状态与诊断信息。用法：当扫描或推荐分析结果不符合预期时，打开控制台查看最近的路径、耗时、错误提示和执行状态。";
        }

        return "这个应用主要有这些模块：扫描/路径输入负责读取磁盘或文件夹；Treemap 用面积快速看出谁最占空间；TreeView 用层级和路径精确查看目录；推荐清理会在扫描后给出可复核的清理候选；AI Chat 可以解释扫描结果、回答空间占用问题，并在需要改动状态时先给确认卡片；设置/API Key 用来配置模型服务；白名单/保护路径用于降低敏感目录被清理流程触达的风险；控制台/日志用于排查扫描和分析状态。你可以直接说“扫描 D 盘”“Downloads 里最大的文件是什么”或“推荐清理怎么用”。";
    }

    private static string BuildEnglishModuleHelpAnswer(string lower)
    {
        if (ContainsAny(lower, "treemap", "矩形图", "树图"))
        {
            return "Treemap visualizes disk usage with rectangle area: larger blocks use more space. Use it after scanning to quickly spot large folders or files, then click blocks to drill down. Switch to TreeView when you need exact hierarchy and paths.";
        }

        if (ContainsAny(lower, "treeview", "文件树", "树", "列表"))
        {
            return "TreeView shows the scanned result as a folder/file hierarchy with sizes. Use it after a scan to expand folders, inspect exact paths, and ask AI about a selected folder such as ‘what can be cleaned here?’.";
        }

        if (ContainsAny(lower, "推荐清理", "清理推荐", "推荐", "recommendation", "recommendations", "分析"))
        {
            return "Cleanup recommendations analyze the current scan and produce reviewable cleanup candidates. Start with a scan, then ask for cleanup analysis or run recommendations. A new analysis replaces old recommendation data, and actual cleanup still requires your review and confirmation.";
        }

        if (ContainsAny(lower, "扫描", "scan", "路径", "地址栏", "目录", "文件夹"))
        {
            return "Scan reads a selected drive or folder and updates Treemap, TreeView, and AI context with disk usage data. Choose or type a path and start scanning; you can also ask ‘scan D:\\Downloads’, and I will show a confirmation card first.";
        }

        if (ContainsAny(lower, "ai", "chat", "copilot", "助手"))
        {
            return "AI Chat / Copilot explains scan results, answers file-tree questions, and can prepare confirmation cards for scan, cleanup analysis, or navigation. It stays focused on disk space management; state-changing actions are confirmed first.";
        }

        if (ContainsAny(lower, "白名单", "保护", "whitelist", "protected"))
        {
            return "Whitelist/protected paths help reduce risk around sensitive locations. Add paths you do not want cleanup-oriented workflows to affect; AI queries and recommendations should stay within scanned and allowed data.";
        }

        if (ContainsAny(lower, "设置", "api key", "apikey", "settings"))
        {
            return "Settings configure the model service, API Key, and app preferences. If AI says model configuration is required, add a valid model service configuration there. Local module help can still work without an API Key.";
        }

        if (ContainsAny(lower, "控制台", "日志", "console", "logs"))
        {
            return "Console/logs show scan, analysis, cleanup, and diagnostic status. Use them when a scan or recommendation analysis behaves unexpectedly and you need recent paths, errors, or progress details.";
        }

        return "This app has these user-facing modules: Scan/path input reads a drive or folder; Treemap visually highlights what takes space; TreeView shows exact folder hierarchy and paths; Cleanup recommendations produce reviewable cleanup candidates after a scan; AI Chat explains scan results and shows confirmation cards before state-changing actions; Settings/API Key configures the model service; Whitelist/protected paths reduce risk around sensitive locations; Console/logs help diagnose scan and analysis status. You can ask things like ‘scan D drive’, ‘what are the largest files in Downloads?’, or ‘how do cleanup recommendations work?’.";
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
        if (ContainsAny(text, "都有啥功能", "有什么功能", "哪些功能", "功能清单", "用好你", "what can you do", "features", "modules"))
        {
            return true;
        }

        return ContainsAny(text, ModuleHelpQuestionKeywords) && ContainsAny(text, ModuleHelpSubjectKeywords);
    }

    private static string? ExtractPath(string text)
    {
        var match = WindowsPathRegex.Match(text);
        if (match.Success)
        {
            return match.Value.Trim().TrimEnd('.', ',', '，', '。', ';', '；');
        }

        var driveMatch = DriveLetterRegex.Match(text);
        return driveMatch.Success ? $@"{char.ToUpperInvariant(driveMatch.Groups[1].Value[0])}:\" : null;
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


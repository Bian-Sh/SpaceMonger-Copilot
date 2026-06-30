using System.Text.RegularExpressions;
using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Copilot;

public sealed class AiSkillRouter : IAiSkillRouter
{
    private static readonly Regex SkillMentionRegex = new(@"(?<!\S)@([A-Za-z0-9][A-Za-z0-9._-]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TokenRegex = new(@"[A-Za-z0-9]+|[\u4e00-\u9fff]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "from", "this", "that", "when", "what", "where", "about", "skill",
        "who", "are", "you", "your", "please", "clean", "cleanup", "scan", "scanning", "disk", "project", "projects", "file", "folder", "folders"
    };

    private readonly ISkillPromptProvider _skillPromptProvider;
    private readonly Lazy<IReadOnlyDictionary<string, AiSkillCatalogItem>> _catalog;

    public AiSkillRouter()
        : this(new FileSkillPromptProvider())
    {
    }

    public AiSkillRouter(ISkillPromptProvider skillPromptProvider)
    {
        _skillPromptProvider = skillPromptProvider;
        _catalog = new Lazy<IReadOnlyDictionary<string, AiSkillCatalogItem>>(() => GetSkillCatalog().ToDictionary(skill => skill.Id, StringComparer.OrdinalIgnoreCase));
    }

    public string? GetSkillSource(string skillId) => _skillPromptProvider.GetRawContent(skillId);

    public IReadOnlyList<AiSkillCatalogItem> GetSkillCatalog() => _skillPromptProvider.GetSkillCatalog();

    public AiSkillRoutingResult Route(string userMessage, FileEntry? linkedEntry, FileEntry? currentViewRoot, bool hasExistingRecommendations, string? responseLanguage = null)
    {
        var trimmedMessage = userMessage.Trim();
        var selectedSkillIds = ExtractSelectedSkillIds(trimmedMessage).ToList();
        if (selectedSkillIds.Count == 0 && !SkillMentionRegex.IsMatch(trimmedMessage))
        {
            selectedSkillIds.AddRange(MatchSkillIdsFromDeclarations(trimmedMessage));
        }

        var skills = selectedSkillIds.Select(BuildSelectedSkill).ToList();

        return new AiSkillRoutingResult(skills)
        {
            SelectedSkillIds = selectedSkillIds
        };
    }

    private AiSkill BuildSelectedSkill(string skillId)
    {
        var catalogItem = _catalog.Value[skillId];
        return new AiSkill(
            catalogItem.Id,
            catalogItem.Description,
            _skillPromptProvider.GetPrompt(catalogItem.Id, $"Skill {catalogItem.Id}: {catalogItem.Description}"));
    }

    private IReadOnlyList<string> ExtractSelectedSkillIds(string text)
    {
        return SkillMentionRegex.Matches(text)
            .Select(match => match.Groups[1].Value)
            .Where(_catalog.Value.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<string> MatchSkillIdsFromDeclarations(string userMessage)
    {
        var queryTokens = ExtractTokens(userMessage).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (queryTokens.Count == 0)
        {
            return [];
        }

        return _catalog.Value.Values
            .Select(skill => new
            {
                Skill = skill,
                Score = ExtractTokens($"{skill.Id} {skill.DisplayName} {skill.Description} {_skillPromptProvider.GetPrompt(skill.Id, string.Empty)}")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(queryTokens.Contains)
            })
            .Where(match => match.Score >= 1)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Skill.Id, StringComparer.OrdinalIgnoreCase)
            .Take(1)
            .Select(match => match.Skill.Id)
            .ToList();
    }

    private static IEnumerable<string> ExtractTokens(string text)
    {
        return TokenRegex.Matches(text)
            .Select(match => match.Value)
            .Where(token => token.Length >= 3)
            .Where(token => !StopWords.Contains(token));
    }

}




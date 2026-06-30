using System.Text.RegularExpressions;
using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Copilot;

public sealed class AiSkillRouter : IAiSkillRouter
{
    private static readonly Regex SkillMentionRegex = new(@"(?<!\S)@([A-Za-z0-9][A-Za-z0-9._-]*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        var selectedSkillIds = ExtractSelectedSkillIds(userMessage.Trim());
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

}




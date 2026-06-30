using System.Text;
using System.Text.RegularExpressions;

namespace SpaceMonger.Core.Services.Copilot;

public sealed class FileSkillPromptProvider : ISkillPromptProvider
{
    private static readonly Regex SkillIdRegex = new("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.Compiled);
    private readonly IReadOnlyList<string>? _skillDirectories;

    public FileSkillPromptProvider()
    {
    }

    public FileSkillPromptProvider(IEnumerable<string> skillDirectories)
    {
        _skillDirectories = skillDirectories.ToArray();
    }

    public IReadOnlyList<AiSkillCatalogItem> GetSkillCatalog() => LoadSkills(_skillDirectories).Catalog;

    public string GetPrompt(string skillId, string fallback)
    {
        if (LoadSkills(_skillDirectories).Prompts.TryGetValue(skillId, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return fallback;
    }

    public string? GetRawContent(string skillId)
    {
        return LoadSkills(_skillDirectories).Prompts.TryGetValue(skillId, out var value) ? value : null;
    }

    public AiSkillStoreResult CreateOrUpdateSkill(string skillId, string title, string description, string bodyMarkdown, bool overwrite)
    {
        skillId = skillId.Trim();
        if (!IsValidSkillId(skillId))
        {
            return new AiSkillStoreResult(false, "Skill id must use lowercase letters, digits, and hyphens only.", skillId, ErrorCode: "invalid_skill_id");
        }

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(bodyMarkdown))
        {
            return new AiSkillStoreResult(false, "Skill title, description, and body are required.", skillId, ErrorCode: "missing_fields");
        }

        var skillDirectory = GetSafeSkillDirectory(skillId);
        var skillFile = Path.Combine(skillDirectory, "SKILL.md");
        var existed = File.Exists(skillFile);
        if (existed && !overwrite)
        {
            return new AiSkillStoreResult(false, "Skill already exists. Set overwrite=true to update it.", skillId, ErrorCode: "already_exists");
        }

        Directory.CreateDirectory(skillDirectory);
        var content = BuildSkillMarkdown(skillId, title, description, bodyMarkdown);
        File.WriteAllText(skillFile, content, Encoding.UTF8);
        return new AiSkillStoreResult(true, existed ? "Skill updated." : "Skill created.", skillId, content);
    }

    public AiSkillStoreResult DeleteSkill(string skillId)
    {
        skillId = skillId.Trim();
        if (!IsValidSkillId(skillId))
        {
            return new AiSkillStoreResult(false, "Skill id must use lowercase letters, digits, and hyphens only.", skillId, ErrorCode: "invalid_skill_id");
        }

        var skillDirectory = GetSafeSkillDirectory(skillId);
        var skillFile = Path.Combine(skillDirectory, "SKILL.md");
        if (!File.Exists(skillFile))
        {
            return new AiSkillStoreResult(false, "Skill does not exist.", skillId, ErrorCode: "not_found");
        }

        Directory.Delete(skillDirectory, recursive: true);
        return new AiSkillStoreResult(true, "Skill deleted.", skillId);
    }

    private static LoadedSkills LoadSkills(IReadOnlyList<string>? configuredDirectories)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in GetSkillDirectories(configuredDirectories).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "SKILL.md", SearchOption.AllDirectories).OrderBy(Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase))
            {
                var id = new DirectoryInfo(Path.GetDirectoryName(file)!).Name;
                if (result.ContainsKey(id))
                {
                    continue;
                }

                result[id] = File.ReadAllText(file, Encoding.UTF8);
            }
        }

        var catalog = result
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(item => new AiSkillCatalogItem(item.Key, ExtractDisplayName(item.Key, item.Value), ExtractDescription(item.Value)))
            .ToList();

        return new LoadedSkills(result, catalog);
    }

    private static IEnumerable<string> GetSkillDirectories(IReadOnlyList<string>? configuredDirectories)
    {
        if (configuredDirectories is { Count: > 0 })
        {
            return configuredDirectories;
        }

        var baseDirectory = AppContext.BaseDirectory;
        var solutionRoot = LocateSolutionRoot(baseDirectory);
        return
        [
            Path.Combine(baseDirectory, "skills"),
            Path.Combine(solutionRoot ?? baseDirectory, "skills"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "skills"))
        ];
    }

    private static string ExtractDisplayName(string skillId, string content)
    {
        foreach (var line in ReadLines(content))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                return trimmed[2..].Trim();
            }
        }

        return skillId;
    }

    private static string ExtractDescription(string content)
    {
        var lines = ReadLines(content).ToArray();
        var frontmatterDescription = ExtractFrontmatterValue(lines, "description");
        if (!string.IsNullOrWhiteSpace(frontmatterDescription))
        {
            return frontmatterDescription;
        }

        for (var index = 0; index < lines.Length; index++)
        {
            if (!string.Equals(lines[index].Trim(), "## Purpose", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var description = ReadParagraph(lines, index + 1);
            if (!string.IsNullOrWhiteSpace(description))
            {
                return description;
            }
        }

        return ReadParagraph(lines, 0);
    }

    private static string ReadParagraph(IReadOnlyList<string> lines, int startIndex)
    {
        var paragraph = new List<string>();
        for (var index = startIndex; index < lines.Count; index++)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal) && paragraph.Count == 0)
            {
                continue;
            }

            if (trimmed.StartsWith("## ", StringComparison.Ordinal) && paragraph.Count > 0)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                if (paragraph.Count > 0)
                {
                    break;
                }

                continue;
            }

            paragraph.Add(trimmed);
        }

        return string.Join(" ", paragraph);
    }

    private static IEnumerable<string> ReadLines(string content)
    {
        using var reader = new StringReader(content);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static string? LocateSolutionRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (directory.EnumerateFiles("*.sln", SearchOption.TopDirectoryOnly).Any())
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private string GetWritableSkillRoot()
    {
        var configured = _skillDirectories?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var baseDirectory = AppContext.BaseDirectory;
        var solutionRoot = LocateSolutionRoot(baseDirectory);
        return Path.Combine(solutionRoot ?? baseDirectory, "skills");
    }

    private string GetSafeSkillDirectory(string skillId)
    {
        var root = GetWritableSkillRoot();
        var skillDirectory = Path.GetFullPath(Path.Combine(root, skillId));
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!skillDirectory.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Skill path escaped the configured skills directory.");
        }

        return skillDirectory;
    }

    private static bool IsValidSkillId(string skillId) => SkillIdRegex.IsMatch(skillId);

    private static string BuildSkillMarkdown(string skillId, string title, string description, string bodyMarkdown)
    {
        var cleanTitle = NormalizeSingleLine(title);
        var cleanDescription = NormalizeSingleLine(description);
        var body = bodyMarkdown.Trim();
        return $"""
            ---
            name: {skillId}
            description: {cleanDescription}
            ---

            # {cleanTitle}

            ## Purpose
            {cleanDescription}

            {body}
            """;
    }

    private static string NormalizeSingleLine(string value) => value.Replace("\r", " ").Replace("\n", " ").Trim();

    private static string? ExtractFrontmatterValue(IReadOnlyList<string> lines, string key)
    {
        if (lines.Count < 3 || lines[0].Trim() != "---")
        {
            return null;
        }

        var prefix = key + ":";
        for (var index = 1; index < lines.Count; index++)
        {
            var line = lines[index].Trim();
            if (line == "---")
            {
                return null;
            }

            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return line[prefix.Length..].Trim().Trim('"');
            }
        }

        return null;
    }

    private sealed record LoadedSkills(IReadOnlyDictionary<string, string> Prompts, IReadOnlyList<AiSkillCatalogItem> Catalog);
}

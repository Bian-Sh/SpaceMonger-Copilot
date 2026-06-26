using System.Reflection;
using System.Text;

namespace SpaceMonger.Core.Services.Copilot;

public static class SkillCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> CachedSkills = new(LoadSkills);

    public static string GetPrompt(string skillId, string fallback)
    {
        if (CachedSkills.Value.TryGetValue(skillId, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return fallback;
    }

    public static string? GetRawContent(string skillId)
    {
        return CachedSkills.Value.TryGetValue(skillId, out var value) ? value : null;
    }

    private static IReadOnlyDictionary<string, string> LoadSkills()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "skills"),
            Path.Combine(LocateSolutionRoot(baseDirectory) ?? baseDirectory, "skills")
        };

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "SKILL.md", SearchOption.AllDirectories))
            {
                var id = new DirectoryInfo(Path.GetDirectoryName(file)!).Name;
                if (result.ContainsKey(id))
                {
                    continue;
                }

                result[id] = File.ReadAllText(file, Encoding.UTF8);
            }
        }

        return result;
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
}

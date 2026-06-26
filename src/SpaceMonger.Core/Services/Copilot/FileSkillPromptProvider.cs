using System.Text;

namespace SpaceMonger.Core.Services.Copilot;

public sealed class FileSkillPromptProvider : ISkillPromptProvider
{
    private readonly Lazy<IReadOnlyDictionary<string, string>> _skills = new(LoadSkills);

    public string GetPrompt(string skillId, string fallback)
    {
        if (_skills.Value.TryGetValue(skillId, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return fallback;
    }

    public string? GetRawContent(string skillId)
    {
        return _skills.Value.TryGetValue(skillId, out var value) ? value : null;
    }

    private static IReadOnlyDictionary<string, string> LoadSkills()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var solutionRoot = LocateSolutionRoot(baseDirectory);
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "skills"),
            Path.Combine(solutionRoot ?? baseDirectory, "skills"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "skills"))
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


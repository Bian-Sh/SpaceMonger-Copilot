using System.Text.Json;
using System.Text.RegularExpressions;
using SpaceMonger.Core.Diagnostics;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Llm;

namespace SpaceMonger.Core.Services.Analysis;

public partial class RecommendationEngine
{
    private static FileEntry? FindEntryByPath(FileEntry root, string targetPath)
    {
        var normalizedTarget = targetPath.Replace('\\', '/').TrimEnd('/');
        var normalizedRoot = root.Path.Replace('\\', '/').TrimEnd('/');

        if (string.Equals(normalizedRoot, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        foreach (var child in root.Children)
        {
            var found = FindEntryByPath(child, targetPath);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static bool IsProtectedPath(string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Check for protected system directories (Windows, Program Files)
        foreach (var protectedSegment in ProtectedPathSegments)
        {
            if (segments.Any(s => string.Equals(s, protectedSegment, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        // Check for standard user document folders
        // Pattern: .../Users/<username>/<ProtectedFolder>/...
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (string.Equals(segments[i], "Users", StringComparison.OrdinalIgnoreCase) &&
                i + 2 < segments.Length)
            {
                var folderAfterUsername = segments[i + 2];
                if (ProtectedUserFolders.Any(pf =>
                    string.Equals(pf, folderAfterUsername, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

using System.Text.Json;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.FileTree;

namespace SpaceMonger.Core.Services.Agent;

public abstract class FileTreeAgentToolBase(IFileTreeQueryService queryService) : IAgentTool
{
    protected IFileTreeQueryService QueryService { get; } = queryService;

    public abstract string Name { get; }
    public abstract string Description { get; }
    public ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;
    public abstract JsonElement Schema { get; }

    public abstract Task<JsonElement> ExecuteAsync(AgentContext context, JsonElement arguments, CancellationToken cancellationToken);

    protected static JsonElement Json(object value)
    {
        return JsonSerializer.SerializeToElement(value, AgentJson.Options);
    }

    protected static JsonElement SchemaJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    protected static string? GetString(JsonElement arguments, string name)
    {
        return arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    protected static int GetInt(JsonElement arguments, string name, int defaultValue, int min, int max)
    {
        if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.TryGetInt32(out var intValue))
        {
            return Math.Clamp(intValue, min, max);
        }

        return defaultValue;
    }

    protected static long? GetLong(JsonElement arguments, string name)
    {
        return arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.TryGetInt64(out var longValue)
            ? longValue
            : null;
    }

    protected static bool GetBool(JsonElement arguments, string name, bool defaultValue = false)
    {
        return arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : defaultValue;
    }

    protected static object EntryJson(FileEntry entry)
    {
        return new
        {
            path = entry.Path,
            name = entry.Name,
            size_bytes = entry.Size,
            type = entry.IsDirectory ? "directory" : "file",
            extension = entry.Extension,
            last_modified = entry.LastModified == default ? null : entry.LastModified.ToString("O"),
            child_count = entry.IsDirectory ? entry.Children.Count : 0,
            flags = new
            {
                is_reparse_point = entry.IsReparsePoint,
                is_access_denied = entry.IsAccessDenied,
                is_cloud_placeholder = entry.IsCloudPlaceholder
            }
        };
    }

    protected static JsonElement Error(string code, string message)
    {
        return Json(new
        {
            ok = false,
            error = new
            {
                code,
                message
            }
        });
    }
}

public sealed class FindByNameTool(IFileTreeQueryService queryService) : FileTreeAgentToolBase(queryService)
{
    public override string Name => "find_by_name";
    public override string Description => "Find scanned files or directories by name using case-insensitive matching.";
    public override JsonElement Schema { get; } = SchemaJson("""
        {"type":"object","properties":{"name":{"type":"string"},"exact_match":{"type":"boolean"},"max_results":{"type":"integer","minimum":1,"maximum":100}},"required":["name"]}
        """);

    public override Task<JsonElement> ExecuteAsync(AgentContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var name = GetString(arguments, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult(Error("invalid_arguments", "name is required."));
        }

        var results = QueryService.FindByName(
            context.Session,
            name,
            GetBool(arguments, "exact_match"),
            GetInt(arguments, "max_results", 25, 1, 100));

        return Task.FromResult(Json(new
        {
            ok = true,
            query = name,
            count = results.Count,
            results = results.Select(EntryJson)
        }));
    }
}

public sealed class FindByPathTool(IFileTreeQueryService queryService) : FileTreeAgentToolBase(queryService)
{
    public override string Name => "find_by_path";
    public override string Description => "Find a scanned file or directory by full path. Windows paths are case-insensitive and slash-normalized.";
    public override JsonElement Schema { get; } = SchemaJson("""
        {"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}
        """);

    public override Task<JsonElement> ExecuteAsync(AgentContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var path = GetString(arguments, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(Error("invalid_arguments", "path is required."));
        }

        var entry = QueryService.FindByPath(context.Session, path);
        return Task.FromResult(entry is null
            ? Error("not_found", $"Path not found in scanned tree: {path}")
            : Json(new { ok = true, result = EntryJson(entry) }));
    }
}

public sealed class ListChildrenTool(IFileTreeQueryService queryService) : FileTreeAgentToolBase(queryService)
{
    public override string Name => "list_children";
    public override string Description => "List direct children of a scanned directory, ordered by descending size.";
    public override JsonElement Schema { get; } = SchemaJson("""
        {"type":"object","properties":{"path":{"type":"string"},"max_results":{"type":"integer","minimum":1,"maximum":200}},"required":["path"]}
        """);

    public override Task<JsonElement> ExecuteAsync(AgentContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var path = GetString(arguments, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(Error("invalid_arguments", "path is required."));
        }

        var entry = QueryService.FindByPath(context.Session, path);
        if (entry is null)
        {
            return Task.FromResult(Error("not_found", $"Path not found in scanned tree: {path}"));
        }

        if (!entry.IsDirectory)
        {
            return Task.FromResult(Error("not_directory", $"Path is not a directory: {path}"));
        }

        var children = QueryService.ListChildren(context.Session, path, GetInt(arguments, "max_results", 50, 1, 200));
        return Task.FromResult(Json(new
        {
            ok = true,
            path = entry.Path,
            count = children.Count,
            children = children.Select(EntryJson)
        }));
    }
}

public sealed class SummarizeSubtreeTool(IFileTreeQueryService queryService) : FileTreeAgentToolBase(queryService)
{
    public override string Name => "summarize_subtree";
    public override string Description => "Summarize a scanned subtree with file/folder counts and largest direct children.";
    public override JsonElement Schema { get; } = SchemaJson("""
        {"type":"object","properties":{"path":{"type":"string"},"top_children":{"type":"integer","minimum":1,"maximum":50}},"required":["path"]}
        """);

    public override Task<JsonElement> ExecuteAsync(AgentContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var path = GetString(arguments, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(Error("invalid_arguments", "path is required."));
        }

        var entry = QueryService.FindByPath(context.Session, path);
        if (entry is null)
        {
            return Task.FromResult(Error("not_found", $"Path not found in scanned tree: {path}"));
        }

        var summary = QueryService.SummarizeSubtree(context.Session, path, GetInt(arguments, "top_children", 20, 1, 50));
        return Task.FromResult(Json(new
        {
            ok = true,
            path = summary.Path,
            name = summary.Name,
            size_bytes = summary.SizeBytes,
            file_count = summary.FileCount,
            directory_count = summary.DirectoryCount,
            last_modified = summary.LastModified?.ToString("O"),
            largest_children = summary.LargestChildren.Select(EntryJson)
        }));
    }
}

public sealed class FindLargeFilesTool(IFileTreeQueryService queryService) : FileTreeAgentToolBase(queryService)
{
    public override string Name => "find_large_files";
    public override string Description => "Find the largest scanned files, optionally under a directory path.";
    public override JsonElement Schema { get; } = SchemaJson("""
        {"type":"object","properties":{"under_path":{"type":"string"},"max_results":{"type":"integer","minimum":1,"maximum":100},"min_size_bytes":{"type":"integer","minimum":0}}}
        """);

    public override Task<JsonElement> ExecuteAsync(AgentContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var underPath = GetString(arguments, "under_path");
        if (!string.IsNullOrWhiteSpace(underPath) && QueryService.FindByPath(context.Session, underPath) is null)
        {
            return Task.FromResult(Error("not_found", $"Path not found in scanned tree: {underPath}"));
        }

        var results = QueryService.FindLargeFiles(
            context.Session,
            underPath,
            GetInt(arguments, "max_results", 25, 1, 100),
            GetLong(arguments, "min_size_bytes"));

        return Task.FromResult(Json(new
        {
            ok = true,
            under_path = underPath,
            count = results.Count,
            results = results.Select(EntryJson)
        }));
    }
}

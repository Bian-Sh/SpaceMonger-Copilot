using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Treemap;

public class SquarifiedTreemapLayout : ITreemapLayoutEngine
{
    private const float HeaderBaseHeight = 18f;
    private const float HeaderMinHeight = 10f;
    private const float BorderPadding = 2f;
    private const float MinNodeDimension = 14f;

    // A directory only expands its children if the content area (after header
    // and border) meets this minimum.  Below this threshold the directory is
    // rendered as a solid colored block — matching SpaceMonger's adaptive
    // depth behavior where small directories become leaf nodes.
    private const float MinContentArea = 800f;  // ~28×28 px equivalent

    // Maximum children to render per directory.
    private const int MaxChildrenPerDir = 20;

    // Drive root (depth 0) gets a neutral off-white. Everything below it
    // cycles through 8 colors matching the original SpaceMonger palette.
    private const string DriveColor = "#F0F0E8";

    private static readonly string[] Palette =
    [
        "#FF7F7F", // salmon / light red
        "#FFBF7F", // peach / light orange
        "#FFFF00", // yellow
        "#7FFF7F", // light green
        "#7FFFFF", // cyan
        "#BFBFFF", // lavender / light blue
        "#BFBFBF", // light gray
        "#FF7FFF", // pink / magenta
    ];

    private struct Rect
    {
        public float X, Y, Width, Height;

        public Rect(float x, float y, float width, float height)
        {
            X = x; Y = y; Width = width; Height = height;
        }

        public float Area => Width * Height;
        public float ShorterSide => Math.Min(Width, Height);
    }

    public List<TreemapNode> ComputeLayout(FileEntry root, float width, float height, int maxDepth)
    {
        var result = new List<TreemapNode>();

        if (root.Size <= 0 || width <= 0 || height <= 0)
            return result;

        var rootRect = new Rect(0, 0, width, height);
        EmitDirectoryNode(root, rootRect, 0, maxDepth, result);

        return result;
    }

    private void EmitDirectoryNode(
        FileEntry dir, Rect rect, int depth, int maxDepth,
        List<TreemapNode> result)
    {
        float headerHeight = GetHeaderHeight(depth, rect.Height);

        string? label = rect.Width >= 40 && headerHeight >= HeaderMinHeight
            ? $"{dir.Name} ({FormatSize(dir.Size)})"
            : null;

        // Depth 0 (drive root) is off-white; everything below cycles the palette.
        string color = depth == 0
            ? DriveColor
            : Palette[(depth - 1) % Palette.Length];

        var node = new TreemapNode
        {
            Entry = dir,
            X = rect.X,
            Y = rect.Y,
            Width = rect.Width,
            Height = rect.Height,
            ColorHex = color,
            Depth = depth,
            IsVisible = rect.Width >= MinNodeDimension && rect.Height >= MinNodeDimension,
            Label = label
        };
        result.Add(node);

        if (depth >= maxDepth)
            return;

        float contentX = rect.X + BorderPadding;
        float contentY = rect.Y + headerHeight;
        float contentW = rect.Width - BorderPadding * 2;
        float contentH = rect.Height - headerHeight - BorderPadding;

        // Area-based pruning: if the content area is too small to show
        // meaningful children, treat this directory as a leaf (solid block).
        if (contentW < MinNodeDimension || contentH < MinNodeDimension
            || contentW * contentH < MinContentArea)
            return;

        var allChildren = dir.Children
            .Where(c => c.Size > 0)
            .OrderByDescending(c => c.Size)
            .ToList();

        if (allChildren.Count == 0)
            return;

        var children = allChildren.Count > MaxChildrenPerDir
            ? allChildren.Take(MaxChildrenPerDir).ToList()
            : allChildren;

        var contentRect = new Rect(contentX, contentY, contentW, contentH);
        SquarifyChildren(children, contentRect, depth + 1, maxDepth, result);
    }

    private void SquarifyChildren(
        List<FileEntry> items, Rect rect, int depth, int maxDepth,
        List<TreemapNode> result)
    {
        if (depth > maxDepth || items.Count == 0 || rect.Area <= 0)
            return;

        if (rect.Width < MinNodeDimension || rect.Height < MinNodeDimension)
            return;

        if (items.Count == 1)
        {
            EmitNode(items[0], rect, depth, maxDepth, result);
            return;
        }

        long totalSize = items.Sum(i => i.Size);
        var normalizedAreas = items
            .Select(i => (float)((double)i.Size / totalSize * rect.Area))
            .ToList();

        LayoutItems(items, normalizedAreas, rect, depth, maxDepth, result);
    }

    private void LayoutItems(
        List<FileEntry> items, List<float> areas, Rect rect,
        int depth, int maxDepth,
        List<TreemapNode> result)
    {
        var remaining = new Rect(rect.X, rect.Y, rect.Width, rect.Height);
        int start = 0;

        while (start < items.Count)
        {
            float shorterSide = remaining.ShorterSide;
            if (shorterSide <= 0)
                break;

            var rowAreas = new List<float> { areas[start] };
            int end = start + 1;

            while (end < items.Count)
            {
                float worstWithout = WorstAspectRatio(rowAreas, shorterSide);
                rowAreas.Add(areas[end]);
                float worstWith = WorstAspectRatio(rowAreas, shorterSide);

                if (worstWith > worstWithout)
                {
                    rowAreas.RemoveAt(rowAreas.Count - 1);
                    break;
                }
                end++;
            }

            int rowCount = end - start;
            remaining = LayoutRow(items, areas, start, rowCount, remaining,
                depth, maxDepth, result);
            start = end;
        }
    }

    private Rect LayoutRow(
        List<FileEntry> items, List<float> areas,
        int startIndex, int count, Rect rect,
        int depth, int maxDepth,
        List<TreemapNode> result)
    {
        float rowTotal = 0;
        for (int i = startIndex; i < startIndex + count; i++)
            rowTotal += areas[i];

        bool layoutHorizontally = rect.Width >= rect.Height;

        float stripThickness;
        if (layoutHorizontally)
            stripThickness = rect.Area > 0 ? rowTotal / rect.Height : 0;
        else
            stripThickness = rect.Area > 0 ? rowTotal / rect.Width : 0;

        float offset = 0;

        for (int i = startIndex; i < startIndex + count; i++)
        {
            float itemLength = stripThickness > 0 ? areas[i] / stripThickness : 0;

            float x, y, w, h;
            if (layoutHorizontally)
            {
                x = rect.X;
                y = rect.Y + offset;
                w = stripThickness;
                h = itemLength;
            }
            else
            {
                x = rect.X + offset;
                y = rect.Y;
                w = itemLength;
                h = stripThickness;
            }

            EmitNode(items[i], new Rect(x, y, w, h), depth, maxDepth, result);
            offset += itemLength;
        }

        if (layoutHorizontally)
            return new Rect(rect.X + stripThickness, rect.Y, rect.Width - stripThickness, rect.Height);
        else
            return new Rect(rect.X, rect.Y + stripThickness, rect.Width, rect.Height - stripThickness);
    }

    private void EmitNode(
        FileEntry entry, Rect rect, int depth, int maxDepth,
        List<TreemapNode> result)
    {
        if (rect.Width < MinNodeDimension || rect.Height < MinNodeDimension)
            return;

        if (entry.IsDirectory)
        {
            EmitDirectoryNode(entry, rect, depth, maxDepth, result);
        }
        else
        {
            // Files sit at the same depth as sibling directories inside
            // the parent's content area, so they use the same depth color.
            string fileColor = depth == 0
                ? DriveColor
                : Palette[(depth - 1) % Palette.Length];

            string? label = rect.Width >= 40 && rect.Height >= 14
                ? $"{entry.Name} ({FormatSize(entry.Size)})"
                : null;

            result.Add(new TreemapNode
            {
                Entry = entry,
                X = rect.X,
                Y = rect.Y,
                Width = rect.Width,
                Height = rect.Height,
                ColorHex = fileColor,
                Depth = depth,
                IsVisible = true,
                Label = label
            });
        }
    }

    private static float GetHeaderHeight(int depth, float availableHeight)
    {
        float h = HeaderBaseHeight - depth * 1.5f;
        h = Math.Max(h, HeaderMinHeight);
        h = Math.Min(h, availableHeight * 0.4f);
        return h;
    }

    private static float WorstAspectRatio(List<float> rowAreas, float sideLength)
    {
        if (rowAreas.Count == 0 || sideLength <= 0)
            return float.MaxValue;

        float rowTotal = 0;
        float minArea = float.MaxValue;
        float maxArea = float.MinValue;

        for (int i = 0; i < rowAreas.Count; i++)
        {
            float a = rowAreas[i];
            rowTotal += a;
            if (a < minArea) minArea = a;
            if (a > maxArea) maxArea = a;
        }

        if (rowTotal <= 0)
            return float.MaxValue;

        float w2 = sideLength * sideLength;
        float r2 = rowTotal * rowTotal;

        float worstForMax = Math.Max(w2 * maxArea / r2, r2 / (w2 * maxArea));
        float worstForMin = Math.Max(w2 * minArea / r2, r2 / (w2 * minArea));

        return Math.Max(worstForMax, worstForMin);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024L) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes < 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F1} TB";
    }
}

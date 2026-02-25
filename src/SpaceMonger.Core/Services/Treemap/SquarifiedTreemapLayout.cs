using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Treemap;

public class SquarifiedTreemapLayout : ITreemapLayoutEngine
{
    private struct Rect
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;

        public Rect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public float Area => Width * Height;
        public float ShorterSide => Math.Min(Width, Height);
    }

    public List<TreemapNode> ComputeLayout(FileEntry root, float width, float height, int maxDepth)
    {
        var result = new List<TreemapNode>();

        if (root.Size <= 0 || width <= 0 || height <= 0)
            return result;

        var children = root.Children
            .Where(c => c.Size > 0)
            .OrderByDescending(c => c.Size)
            .ToList();

        if (children.Count == 0)
            return result;

        var rect = new Rect(0, 0, width, height);
        Squarify(children, rect, 0, maxDepth, result);

        return result;
    }

    private void Squarify(List<FileEntry> items, Rect rect, int depth, int maxDepth, List<TreemapNode> result)
    {
        if (depth > maxDepth || items.Count == 0 || rect.Area <= 0)
            return;

        // Single item: give it the entire rectangle
        if (items.Count == 1)
        {
            LayoutSingleItem(items[0], rect, depth, maxDepth, result);
            return;
        }

        // Normalize sizes to fill the rectangle area proportionally
        long totalSize = items.Sum(i => i.Size);
        var normalizedAreas = items
            .Select(i => (float)((double)i.Size / totalSize * rect.Area))
            .ToList();

        LayoutItems(items, normalizedAreas, rect, depth, maxDepth, result);
    }

    private void LayoutItems(
        List<FileEntry> items,
        List<float> areas,
        Rect rect,
        int depth,
        int maxDepth,
        List<TreemapNode> result)
    {
        var remaining = new Rect(rect.X, rect.Y, rect.Width, rect.Height);
        int start = 0;

        while (start < items.Count)
        {
            float shorterSide = remaining.ShorterSide;

            if (shorterSide <= 0)
                break;

            // Build a row greedily: keep adding items while the worst aspect ratio improves
            var rowAreas = new List<float> { areas[start] };
            int end = start + 1;

            while (end < items.Count)
            {
                float worstWithout = WorstAspectRatio(rowAreas, shorterSide);
                rowAreas.Add(areas[end]);
                float worstWith = WorstAspectRatio(rowAreas, shorterSide);

                if (worstWith > worstWithout)
                {
                    // Adding this item made it worse; remove it and finalize the row
                    rowAreas.RemoveAt(rowAreas.Count - 1);
                    break;
                }

                end++;
            }

            // Layout the finalized row as a strip along the shorter side
            int rowCount = end - start;
            remaining = LayoutRow(
                items, areas, start, rowCount, remaining, depth, maxDepth, result);

            start = end;
        }
    }

    private Rect LayoutRow(
        List<FileEntry> items,
        List<float> areas,
        int startIndex,
        int count,
        Rect rect,
        int depth,
        int maxDepth,
        List<TreemapNode> result)
    {
        float rowTotal = 0;
        for (int i = startIndex; i < startIndex + count; i++)
            rowTotal += areas[i];

        bool layoutHorizontally = rect.Width >= rect.Height;

        // The strip dimension along the layout direction
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

            EmitNode(items[i], x, y, w, h, depth, maxDepth, result);
            offset += itemLength;
        }

        // Return the remaining rectangle after this strip
        if (layoutHorizontally)
        {
            return new Rect(
                rect.X + stripThickness,
                rect.Y,
                rect.Width - stripThickness,
                rect.Height);
        }
        else
        {
            return new Rect(
                rect.X,
                rect.Y + stripThickness,
                rect.Width,
                rect.Height - stripThickness);
        }
    }

    private void LayoutSingleItem(
        FileEntry item, Rect rect, int depth, int maxDepth, List<TreemapNode> result)
    {
        EmitNode(item, rect.X, rect.Y, rect.Width, rect.Height, depth, maxDepth, result);
    }

    private void EmitNode(
        FileEntry entry,
        float x, float y, float width, float height,
        int depth, int maxDepth,
        List<TreemapNode> result)
    {
        bool isVisible = width >= 3 && height >= 3;
        string? label = isVisible && width >= 40 && height >= 16
            ? $"{entry.Name} ({FormatSize(entry.Size)})"
            : null;

        var node = new TreemapNode
        {
            Entry = entry,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            ColorHex = FileTypeColorMap.GetColorHex(entry.Extension, entry.IsDirectory),
            Depth = depth,
            IsVisible = isVisible,
            Label = label
        };

        result.Add(node);

        // Recurse into directory children
        if (entry.IsDirectory)
        {
            var children = entry.Children
                .Where(c => c.Size > 0)
                .OrderByDescending(c => c.Size)
                .ToList();

            if (children.Count > 0)
            {
                var childRect = new Rect(x, y, width, height);
                Squarify(children, childRect, depth + 1, maxDepth, result);
            }
        }
    }

    /// <summary>
    /// Calculates the worst (maximum) aspect ratio for a row of items laid out in a strip.
    /// </summary>
    /// <param name="rowAreas">Normalized areas of items in the row.</param>
    /// <param name="sideLength">The length of the side along which the strip is laid out.</param>
    /// <returns>The worst aspect ratio in the row (1.0 = perfect square).</returns>
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

        // For a row laid out along side of length w:
        // The strip width = rowTotal / w
        // Each item has length = area / stripWidth = area * w / rowTotal
        // Aspect ratio = max(stripWidth / length, length / stripWidth)
        //              = max(rowTotal^2 / (w^2 * area), w^2 * area / rowTotal^2)
        // Worst = max over all items, which is maximized at the min and max area values.
        float w2 = sideLength * sideLength;
        float r2 = rowTotal * rowTotal;

        float worstForMax = Math.Max(w2 * maxArea / r2, r2 / (w2 * maxArea));
        float worstForMin = Math.Max(w2 * minArea / r2, r2 / (w2 * minArea));

        return Math.Max(worstForMax, worstForMin);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024L)
            return $"{bytes} B";

        if (bytes < 1024L * 1024)
            return $"{bytes / 1024.0:F1} KB";

        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";

        if (bytes < 1024L * 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";

        return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F1} TB";
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using SpaceMonger.App.Converters;
using SpaceMonger.Core.Models;

namespace SpaceMonger.App.Controls;

public class TreemapControl : SKElement
{
    private List<TreemapNode>? _nodes;
    private readonly ToolTip _toolTip;
    private TreemapNode? _lastHoveredNode;


    public List<TreemapNode>? Nodes
    {
        get => _nodes;
        set
        {
            _nodes = value;
            InvalidateVisual();
        }
    }

    public event EventHandler<TreemapNode>? NodeClicked;
    public event EventHandler<TreemapNode>? NodeHovered;
    public event EventHandler<TreemapNode>? NodeRightClicked;

    public TreemapControl()
    {
        PaintSurface += OnPaintSurface;

        _toolTip = new ToolTip
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse,
        };
        ToolTip = _toolTip;
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;
        canvas.Clear(SKColor.Parse("#1E1E1E"));

        if (_nodes is null || _nodes.Count == 0)
            return;

        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            IsAntialias = false,
        };

        using var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true,
        };

        using var fileTextPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
        };

        // Render in depth order: shallow first, deep on top.
        // Directories first at each depth, then files on top.
        var sortedNodes = _nodes
            .Where(n => n.IsVisible)
            .OrderBy(n => n.Depth)
            .ThenBy(n => n.Entry.IsDirectory ? 0 : 1)
            .ToList();

        foreach (var node in sortedNodes)
        {
            if (node.Entry.IsDirectory)
            {
                DrawDirectoryNode(canvas, node, fillPaint, borderPaint, textPaint);
            }
            else
            {
                DrawFileNode(canvas, node, fillPaint, borderPaint, fileTextPaint);
            }
        }
    }

    private static void DrawDirectoryNode(
        SKCanvas canvas, TreemapNode node,
        SKPaint fillPaint, SKPaint borderPaint, SKPaint textPaint)
    {
        var fullRect = new SKRect(node.X, node.Y, node.X + node.Width, node.Y + node.Height);

        // Fill the entire directory rect with its assigned palette color.
        // This color shows through as the header bar and the border/frame around children.
        SKColor dirColor = SKColor.Parse(node.ColorHex);
        fillPaint.Color = dirColor;
        canvas.DrawRect(fullRect, fillPaint);

        // Darker border for definition.
        borderPaint.Color = DarkenColor(dirColor, 0.35f);
        borderPaint.StrokeWidth = node.Depth <= 1 ? 2f : 1f;
        canvas.DrawRect(fullRect, borderPaint);

        // Label in the header area — always black text like SpaceMonger.
        if (node.Label is not null)
        {
            float headerHeight = GetHeaderHeight(node.Depth, node.Height);
            float fontSize = Math.Max(8f, Math.Min(headerHeight - 2f, 13f));

            using var headerFont = new SKFont(SKTypeface.Default, fontSize);
            float textWidth = headerFont.MeasureText(node.Label);

            if (textWidth < node.Width - 4)
            {
                float textX = node.X + 3f;
                float textY = node.Y + headerHeight / 2f + fontSize / 2f - 1f;

                textPaint.Color = SKColors.Black;
                canvas.DrawText(node.Label, textX, textY, SKTextAlign.Left, headerFont, textPaint);
            }
        }

        if (node.Entry.IsAccessDenied)
        {
            DrawAccessDeniedOverlay(canvas, fullRect);
        }
    }

    private static void DrawFileNode(
        SKCanvas canvas, TreemapNode node,
        SKPaint fillPaint, SKPaint borderPaint, SKPaint textPaint)
    {
        float pad = 0.5f;
        var rect = new SKRect(
            node.X + pad,
            node.Y + pad,
            node.X + node.Width - pad,
            node.Y + node.Height - pad);

        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        SKColor nodeColor = SKColor.Parse(node.ColorHex);
        fillPaint.Color = nodeColor;
        canvas.DrawRect(rect, fillPaint);

        borderPaint.Color = DarkenColor(nodeColor, 0.25f);
        borderPaint.StrokeWidth = 1f;
        canvas.DrawRect(rect, borderPaint);

        if (node.Label is not null)
        {
            float fontSize = Math.Max(8f, Math.Min(rect.Height - 4f, 11f));
            using var font = new SKFont(SKTypeface.Default, fontSize);
            float textWidth = font.MeasureText(node.Label);

            if (textWidth < rect.Width - 4 && fontSize < rect.Height - 2)
            {
                float textX = rect.MidX - textWidth / 2f;
                float textY = rect.MidY + fontSize / 2f - 1f;
                // Black text on all file labels — matches SpaceMonger.
                textPaint.Color = SKColors.Black;
                canvas.DrawText(node.Label, textX, textY, SKTextAlign.Left, font, textPaint);
            }
        }

        if (node.Entry.IsAccessDenied)
        {
            DrawAccessDeniedOverlay(canvas, rect);
        }
    }

    private static float GetHeaderHeight(int depth, float availableHeight)
    {
        float h = 18f - depth * 1.5f;
        h = Math.Max(h, 10f);
        h = Math.Min(h, availableHeight * 0.4f);
        return h;
    }

    public TreemapNode? HitTest(double x, double y)
    {
        if (_nodes is null || _nodes.Count == 0)
            return null;

        TreemapNode? deepest = null;

        for (int i = _nodes.Count - 1; i >= 0; i--)
        {
            var node = _nodes[i];
            if (!node.IsVisible)
                continue;

            if (x >= node.X && x <= node.X + node.Width &&
                y >= node.Y && y <= node.Y + node.Height)
            {
                if (deepest is null || node.Depth > deepest.Depth)
                {
                    deepest = node;
                }
            }
        }

        return deepest;
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var position = e.GetPosition(this);
        var node = HitTest(position.X, position.Y);
        if (node is not null)
        {
            NodeClicked?.Invoke(this, node);
        }
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var position = e.GetPosition(this);
        var node = HitTest(position.X, position.Y);

        if (node is not null)
        {
            NodeHovered?.Invoke(this, node);

            if (node != _lastHoveredNode)
            {
                // Close first so WPF recalculates position at the new mouse location.
                _toolTip.IsOpen = false;
                _lastHoveredNode = node;
                UpdateToolTipContent(node);
                _toolTip.IsOpen = true;
            }
        }
        else
        {
            _toolTip.IsOpen = false;
            _lastHoveredNode = null;
        }
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _toolTip.IsOpen = false;
        _lastHoveredNode = null;
    }

    private void UpdateToolTipContent(TreemapNode node)
    {
        var entry = node.Entry;
        string fileType = entry.IsDirectory ? "Folder" : (entry.Extension ?? "Unknown");
        string size = FileSizeConverter.FormatSize(entry.Size);
        string modified = entry.LastModified.ToString("g");

        var panel = new StackPanel { Margin = new Thickness(2) };

        panel.Children.Add(new TextBlock
        {
            Text = entry.Path,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400,
        });

        panel.Children.Add(new TextBlock
        {
            Text = $"Size: {size}",
            Margin = new Thickness(0, 2, 0, 0),
        });

        panel.Children.Add(new TextBlock
        {
            Text = $"Type: {fileType}",
            Margin = new Thickness(0, 2, 0, 0),
        });

        panel.Children.Add(new TextBlock
        {
            Text = $"Modified: {modified}",
            Margin = new Thickness(0, 2, 0, 0),
        });

        if (entry.IsAccessDenied)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Access Denied",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 80, 80)),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        _toolTip.Content = panel;
    }

    protected override void OnMouseRightButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        var position = e.GetPosition(this);
        var node = HitTest(position.X, position.Y);
        if (node is not null)
        {
            NodeRightClicked?.Invoke(this, node);
        }
    }

    private static void DrawAccessDeniedOverlay(SKCanvas canvas, SKRect rect)
    {
        using var hatchPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(255, 80, 80, 160),
            StrokeWidth = 1.5f,
            IsAntialias = true,
        };

        canvas.Save();
        canvas.ClipRect(rect);

        float step = 8f;
        float totalSpan = rect.Width + rect.Height;
        for (float offset = 0; offset < totalSpan; offset += step)
        {
            float x0 = rect.Left + offset;
            float y0 = rect.Bottom;
            float x1 = rect.Left + offset - rect.Height;
            float y1 = rect.Top;
            canvas.DrawLine(x0, y0, x1, y1, hatchPaint);
        }

        canvas.Restore();
    }

    private static SKColor DarkenColor(SKColor color, float factor)
    {
        byte r = (byte)(color.Red * (1f - factor));
        byte g = (byte)(color.Green * (1f - factor));
        byte b = (byte)(color.Blue * (1f - factor));
        return new SKColor(r, g, b, color.Alpha);
    }
}

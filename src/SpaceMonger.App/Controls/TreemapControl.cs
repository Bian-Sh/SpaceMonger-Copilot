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

        var visibleNodes = _nodes
            .Where(n => n.IsVisible)
            .OrderBy(n => n.Depth)
            .ToList();

        using var fillPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };

        using var borderPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            IsAntialias = true,
        };

        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
        };

        using var textFont = new SKFont(SKTypeface.Default, 12f);

        foreach (var node in visibleNodes)
        {
            float padding = 1f;
            var rect = new SKRect(
                node.X + padding,
                node.Y + padding,
                node.X + node.Width - padding,
                node.Y + node.Height - padding);

            if (rect.Width <= 0 || rect.Height <= 0)
                continue;

            SKColor nodeColor = SKColor.Parse(node.ColorHex);
            fillPaint.Color = nodeColor;
            canvas.DrawRect(rect, fillPaint);

            borderPaint.Color = DarkenColor(nodeColor, 0.3f);
            canvas.DrawRect(rect, borderPaint);

            // Draw hatched overlay for access-denied entries
            if (node.Entry.IsAccessDenied)
            {
                DrawAccessDeniedOverlay(canvas, rect);
            }

            if (node.Label is not null)
            {
                float textWidth = textFont.MeasureText(node.Label);
                if (textWidth < rect.Width - 4 && textFont.Size < rect.Height - 4)
                {
                    float textX = rect.MidX - textWidth / 2f;
                    float textY = rect.MidY + textFont.Size / 2f - 2f;
                    canvas.DrawText(node.Label, textX, textY, SKTextAlign.Left, textFont, textPaint);
                }
            }
        }
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
                _lastHoveredNode = node;
                UpdateToolTipContent(node);
            }

            _toolTip.IsOpen = true;
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

    /// <summary>
    /// Draws diagonal hatched lines over a rectangle to indicate access-denied entries.
    /// </summary>
    private static void DrawAccessDeniedOverlay(SKCanvas canvas, SKRect rect)
    {
        using var hatchPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(255, 80, 80, 160), // Semi-transparent red
            StrokeWidth = 1.5f,
            IsAntialias = true,
        };

        // Save canvas state and clip to the node rectangle
        canvas.Save();
        canvas.ClipRect(rect);

        // Draw diagonal lines from bottom-left to top-right across the rectangle
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

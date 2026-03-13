using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StdfAnalyzer.Models;

namespace StdfAnalyzer.Controls;

public class WaferMapControl : Control
{
    public static readonly DependencyProperty PartsProperty =
        DependencyProperty.Register(nameof(Parts), typeof(List<PartData>), typeof(WaferMapControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public List<PartData>? Parts
    {
        get => (List<PartData>?)GetValue(PartsProperty);
        set => SetValue(PartsProperty, value);
    }

    private static readonly Brush PassBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80));
    private static readonly Brush FailBrush = new SolidColorBrush(Color.FromRgb(244, 67, 54));
    private static readonly Pen BorderPen = new(new SolidColorBrush(Color.FromRgb(40, 40, 40)), 0.5);
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    static WaferMapControl()
    {
        PassBrush.Freeze();
        FailBrush.Freeze();
        BorderPen.Freeze();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 30, 30)), null,
            new Rect(0, 0, ActualWidth, ActualHeight));

        var parts = Parts;
        if (parts == null || parts.Count == 0)
        {
            DrawCenteredText(dc, "无晶圆数据", ActualWidth / 2, ActualHeight / 2, 16, Brushes.Gray);
            return;
        }

        int minX = parts.Min(p => p.XCoord);
        int maxX = parts.Max(p => p.XCoord);
        int minY = parts.Min(p => p.YCoord);
        int maxY = parts.Max(p => p.YCoord);

        int rangeX = maxX - minX + 1;
        int rangeY = maxY - minY + 1;

        if (rangeX <= 0 || rangeY <= 0) return;

        double margin = 40;
        double availW = ActualWidth - margin * 2;
        double availH = ActualHeight - margin * 2 - 30;

        double cellW = availW / rangeX;
        double cellH = availH / rangeY;
        double cellSize = Math.Min(cellW, cellH);
        cellSize = Math.Max(cellSize, 1);

        double mapW = cellSize * rangeX;
        double mapH = cellSize * rangeY;
        double offsetX = (ActualWidth - mapW) / 2;
        double offsetY = margin;

        foreach (var p in parts)
        {
            double x = offsetX + (p.XCoord - minX) * cellSize;
            double y = offsetY + (p.YCoord - minY) * cellSize;
            Brush fill = p.Pass ? PassBrush : FailBrush;
            dc.DrawRectangle(fill, BorderPen, new Rect(x, y, cellSize, cellSize));
        }

        double legendY = offsetY + mapH + 10;
        double legendX = offsetX;
        DrawLegendItem(dc, legendX, legendY, PassBrush, "Pass");
        DrawLegendItem(dc, legendX + 80, legendY, FailBrush, "Fail");
    }

    private static void DrawLegendItem(DrawingContext dc, double x, double y, Brush color, string label)
    {
        dc.DrawRectangle(color, null, new Rect(x, y, 12, 12));
        DrawCenteredText(dc, label, x + 20 + 25, y + 6, 11, Brushes.White, HorizontalAlignment.Left);
    }

    private static void DrawCenteredText(DrawingContext dc, string text, double x, double y, double size,
        Brush brush, HorizontalAlignment align = HorizontalAlignment.Center)
    {
        var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, LabelTypeface, size, brush,
            VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip);

        double drawX = align == HorizontalAlignment.Center ? x - ft.Width / 2 : x;
        double drawY = y - ft.Height / 2;
        dc.DrawText(ft, new Point(drawX, drawY));
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using StdfAnalyzer.Models;

namespace StdfAnalyzer.Controls;

public class WaferMapControl : Control
{
    public static readonly DependencyProperty PartsProperty =
        DependencyProperty.Register(nameof(Parts), typeof(List<PartData>), typeof(WaferMapControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnPartsChanged));

    public static readonly DependencyProperty ShowHBinProperty =
        DependencyProperty.Register(nameof(ShowHBin), typeof(bool), typeof(WaferMapControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnBinModeChanged));

    public List<PartData>? Parts
    {
        get => (List<PartData>?)GetValue(PartsProperty);
        set => SetValue(PartsProperty, value);
    }

    public bool ShowHBin
    {
        get => (bool)GetValue(ShowHBinProperty);
        set => SetValue(ShowHBinProperty, value);
    }

    private static readonly Brush PassBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80));
    private static readonly Brush FailBrush = new SolidColorBrush(Color.FromRgb(244, 67, 54));
    private static readonly Brush BgBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));
    private static readonly Brush TextBrush = Brushes.White;
    private static readonly Brush TextShadowBrush = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0));
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    static WaferMapControl()
    {
        PassBrush.Freeze();
        FailBrush.Freeze();
        BgBrush.Freeze();
        TextShadowBrush.Freeze();
    }

    private double _zoom = 1.0;
    private double _panX, _panY;
    private Point _lastMouse;
    private bool _isPanning;
    private double _dpi = 1.0;

    // Cached map geometry (frozen DrawingGroup for GPU-friendly rendering)
    private DrawingGroup? _mapCache;
    private Dictionary<long, PartData>? _coordLookup;
    private int _minX, _minY, _rangeX, _rangeY;
    private double _baseCellSize;
    private bool _layoutDirty = true;

    // Cached text objects per SBin value
    private readonly Dictionary<ushort, FormattedText> _textCache = new();
    private readonly Dictionary<ushort, FormattedText> _shadowTextCache = new();
    private double _cachedFontSize;

    private const double ZoomMin = 0.5;
    private const double ZoomMax = 40.0;
    private const double ZoomStep = 1.15;
    private const double MinCellForText = 18.0;
    private const double MapPadding = 40.0;

    public WaferMapControl()
    {
        ClipToBounds = true;
        Focusable = true;
        SizeChanged += (_, _) => _layoutDirty = true;
    }

    private static void OnBinModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (WaferMapControl)d;
        ctrl._textCache.Clear();
        ctrl._shadowTextCache.Clear();
        ctrl.InvalidateVisual();
    }

    private static void OnPartsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (WaferMapControl)d;
        ctrl._zoom = 1.0;
        ctrl._panX = 0;
        ctrl._panY = 0;
        ctrl._mapCache = null;
        ctrl._coordLookup = null;
        ctrl._layoutDirty = true;
        ctrl._textCache.Clear();
        ctrl._shadowTextCache.Clear();
        ctrl.InvalidateVisual();
    }

    private void RebuildCache()
    {
        _mapCache = null;
        _coordLookup = null;
        _layoutDirty = false;

        var parts = Parts;
        if (parts == null || parts.Count == 0) return;

        _minX = int.MaxValue;
        _minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;
        foreach (var p in parts)
        {
            if (p.XCoord < _minX) _minX = p.XCoord;
            if (p.XCoord > maxX) maxX = p.XCoord;
            if (p.YCoord < _minY) _minY = p.YCoord;
            if (p.YCoord > maxY) maxY = p.YCoord;
        }
        _rangeX = maxX - _minX + 1;
        _rangeY = maxY - _minY + 1;
        if (_rangeX <= 0 || _rangeY <= 0) return;

        double availW = ActualWidth - MapPadding * 2;
        double availH = ActualHeight - MapPadding * 2 - 30;
        _baseCellSize = Math.Max(Math.Min(availW / _rangeX, availH / _rangeY), 1.0);

        // Unit-coordinate pen: border width relative to 1-unit cell
        var unitPen = new Pen(new SolidColorBrush(Color.FromRgb(50, 50, 50)), 0.06);
        unitPen.Freeze();

        _coordLookup = new Dictionary<long, PartData>(parts.Count);
        var dg = new DrawingGroup();
        using (var dc = dg.Open())
        {
            foreach (var p in parts)
            {
                int dx = p.XCoord - _minX;
                int dy = p.YCoord - _minY;
                Brush fill = p.Pass ? PassBrush : FailBrush;
                dc.DrawRectangle(fill, unitPen, new Rect(dx, dy, 1, 1));
                _coordLookup[CoordKey(dx, dy)] = p;
            }
        }
        dg.Freeze();
        _mapCache = dg;
    }

    private static long CoordKey(int dx, int dy) => ((long)dy << 32) | (uint)dx;

    #region Mouse Interaction

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Parts == null || Parts.Count == 0) return;

        var mousePos = e.GetPosition(this);
        double oldZoom = _zoom;
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep), ZoomMin, ZoomMax);

        double s = _zoom / oldZoom;
        _panX = mousePos.X - s * (mousePos.X - _panX);
        _panY = mousePos.Y - s * (mousePos.Y - _panY);

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Parts == null || Parts.Count == 0) return;
        _lastMouse = e.GetPosition(this);
        _isPanning = true;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isPanning) return;
        var pos = e.GetPosition(this);
        _panX += pos.X - _lastMouse.X;
        _panY += pos.Y - _lastMouse.Y;
        _lastMouse = pos;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_isPanning) { _isPanning = false; ReleaseMouseCapture(); }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        _zoom = 1.0;
        _panX = 0;
        _panY = 0;
        InvalidateVisual();
    }

    #endregion

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        _dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        dc.DrawRectangle(BgBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var parts = Parts;
        if (parts == null || parts.Count == 0)
        {
            var ft = MakeText("无晶圆数据", 16, Brushes.Gray);
            dc.DrawText(ft, new Point((ActualWidth - ft.Width) / 2, (ActualHeight - ft.Height) / 2));
            return;
        }

        if (_layoutDirty || _mapCache == null) RebuildCache();
        if (_mapCache == null || _rangeX <= 0 || _rangeY <= 0) return;

        double cellSize = _baseCellSize * _zoom;
        double baseOx = (ActualWidth - _baseCellSize * _rangeX) / 2;
        double ox = baseOx * _zoom + _panX;
        double oy = MapPadding * _zoom + _panY;

        // Draw all die rectangles via cached frozen DrawingGroup + transform
        dc.PushTransform(new MatrixTransform(cellSize, 0, 0, cellSize, ox, oy));
        dc.DrawDrawing(_mapCache);
        dc.Pop();

        // Draw bin text only for visible cells when zoomed in enough
        bool useHBin = ShowHBin;
        if (cellSize >= MinCellForText && _coordLookup != null)
        {
            double fontSize = Math.Clamp(cellSize * 0.38, 7, 28);
            EnsureTextCache(fontSize, parts, useHBin);

            int visMinDx = Math.Max(0, (int)Math.Floor(-ox / cellSize));
            int visMaxDx = Math.Min(_rangeX - 1, (int)Math.Ceiling((ActualWidth - ox) / cellSize));
            int visMinDy = Math.Max(0, (int)Math.Floor(-oy / cellSize));
            int visMaxDy = Math.Min(_rangeY - 1, (int)Math.Ceiling((ActualHeight - oy) / cellSize));

            for (int dy = visMinDy; dy <= visMaxDy; dy++)
            {
                for (int dx = visMinDx; dx <= visMaxDx; dx++)
                {
                    if (!_coordLookup.TryGetValue(CoordKey(dx, dy), out var p)) continue;

                    ushort binVal = useHBin ? p.HardBin : p.SoftBin;
                    if (_textCache.TryGetValue(binVal, out var ft))
                    {
                        double sx = ox + dx * cellSize + (cellSize - ft.Width) / 2;
                        double sy = oy + dy * cellSize + (cellSize - ft.Height) / 2;

                        if (_shadowTextCache.TryGetValue(binVal, out var shadow))
                            dc.DrawText(shadow, new Point(sx + 0.8, sy + 0.8));
                        dc.DrawText(ft, new Point(sx, sy));
                    }
                }
            }
        }

        // Legend
        double legendY = oy + _rangeY * cellSize + 8;
        if (legendY > 0 && legendY < ActualHeight)
        {
            dc.DrawRectangle(PassBrush, null, new Rect(ox, legendY, 12, 12));
            var ftPass = MakeText("Pass", 11, Brushes.White);
            dc.DrawText(ftPass, new Point(ox + 16, legendY + (12 - ftPass.Height) / 2));
            dc.DrawRectangle(FailBrush, null, new Rect(ox + 60, legendY, 12, 12));
            var ftFail = MakeText("Fail", 11, Brushes.White);
            dc.DrawText(ftFail, new Point(ox + 76, legendY + (12 - ftFail.Height) / 2));
        }

        // Zoom indicator
        if (Math.Abs(_zoom - 1.0) >= 0.01)
        {
            var ftZoom = MakeText($"{_zoom * 100:F0}%", 11, Brushes.Gray);
            dc.DrawText(ftZoom, new Point(ActualWidth - ftZoom.Width - 12, ActualHeight - ftZoom.Height - 8));
            var ftHint = MakeText("右键还原", 10, Brushes.DimGray);
            dc.DrawText(ftHint, new Point(ActualWidth - ftZoom.Width - ftHint.Width - 20, ActualHeight - ftHint.Height - 8));
        }
    }

    private bool _cachedUseHBin;

    private void EnsureTextCache(double fontSize, List<PartData> parts, bool useHBin)
    {
        if (Math.Abs(fontSize - _cachedFontSize) < 0.5 && _cachedUseHBin == useHBin && _textCache.Count > 0)
            return;

        _textCache.Clear();
        _shadowTextCache.Clear();
        _cachedFontSize = fontSize;
        _cachedUseHBin = useHBin;

        var seenBins = new HashSet<ushort>();
        foreach (var p in parts)
        {
            ushort binVal = useHBin ? p.HardBin : p.SoftBin;
            if (!seenBins.Add(binVal)) continue;
            var text = binVal.ToString();
            _textCache[binVal] = MakeText(text, fontSize, TextBrush);
            _shadowTextCache[binVal] = MakeText(text, fontSize, TextShadowBrush);
        }
    }

    private FormattedText MakeText(string text, double size, Brush brush)
    {
        return new FormattedText(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, LabelTypeface, size, brush, _dpi);
    }
}

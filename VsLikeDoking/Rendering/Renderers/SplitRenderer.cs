using System;
using System.Drawing;

using VsLikeDoking.Rendering.Theme;
using VsLikeDoking.Utils;

namespace VsLikeDoking.Rendering.Renderers
{
  /// <summary>스필리터(가로/세로)를 그리는 부분 렌더러이다.</summary>
  /// <remarks>기본은 단색이지만 hot/dragging 상태일 때 강조색을 쓰도록 하면 VS 느낌이 난다.</remarks>
  public class SplitRenderer
  {
    // Fields ==========================================================================

    private ColorPalette _Palette;
    private DockMetrics _Metrics;

    // Properties ================================================================

    public ColorPalette Palette
    {
      get { return _Palette; }
      set { _Palette = value ?? throw new ArgumentNullException(nameof(value)); }
    }

    public DockMetrics Metrics
    {
      get { return _Metrics; }
      set { _Metrics = (value ?? throw new ArgumentNullException(nameof(value))).Normalize(); }
    }

    // Ctor ======================================================================

    public SplitRenderer(ColorPalette palette, DockMetrics metrics)
    {
      _Palette = palette ?? throw new ArgumentNullException(nameof(palette));
      _Metrics = (metrics ?? throw new ArgumentNullException(nameof(metrics))).Normalize();
    }

    // Draw =====================================================================

    /// <summary>스플리터를 그린다.</summary>
    public void DrawSplitter(Graphics g, Rectangle bounds, bool hot = false, bool dragging = false)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));

      var color = _Palette[ColorPalette.Role.Splitter];

      if (dragging) color = MathEx.Mix(color, _Palette[ColorPalette.Role.Accent], 0.45);
      else if (hot) color = MathEx.Mix(color, _Palette[ColorPalette.Role.Accent], 0.25);

      using var brush = new SolidBrush(color);
      g.FillRectangle(brush, bounds);
    }

    /// <summary>도킹 프리뷰(반투명 채움 + 테두리)를 그린다.</summary>
    public void DrawDockPreview(Graphics g, Rectangle bounds)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));

      using (var brush = new SolidBrush(_Palette[ColorPalette.Role.DockPreviewFill])) g.FillRectangle(brush, bounds);

      int thick = Math.Max(1, _Metrics.DockPreviewBorderThickness);
      using var pen = new Pen(_Palette[ColorPalette.Role.DockPreviewBorder], thick);
      g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }
  }
}
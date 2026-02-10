using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

using VsLikeDoking.Rendering.Theme;

namespace VsLikeDoking.Rendering.Renderers
{
  /// <summary>도구창/패널 상단 캡션(제목 바) 과 그 안의 버튼(닫기 등)을 그리는 부분 렌더러다.</summary>
  /// <remarks>VsDockRenderer가 호출한다.</remarks>
  public sealed class CaptionRenderer
  {
    // Fields ====================================================================

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
      set { _Metrics = value ?? throw new ArgumentNullException(nameof(value)); }
    }

    // Ctor ======================================================================

    public CaptionRenderer(ColorPalette palette, DockMetrics metrics)
    {
      _Palette = palette ?? throw new ArgumentNullException(nameof(palette));
      _Metrics = (metrics ?? throw new ArgumentNullException(nameof(metrics))).Normalize();
    }

    // Draw =====================================================================

    /// <summary>켑션(헤더) 전체를 그린다.</summary>
    public void DrawCaption(Graphics g, Rectangle bounds, string title, DockRenderer.CaptionVisualState state, bool showCloseButton, bool closeHot = false, bool closePressed = false)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));
      title ??= string.Empty;

      var back = _Palette[(state == DockRenderer.CaptionVisualState.Active) ? ColorPalette.Role.CaptionBackActive : ColorPalette.Role.CaptionBack];
      var textColor = _Palette[(state == DockRenderer.CaptionVisualState.Active) ? ColorPalette.Role.CaptionTextActive : ColorPalette.Role.CaptionText];

      using (var brush = new SolidBrush(back)) g.FillRectangle(brush, bounds);
      DrawPanelBorder(g, bounds);
      ComputeLayout(bounds, showCloseButton, out var textRect, out var closeRect);
      DrawText(g, textRect, title, textColor);
      if (showCloseButton) DrawCloseButton(g, closeRect, closeHot, closePressed);
    }

    // Layout ===================================================================


    /// <summary>캡션 텍스트/닫기 버튼 영역을 계산한다.</summary>
    public void ComputeLayout(Rectangle bounds, bool showCloseButton, out Rectangle textRect, out Rectangle closeButtonRect)
    {
      var m = _Metrics;


      textRect = Rectangle.Inflate(bounds, -m.CaptionPaddingX, 0);
      closeButtonRect = Rectangle.Empty;

      if (showCloseButton)
      {
        int size = m.CaptionButtonSize;
        int cx = bounds.Right - m.CaptionPaddingX - size;
        int cy = bounds.Y + (bounds.Height - size) / 2;
        closeButtonRect = new Rectangle(cx, cy, size, size);

        textRect = Rectangle.FromLTRB(textRect.Left, textRect.Top, Math.Max(textRect.Left, closeButtonRect.Left - 6), textRect.Bottom);
      }
      if (textRect.Width < 1) textRect = new Rectangle(textRect.X, textRect.Y, 1, textRect.Height);
      if (textRect.Height < 1) textRect = new Rectangle(textRect.X, textRect.Y, textRect.Width, 1);
    }

    /// <summary>닫기 버튼 히트 테스트.</summary>
    public bool HitTestCloseButton(Rectangle captionBounds, Point clientPoint)
    {
      ComputeLayout(captionBounds, true, out _, out var closeRect);
      return closeRect.Contains(clientPoint);
    }

    // Text/Glyph ===============================================================

    /// <summary>캡션 텍스트를 그린다.</summary>
    public void DrawText(Graphics g, Rectangle textBounds, string text, Color color)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));
      text ??= string.Empty;

      var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
      using var font = CreateFont(_Metrics.CaptionFont);
      TextRenderer.DrawText(g, text, font, textBounds, color, flags);
    }

    /// <summary>닫기 버튼(배경/글리프)을 그린다.</summary>
    public void DrawCloseButton(Graphics g, Rectangle bounds, bool hot, bool pressed)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));

      if (hot || pressed)
      {
        using var brush = new SolidBrush(_Palette[ColorPalette.Role.CaptionButtonBackHot]);
        g.FillRectangle(brush, bounds);
      }
      DrawCloseGlyph(g, bounds, _Palette[ColorPalette.Role.CaptionButtonGlyph]);
    }

    private void DrawCloseGlyph(Graphics g, Rectangle bounds, Color color)
    {
      int w = Math.Max(1, _Metrics.GlyphStrokeWidth);
      int pad = 4;

      var rect = Rectangle.Inflate(bounds, -pad, -pad);
      if (rect.Width < 2 || rect.Height < 2) return;

      using var pen = new Pen(color, w);
      pen.Alignment = PenAlignment.Center;

      g.DrawLine(pen, rect.Left, rect.Top, rect.Right, rect.Bottom);
      g.DrawLine(pen, rect.Left, rect.Bottom, rect.Right, rect.Top);
    }

    // Helpers ==================================================================

    private void DrawPanelBorder(Graphics g, Rectangle bounds)
    {
      int t = _Metrics.PanelBorderThickness;
      if (t <= 0) return;

      using var pen = new Pen(_Palette[ColorPalette.Role.PanelBorder], t);
      g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    private static Font CreateFont(FontSpec spec)
    {
      spec = spec.Normalize();
      return new Font(spec.Family, spec.Size, spec.Style, GraphicsUnit.Point);
    }

  }
}

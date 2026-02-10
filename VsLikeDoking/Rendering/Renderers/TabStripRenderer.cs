using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

using VsLikeDoking.Rendering.Theme;

namespace VsLikeDoking.Rendering.Renderers
{
  /// <summary>탭 스트립 배경/탭 1개(텍스트+닫기버튼)의 그리기 + 기본 레이아웃 계산(텍스트 영역/닫기 버튼 영역)을 담당</summary>
  /// <remarks>VsDockRenderer가 호출한다.</remarks>
  public sealed class TabStripRenderer
  {
    // Fields ===================================================================

    private ColorPalette _Palette;
    private DockMetrics _Metrics;

    // Properties ===============================================================

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

    // Ctor =====================================================================

    public TabStripRenderer(ColorPalette palette, DockMetrics metrics)
    {
      _Palette = palette ?? throw new ArgumentNullException(nameof(palette));
      _Metrics = (metrics ?? throw new ArgumentNullException(nameof(metrics))).Normalize();
    }

    // Draw ====================================================================

    /// <summary>탭 스트립 배경을 그린다.</summary>
    public void DrawBackground(Graphics g, Rectangle bounds)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));

      using var brush = new SolidBrush(_Palette[ColorPalette.Role.TabStripBack]);
      g.FillRectangle(brush, bounds);

      DrawPanelBorder(g, bounds);
    }

    /// <summary>탭 1개를 그린다.</summary>
    public void DrawTab(Graphics g, Rectangle bounds, string text, DockRenderer.TabVisualState state, bool showCloseButton, bool closeHot = false, bool closePressed = false)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));
      text ??= string.Empty;

      var back = GetTabBackColor(state);
      var border = _Palette[ColorPalette.Role.TabBorder];
      var textColor = GetTabTextColor(state);

      using (var brush = new SolidBrush(back)) g.FillRectangle(brush, bounds);
      using (var pen = new Pen(border)) g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);

      ComputeTabLayout(bounds, showCloseButton, out var textRect, out var closeRect);
      DrawTabText(g, textRect, text, textColor);

      if (showCloseButton) DrawCloseButton(g, closeRect, closeHot, closePressed, textColor);
    }

    // Layout ===================================================================

    /// <summary>탭의 텍스트/닫기 버튼 영역을 계산한다.</summary>
    public void ComputeTabLayout(Rectangle tabBounds, bool showCloseButton, out Rectangle textRect, out Rectangle closeButtonRect)
    {
      var m = _Metrics;

      var inner = Rectangle.Inflate(tabBounds, -m.TabPaddingX, -m.TabPaddingY);
      if (inner.Width < 1) inner.Width = 1;
      if (inner.Height < 1) inner.Height = 1;

      closeButtonRect = Rectangle.Empty;

      if (showCloseButton)
      {
        int size = m.TabCloseButtonSize;
        int cx = tabBounds.Right - m.TabCloseButtonPaddingRight - size;
        int cy = tabBounds.Y + (tabBounds.Height - size) / 2;
        closeButtonRect = new Rectangle(cx, cy, size, size);

        int rightLimit = Math.Max(inner.Left, closeButtonRect.Left - 4);
        textRect = Rectangle.FromLTRB(inner.Left, inner.Top, rightLimit, inner.Bottom);
      }
      else
      {
        textRect = inner;
      }

      if (textRect.Width < 1) textRect = new Rectangle(textRect.X, textRect.Y, 1, textRect.Height);
      if (textRect.Height < 1) textRect = new Rectangle(textRect.X, textRect.Y, textRect.Width, 1);
    }

    /// <summary>닫기 버튼 히트 테스트를 수행한다.</summary>
    public bool HitTestCloseButton(Rectangle tabBounds, Point clientPoint)
    {
      ComputeTabLayout(tabBounds, true, out _, out var closeRect);
      return closeRect.Contains(clientPoint);
    }

    // Text/Glyph ===============================================================

    /// <summary>탭 텍스트를 그린다.</summary>
    public void DrawTabText(Graphics g, Rectangle textBounds, string text, Color color)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));
      text ??= string.Empty;

      var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
      using var font = CreateFont(_Metrics.TabFont);
      TextRenderer.DrawText(g, text, font, textBounds, color, flags);
    }

    /// <summary>닫기 버튼(배경/글리프)을 그린다.</summary>
    public void DrawCloseButton(Graphics g, Rectangle bounds, bool hot, bool pressed, Color fallbackGlyphColor)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));

      if (hot || pressed)
      {
        using var brush = new SolidBrush(_Palette[ColorPalette.Role.CaptionButtonBackHot]);
        g.FillRectangle(brush, bounds);
      }

      var glyphColor = _Palette[ColorPalette.Role.CaptionButtonGlyph];
      if (glyphColor.A == 0) glyphColor = fallbackGlyphColor;

      DrawCloseGlyph(g, bounds, glyphColor);
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
      int thick = _Metrics.PanelBorderThickness;
      if (thick <= 0) return;

      using var pen = new Pen(_Palette[ColorPalette.Role.PanelBorder], thick);
      g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    private Color GetTabBackColor(DockRenderer.TabVisualState state)
    {
      return state switch
      {
        DockRenderer.TabVisualState.Active => _Palette[ColorPalette.Role.TabBackActive],
        DockRenderer.TabVisualState.Hot => _Palette[ColorPalette.Role.TabBackHot],
        _ => _Palette[ColorPalette.Role.TabBack],
      };
    }

    private Color GetTabTextColor(DockRenderer.TabVisualState state)
    {
      return state switch
      {
        DockRenderer.TabVisualState.Active => _Palette[ColorPalette.Role.TabTextActive],
        DockRenderer.TabVisualState.Disabled => _Palette[ColorPalette.Role.TextDisabled],
        _ => _Palette[ColorPalette.Role.TabText],
      };
    }

    private static Font CreateFont(FontSpec spec)
    {
      spec = spec.Normalize();
      return new Font(spec.Family, spec.Size, spec.Style, GraphicsUnit.Point);
    }
  }
}

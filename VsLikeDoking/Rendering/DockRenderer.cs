// VsLikeDocking - VsLikeDoking - Rendering/DockRenderer.cs - DockRenderer - (File)

using System;
using System.Drawing;
using System.Windows.Forms;

using VsLikeDoking.Rendering.Theme;

namespace VsLikeDoking.Rendering
{
  /// <summary>도킹 UI 렌더링의 공통 베이스.</summary>
  /// <remarks>실제 VS 스타일은 VsDockRenderer 같은 파생 클래스로 구현한다.</remarks>
  public abstract class DockRenderer
  {
    // Types =====================================================================================

    public enum TabVisualState { Normal = 0, Hot = 1, Active = 2, Disabled = 3 }
    public enum CaptionVisualState { Inactive = 0, Active = 1 }

    // Fields =====================================================================================

    private ColorPalette _Palette;
    private DockMetrics _Metrics;

    // Properties ==================================================================================

    /// <summary>색상 팔레트</summary>
    public ColorPalette Palette
    {
      get { return _Palette; }
      set { _Palette = value ?? throw new ArgumentNullException(nameof(value)); }
    }

    /// <summary>픽셀 규격(높이/패딩/두께 등)</summary>
    public DockMetrics Metrics
    {
      get { return _Metrics; }
      set { _Metrics = (value ?? throw new ArgumentNullException(nameof(value))).Normalize(); }
    }

    // Ctor ========================================================================================

    protected DockRenderer(ColorPalette? palette = null, DockMetrics? metrics = null)
    {
      _Palette = palette ?? ColorPalette.CreateDark();
      _Metrics = (metrics ?? DockMetrics.CreateVsLike()).Normalize();
    }

    // Tab strip ===================================================================================

    /// <summary>탭 스트립 배경을 그린다.</summary>
    public virtual void DrawTabStripBackground(Graphics g, Rectangle bounds)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));

      using var b = new SolidBrush(Palette[ColorPalette.Role.TabStripBack]);
      g.FillRectangle(b, bounds);

      DrawPanelBorder(g, bounds);
    }

    /// <summary>탭 하나를 그린다.</summary>
    public virtual void DrawTab(Graphics g, Rectangle bounds, string text, TabVisualState state, bool showCloseButton, bool closeHot = false, bool closePressed = false)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));
      if (text is null) text = string.Empty;

      var back = GetTabBackColor(state);
      var border = Palette[ColorPalette.Role.TabBorder];
      var textColor = GetTabTextColor(state);

      using (var b = new SolidBrush(back)) g.FillRectangle(b, bounds);
      using (var p = new Pen(border)) g.DrawRectangle(p, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);

      // Close 버튼을 "실제로 표시"하는 프레임에만 여백을 예약한다.
      var closeVisible = showCloseButton && IsTabCloseButtonVisible(state, closeHot, closePressed);

      ComputeTabLayout(bounds, closeVisible, out var textRect, out var closeRect);

      DrawTabText(g, textRect, text, textColor);

      if (closeVisible)
        DrawTabCloseButton(g, closeRect, state, closeHot, closePressed);
    }

    /// <summary>탭의 텍스트/닫기버튼 배치를 계산한다.</summary>
    public virtual void ComputeTabLayout(Rectangle tabBounds, bool showCloseButton, out Rectangle textRect, out Rectangle closeButtonRect)
    {
      var m = Metrics;

      var inner = Rectangle.Inflate(tabBounds, -m.TabPaddingX, -m.TabPaddingY);
      inner.Height = Math.Max(1, inner.Height);

      closeButtonRect = Rectangle.Empty;

      if (showCloseButton)
      {
        int size = m.TabCloseButtonSize;
        int closeX = tabBounds.Right - m.TabCloseButtonPaddingRight - size;
        int closeY = tabBounds.Y + (tabBounds.Height - size) / 2;
        closeButtonRect = new Rectangle(closeX, closeY, size, size);

        int rightLimit = Math.Max(inner.X, closeButtonRect.Left - 4);
        textRect = Rectangle.FromLTRB(inner.Left, inner.Top, rightLimit, inner.Bottom);
      }
      else
        textRect = inner;

      if (textRect.Width < 1) textRect = new Rectangle(textRect.X, textRect.Y, 1, textRect.Height);
      if (textRect.Height < 1) textRect = new Rectangle(textRect.X, textRect.Y, textRect.Width, 1);
    }

    /// <summary>탭 텍스트를 그린다.</summary>
    public virtual void DrawTabText(Graphics g, Rectangle textBounds, string text, Color color)
    {
      var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
      TextRenderer.DrawText(g, text, SystemFonts.MessageBoxFont, textBounds, color, flags);
    }

    /// <summary>닫기 버튼(배경/글리프)을 그린다.</summary>
    public virtual void DrawCloseButton(Graphics g, Rectangle bounds, bool hot, bool pressed, Color glyphColor)
    {
      if (hot || pressed)
      {
        var back = Palette[ColorPalette.Role.CaptionButtonBackHot];
        using var b = new SolidBrush(back);
        g.FillRectangle(b, bounds);
      }

      DrawCloseGlyph(g, bounds, glyphColor);
    }

    /// <summary>닫기(X) 글리프를 그린다.</summary>
    public virtual void DrawCloseGlyph(Graphics g, Rectangle bounds, Color color)
    {
      int w = Math.Max(1, Metrics.GlyphStrokeWidth);
      var pad = 4;

      var r = Rectangle.Inflate(bounds, -pad, -pad);
      if (r.Width < 2 || r.Height < 2) return;

      using var p = new Pen(color, w);
      p.Alignment = System.Drawing.Drawing2D.PenAlignment.Center;

      g.DrawLine(p, r.Left, r.Top, r.Right, r.Bottom);
      g.DrawLine(p, r.Left, r.Bottom, r.Right, r.Top);
    }

    // Caption =====================================================================================

    /// <summary>도구창 캡션(헤더)을 그린다.</summary>
    public virtual void DrawCaption(Graphics g, Rectangle bounds, string title, CaptionVisualState state, bool showCloseButton, bool closeHot = false, bool closePressed = false)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));
      if (title is null) title = string.Empty;

      var back = Palette[(state == CaptionVisualState.Active) ? ColorPalette.Role.CaptionBackActive : ColorPalette.Role.CaptionBack];
      var textColor = Palette[(state == CaptionVisualState.Active) ? ColorPalette.Role.CaptionTextActive : ColorPalette.Role.CaptionText];

      using (var b = new SolidBrush(back)) g.FillRectangle(b, bounds);

      DrawPanelBorder(g, bounds);

      var textRect = Rectangle.Inflate(bounds, -Metrics.CaptionPaddingX, 0);
      var closeRect = Rectangle.Empty;

      if (showCloseButton)
      {
        int size = Metrics.CaptionButtonSize;
        int closeX = bounds.Right - Metrics.CaptionPaddingX - size;
        int closeY = bounds.Y + (bounds.Height - size) / 2;
        closeRect = new Rectangle(closeX, closeY, size, size);

        textRect = Rectangle.FromLTRB(textRect.Left, textRect.Top, Math.Max(textRect.Left, closeRect.Left - 6), textRect.Bottom);
      }

      DrawCaptionText(g, textRect, title, textColor);

      if (showCloseButton) DrawCloseButton(g, closeRect, closeHot, closePressed, Palette[ColorPalette.Role.CaptionButtonGlyph]);
    }

    /// <summary>캡션 텍스트를 그린다.</summary>
    public virtual void DrawCaptionText(Graphics g, Rectangle textBounds, string text, Color color)
    {
      var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
      TextRenderer.DrawText(g, text, SystemFonts.MessageBoxFont, textBounds, color, flags);
    }

    // Split / Preview ============================================================================

    /// <summary>스플리터를 그린다.</summary>
    public virtual void DrawSplitter(Graphics g, Rectangle bounds, bool hot = false, bool dragging = false)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));

      var color = Palette[ColorPalette.Role.Splitter];
      using var brush = new SolidBrush(color);
      g.FillRectangle(brush, bounds);
    }

    /// <summary>도킹 프리뷰(반투명 영역 + 테두리)를 그린다.</summary>
    public virtual void DrawDockPreview(Graphics g, Rectangle bounds)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));

      using (var brush = new SolidBrush(Palette[ColorPalette.Role.DockPreviewFill])) g.FillRectangle(brush, bounds);

      int thick = Math.Max(1, Metrics.DockPreviewBorderThickness);
      using var pen = new Pen(Palette[ColorPalette.Role.DockPreviewBorder], thick);
      g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    // Helpers =====================================================================================

    /// <summary>페널 테두리를 그린다.</summary>
    protected virtual void DrawPanelBorder(Graphics g, Rectangle bounds)
    {
      int thick = Metrics.PanelBorderThickness;
      if (thick <= 0) return;

      using var pen = new Pen(Palette[ColorPalette.Role.PanelBorder], thick);
      g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    /// <summary>탭 닫기(X) 버튼을 현재 프레임에서 표시할지 결정한다.</summary>
    /// <remarks>기본은 항상 표시한다. (파생 클래스에서 Active/Hover일 때만 표시하도록 변경 가능)</remarks>
    protected virtual bool IsTabCloseButtonVisible(TabVisualState tabState, bool closeHot, bool closePressed)
      => true;

    /// <summary>탭 닫기(X) 버튼(배경/글리프)을 그린다.</summary>
    protected virtual void DrawTabCloseButton(Graphics g, Rectangle bounds, TabVisualState tabState, bool hot, bool pressed)
    {
      var glyphColor = GetTabCloseGlyphColor(tabState, hot, pressed);

      if (hot || pressed)
      {
        var back = Palette[ColorPalette.Role.TabCloseBackHot];
        if (back.A == 0) back = Palette[ColorPalette.Role.CaptionButtonBackHot];

        using var b = new SolidBrush(back);
        g.FillRectangle(b, bounds);
      }

      DrawCloseGlyph(g, bounds, glyphColor);
    }

    /// <summary>탭 닫기(X) 글리프 색상을 결정한다.</summary>
    protected virtual Color GetTabCloseGlyphColor(TabVisualState tabState, bool hot, bool pressed)
    {
      Color c;

      if (pressed) c = Palette[ColorPalette.Role.TabCloseGlyphActive];
      else if (hot) c = Palette[ColorPalette.Role.TabCloseGlyphHot];
      else if (tabState == TabVisualState.Active) c = Palette[ColorPalette.Role.TabCloseGlyphActive];
      else c = Palette[ColorPalette.Role.TabCloseGlyph];

      if (c.A == 0)
      {
        if (pressed || hot || tabState == TabVisualState.Active)
          return Palette[ColorPalette.Role.CaptionButtonGlyph];

        return Palette[ColorPalette.Role.TextDisabled];
      }

      return c;
    }

    protected virtual Color GetTabBackColor(TabVisualState state)
    {
      return state switch
      {
        TabVisualState.Active => Palette[ColorPalette.Role.TabBackActive],
        TabVisualState.Hot => Palette[ColorPalette.Role.TabBackHot],
        _ => Palette[ColorPalette.Role.TabBack]
      };
    }

    protected virtual Color GetTabTextColor(TabVisualState state)
    {
      return state switch
      {
        TabVisualState.Active => Palette[ColorPalette.Role.TabTextActive],
        TabVisualState.Disabled => Palette[ColorPalette.Role.TextDisabled],
        _ => Palette[ColorPalette.Role.TabText],
      };
    }
  }
}

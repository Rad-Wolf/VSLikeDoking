// VsLikeDocking - VsLikeDoking - Rendering/VsDockRenderer.cs - VsDockRenderer - (File)

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

using VsLikeDoking.Rendering.Primitives;
using VsLikeDoking.Rendering.Theme;
using VsLikeDoking.Utils;

namespace VsLikeDoking.Rendering
{
  /// <summary>실제로 쓰는 VS느낌의 구현체</summary>
  /// <remarks>DockRenderer의 기본 동작을 유지하되, GdiCache로 Pen/Brush/Font/StringFormat를 재사용해서 렌더링 비용을 줄인다.</remarks>
  public sealed class VsDockRenderer : DockRenderer, IDisposable
  {
    // Types ====================================================================

    public enum AutoHideTextDirection : byte
    {
      Horizontal = 0,
      Rotate90 = 1,
      Rotate270 = 2,
    }

    // Fields ====================================================================

    private readonly GdiCache _Cache;
    private readonly bool _OwnCache;
    private bool _Disposed;

    // Properties ================================================================

    /// <summary>GDI 캐시(Brush/Pen/Font/StringFormat)를 반환한다.</summary>
    public GdiCache Cache
      => _Cache;

    // Ctor ======================================================================

    /// <summary>
    /// 기본 설정으로 VS 스타일 렌더러를 생성한다.
    /// - Activator.CreateInstance(typeof(VsDockRenderer)) 같은 리플렉션 생성 경로를 위해 "진짜" 기본 생성자를 제공한다.
    /// </summary>
    public VsDockRenderer() : this(null, null, null)
    {
    }

    /// <summary>VS 스타일 렌더러를 생성한다. Cache가 null이면 내부에서 생성하며 Dispose시 함께 정리한다.</summary>
    public VsDockRenderer(ColorPalette? palette = null, DockMetrics? metrics = null, GdiCache? cache = null) : base(palette, metrics)
    {
      _Cache = cache ?? new GdiCache();
      _OwnCache = cache is null;
    }

    // Tab Strip =================================================================

    /// <inheritdoc/>
    public override void DrawTabStripBackground(Graphics g, Rectangle bounds)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));

      using var _ = GdiEx.PushQuality(g, false);

      g.FillRectangle(_Cache.GetBrush(Palette[ColorPalette.Role.TabStripBack]), bounds);
      DrawPanelBorderInternal(g, bounds);
    }

    /// <inheritdoc/>
    public override void DrawTab(Graphics g, Rectangle bounds, string text, TabVisualState state, bool showCloseButton, bool closeHot = false, bool closePressed = false)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));
      text ??= string.Empty;

      using var _ = GdiEx.PushQuality(g, false);

      var back = GetTabBackColorInternal(state);
      var border = Palette[ColorPalette.Role.TabBorder];
      var textColor = GetTabTextColorInternal(state);

      g.FillRectangle(_Cache.GetBrush(back), bounds);
      g.DrawRectangle(_Cache.GetPen(border, 1f, PenAlignment.Inset), bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);

      // Close 버튼을 "실제로 표시"하는 프레임에만 여백을 예약한다.
      var closeVisible = showCloseButton && IsTabCloseButtonVisible(state, closeHot, closePressed);

      ComputeTabLayout(bounds, closeVisible, out var textRect, out var closeRect);

      DrawTabText(g, textRect, text, textColor);

      if (closeVisible)
        DrawTabCloseButton(g, closeRect, state, closeHot, closePressed);
    }

    /// <inheritdoc/>
    public override void DrawTabText(Graphics g, Rectangle textBounds, string text, Color color)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));
      text ??= string.Empty;

      var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
      var font = _Cache.GetFont(Metrics.TabFont);
      TextRenderer.DrawText(g, text, font, textBounds, color, flags);
    }

    /// <inheritdoc/>
    public override void DrawCloseButton(Graphics g, Rectangle bounds, bool hot, bool pressed, Color glyphColor)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));
      using var _ = GdiEx.PushQuality(g, true);

      if (hot || pressed)
        g.FillRectangle(_Cache.GetBrush(Palette[ColorPalette.Role.CaptionButtonBackHot]), bounds);

      // glyphColor(호출측 정책)을 우선 사용. (A==0이면 팔레트 기본값으로 폴백)
      var color = glyphColor;
      if (color.A == 0) color = Palette[ColorPalette.Role.CaptionButtonGlyph];

      DrawCloseGlyph(g, bounds, color);
    }

    /// <inheritdoc/>
    public override void DrawCloseGlyph(Graphics g, Rectangle bounds, Color color)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));

      int w = Math.Max(1, Metrics.GlyphStrokeWidth);
      int pad = 4;

      var rect = Rectangle.Inflate(bounds, -pad, -pad);
      if (rect.Width < 2 || rect.Height < 2) return;

      var pen = _Cache.GetPen(color, w, PenAlignment.Center);
      pen.LineJoin = LineJoin.Miter;

      g.DrawLine(pen, rect.Left, rect.Top, rect.Right, rect.Bottom);
      g.DrawLine(pen, rect.Left, rect.Bottom, rect.Right, rect.Top);
    }

    // AutoHide ==================================================================

    /// <summary>AutoHide Strip 배경을 그린다.</summary>
    public void DrawAutoHideStripBackground(Graphics g, Rectangle bounds)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));

      using var _ = GdiEx.PushQuality(g, false);

      // 현재 팔레트에 AutoHide 전용 role이 없으므로 TabStrip 톤을 재사용한다.
      g.FillRectangle(_Cache.GetBrush(Palette[ColorPalette.Role.TabStripBack]), bounds);
      DrawPanelBorderInternal(g, bounds);
    }

    /// <summary>AutoHide Tab을 그린다. (close 버튼 없음)</summary>
    public void DrawAutoHideTab(Graphics g, Rectangle bounds, string text, TabVisualState state, AutoHideTextDirection textDirection)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));
      text ??= string.Empty;

      if (bounds.Width <= 0 || bounds.Height <= 0) return;

      using var _ = GdiEx.PushQuality(g, false);

      var back = GetTabBackColorInternal(state);
      var border = Palette[ColorPalette.Role.TabBorder];
      var textColor = GetTabTextColorInternal(state);

      g.FillRectangle(_Cache.GetBrush(back), bounds);
      g.DrawRectangle(_Cache.GetPen(border, 1f, PenAlignment.Inset), bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);

      Rectangle textRect;

      if (textDirection == AutoHideTextDirection.Horizontal)
      {
        textRect = Rectangle.Inflate(bounds, -6, 0);
        DrawTabText(g, textRect, text, textColor);
        return;
      }

      // 세로 텍스트는 strip 폭이 얇아 x-padding을 크게 주면 한 글자만 남을 수 있다.
      // 회전 텍스트는 y축 위주로 패딩을 주고, x축은 최소만 줄인다.
      textRect = Rectangle.Inflate(bounds, -2, -4);

      DrawAutoHideTabTextRotated(g, textRect, text, textColor, textDirection);
    }

    private void DrawAutoHideTabTextRotated(Graphics g, Rectangle bounds, string text, Color color, AutoHideTextDirection dir)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));
      text ??= string.Empty;

      if (string.IsNullOrWhiteSpace(text)) return;
      if (bounds.Width <= 2 || bounds.Height <= 2) return;

      var font = _Cache.GetFont(Metrics.TabFont);
      var centerX = bounds.Left + (bounds.Width / 2f);
      var centerY = bounds.Top + (bounds.Height / 2f);
      var angle = dir == AutoHideTextDirection.Rotate90 ? 90f : -90f;

      var state = g.Save();
      try
      {
        g.TranslateTransform(centerX, centerY);
        g.RotateTransform(angle);

        var localWidth = Math.Max(1f, bounds.Height - 4f);
        var localHeight = Math.Max(1f, bounds.Width - 2f);
        var local = new RectangleF(-localWidth / 2f, -localHeight / 2f, localWidth, localHeight);

        using var sf = new StringFormat
        {
          Alignment = StringAlignment.Center,
          LineAlignment = StringAlignment.Center,
          Trimming = StringTrimming.EllipsisCharacter,
          FormatFlags = StringFormatFlags.NoWrap,
        };

        var br = _Cache.GetBrush(color);
        g.DrawString(text, font, br, local, sf);
      }
      finally
      {
        g.Restore(state);
      }
    }

    // Caption ==================================================================

    /// <inheritdoc/>
    public override void DrawCaption(Graphics g, Rectangle bounds, string title, CaptionVisualState state, bool showCloseButton, bool closeHot = false, bool closePressed = false)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));
      title ??= string.Empty;

      using var _ = GdiEx.PushQuality(g, false);

      var back = Palette[(state == CaptionVisualState.Active) ? ColorPalette.Role.CaptionBackActive : ColorPalette.Role.CaptionBack];
      var textColor = Palette[(state == CaptionVisualState.Active) ? ColorPalette.Role.CaptionTextActive : ColorPalette.Role.CaptionText];

      g.FillRectangle(_Cache.GetBrush(back), bounds);
      DrawPanelBorderInternal(g, bounds);

      var textRect = Rectangle.Inflate(bounds, -Metrics.CaptionPaddingX, 0);
      var closeRect = Rectangle.Empty;

      if (showCloseButton)
      {
        int size = Metrics.CaptionButtonSize;
        int cx = bounds.Right - Metrics.CaptionPaddingX - size;
        int cy = bounds.Y + (bounds.Height - size) / 2;
        closeRect = new Rectangle(cx, cy, size, size);

        textRect = Rectangle.FromLTRB(textRect.Left, textRect.Top, Math.Max(textRect.Left, closeRect.Left - 6), textRect.Bottom);
      }

      DrawCaptionText(g, textRect, title, textColor);

      if (showCloseButton)
        DrawCloseButton(g, closeRect, closeHot, closePressed, Palette[ColorPalette.Role.CaptionButtonGlyph]);
    }

    /// <inheritdoc/>
    public override void DrawCaptionText(Graphics g, Rectangle textBounds, string text, Color color)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));
      text ??= string.Empty;

      var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
      var font = _Cache.GetFont(Metrics.CaptionFont);
      TextRenderer.DrawText(g, text, font, textBounds, color, flags);
    }

    // Split / Preview ============================================================

    /// <inheritdoc/>
    public override void DrawSplitter(Graphics g, Rectangle bounds, bool hot = false, bool dragging = false)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));

      using var _ = GdiEx.PushQuality(g, false);

      var color = Palette[ColorPalette.Role.Splitter];

      if (dragging) color = MathEx.Mix(color, Palette[ColorPalette.Role.Accent], 0.45);
      else if (hot) color = MathEx.Mix(color, Palette[ColorPalette.Role.Accent], 0.25);

      g.FillRectangle(_Cache.GetBrush(color), bounds);
    }

    /// <inheritdoc/>
    public override void DrawDockPreview(Graphics g, Rectangle bounds)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));

      using var _ = GdiEx.PushQuality(g, true);

      g.FillRectangle(_Cache.GetBrush(Palette[ColorPalette.Role.DockPreviewFill]), bounds);

      int thick = Math.Max(1, Metrics.DockPreviewBorderThickness);
      var pen = _Cache.GetPen(Palette[ColorPalette.Role.DockPreviewBorder], thick, PenAlignment.Inset);
      g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    // Dispose ==================================================================

    /// <summary>내부에서 생성한 GDI 캐시를 정리한다.</summary>
    public void Dispose()
    {
      if (_Disposed) return;
      _Disposed = true;

      if (_OwnCache)
      {
        try { _Cache.Dispose(); }
        catch { }
      }
    }

    // Helpers ==================================================================

    /// <inheritdoc/>
    protected override bool IsTabCloseButtonVisible(TabVisualState tabState, bool closeHot, bool closePressed)
      => tabState is TabVisualState.Active or TabVisualState.Hot || closeHot || closePressed;

    /// <inheritdoc/>
    protected override void DrawTabCloseButton(Graphics g, Rectangle bounds, TabVisualState tabState, bool hot, bool pressed)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));

      using var _ = GdiEx.PushQuality(g, true);

      // Hot/Pressed일 때만 배경 표시(툴윈도우 캡션 버튼 느낌)
      if (hot || pressed)
      {
        var back = Palette[ColorPalette.Role.CaptionButtonBackHot];
        if (back.A == 0) back = Palette[ColorPalette.Role.TabCloseBackHot];
        if (back.A != 0) g.FillRectangle(_Cache.GetBrush(back), bounds);
      }

      var glyph = Palette[ColorPalette.Role.CaptionButtonGlyph];
      if (glyph.A == 0) glyph = Palette[ColorPalette.Role.TabText];

      if (!(hot || pressed))
      {
        // 기본(비-hover)에서는 더 옅게
        var a = (tabState == TabVisualState.Active) ? 160 : 200;
        glyph = Color.FromArgb(a, glyph);
      }

      DrawCloseGlyph(g, bounds, glyph);
    }

    private void DrawPanelBorderInternal(Graphics g, Rectangle bounds)
    {
      int thick = Metrics.PanelBorderThickness;
      if (thick <= 0) return;

      var pen = _Cache.GetPen(Palette[ColorPalette.Role.PanelBorder], thick, PenAlignment.Inset);
      g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    private Color GetTabBackColorInternal(TabVisualState state)
    {
      return state switch
      {
        TabVisualState.Active => Palette[ColorPalette.Role.TabBackActive],
        TabVisualState.Hot => Palette[ColorPalette.Role.TabBackHot],
        _ => Palette[ColorPalette.Role.TabBack]
      };
    }

    private Color GetTabTextColorInternal(TabVisualState state)
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

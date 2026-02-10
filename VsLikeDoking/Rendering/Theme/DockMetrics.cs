using System;
using System.Drawing;

namespace VsLikeDoking.Rendering.Theme
{
  /// <summary>렌더링에서 쓰는 픽셀 규격(높이/패딩/간격/버튼크기/선 두께 등)을 한곳에 모아두는 수치 테이블이다.</summary>
  public class DockMetrics
  {
    // Defaults===================================================================

    public static DockMetrics CreateVsLike()
      => new DockMetrics().Normalize();

    // Surface ==================================================================

    public int PanelBorderThickness { get; set; } = 1;

    // Tabs =====================================================================

    public int TabStripHeight { get; set; } = 26;
    public int TabMinWidth { get; set; } = 60;
    public int TabMaxWidth { get; set; } = 240;
    public int TabPaddingX { get; set; } = 10;
    public int TabPaddingY { get; set; } = 5;
    public int TabGap { get; set; } = 2;
    public int TabCloseButtonSize { get; set; } = 14;
    public int TabCloseButtonPaddingRight { get; set; } = 6;

    // Caption ==================================================================

    public int CaptionHeight { get; set; } = 24;
    public int CaptionPaddingX { get; set; } = 8;
    public int CaptionButtonSize { get; set; } = 16;
    public int CaptionButtonGap { get; set; } = 6;

    // Split / Preview ============================================================

    public int SplitterVisualThickness { get; set; } = 6;
    public int DockPreviewBorderThickness { get; set; } = 2;

    // Text ======================================================================

    public FontSpec TabFont { get; set; } = FontSpec.DefaultUi(9.0f);
    public FontSpec CaptionFont { get; set; } = FontSpec.DefaultUi(9.0f);

    // Glyphs ===================================================================

    public int GlyphStrokeWidth { get; set; } = 2;

    // Normalize ================================================================

    /// <summary>메트릭 값을 안전 범위로 보정한다.</summary>
    /// <returns></returns>
    public DockMetrics Normalize()
    {
      PanelBorderThickness = Math.Max(0, PanelBorderThickness);

      TabStripHeight = Math.Max(18, TabStripHeight);
      TabMinWidth = Math.Max(20, TabMinWidth);
      TabMaxWidth = Math.Max(TabMinWidth, TabMaxWidth);

      TabPaddingX = Math.Max(0, TabPaddingX);
      TabPaddingY = Math.Max(0, TabPaddingY);
      TabGap = Math.Max(0, TabGap);

      TabCloseButtonSize = Math.Max(8, TabCloseButtonSize);
      TabCloseButtonPaddingRight = Math.Max(0, TabCloseButtonPaddingRight);

      CaptionHeight = Math.Max(18, CaptionHeight);
      CaptionPaddingX = Math.Max(0, CaptionPaddingX);

      CaptionButtonSize = Math.Max(10, CaptionButtonSize);
      CaptionButtonGap = Math.Max(0, CaptionButtonGap);

      SplitterVisualThickness = Math.Max(1, SplitterVisualThickness);
      DockPreviewBorderThickness = Math.Max(1, DockPreviewBorderThickness);

      GlyphStrokeWidth = Math.Max(1, GlyphStrokeWidth);

      TabFont = TabFont.Normalize();
      CaptionFont = CaptionFont.Normalize();

      return this;
    }
  }

  public readonly struct FontSpec
  {
    public string Family { get; }
    public float Size { get; }
    public FontStyle Style { get; }
    public FontSpec(string family, float size, FontStyle style = FontStyle.Regular)
    {
      Family = family ?? string.Empty;
      Size = size;
      Style = style;
    }

    public FontSpec Normalize()
    {
      var fam = string.IsNullOrWhiteSpace(Family) ? "돋움체" : Family.Trim();
      var size = (float)Math.Max(6.0, Size);
      return new FontSpec(fam, size, Style);
    }

    public static FontSpec DefaultUi(float sizePt = 9.0f)
      => new FontSpec("돋움체", sizePt, FontStyle.Regular);
  }
}

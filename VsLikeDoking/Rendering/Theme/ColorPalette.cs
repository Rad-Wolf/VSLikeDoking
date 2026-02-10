// VsLikeDocking - VsLikeDoking - Rendering/Theme/ColorPalette.cs - ColorPalette - (File)

using System;
using System.Drawing;

using VsLikeDoking.Utils;

namespace VsLikeDoking.Rendering.Theme
{
  /// <summary>이 팔레트를 기준으로 색을 가져가게 하여 테마 교체가 쉬워지게 만든다</summary>
  public sealed class ColorPalette
  {
    // Roles =====================================================================================

    public enum Role
    {
      // Surface
      AppBack = 0,
      PanelBack = 1,
      PanelBorder = 2,

      // Text
      Text = 10,
      TextDisabled = 11,

      // Tabs
      TabStripBack = 20,
      TabBack = 21,
      TabBackHot = 22,
      TabBackActive = 23,
      TabBorder = 24,
      TabText = 25,
      TabTextActive = 26,

      // Tab Close (X)
      TabCloseBackHot = 27,
      TabCloseGlyph = 28,
      TabCloseGlyphHot = 29,
      TabCloseGlyphActive = 30,

      // Caption
      CaptionBack = 40,
      CaptionBackActive = 41,
      CaptionText = 42,
      CaptionTextActive = 43,
      CaptionButtonBackHot = 44,
      CaptionButtonGlyph = 45,

      // Split / Overlay
      Splitter = 60,
      DockPreviewFill = 61,
      DockPreviewBorder = 62,

      // Accent
      Accent = 80
    }

    // Fields =====================================================================================

    private readonly Color[] _Colors;

    // Indexer ====================================================================================

    public Color this[Role role]
    {
      get { return _Colors[(int)role]; }
      set { _Colors[(int)role] = value; }
    }

    // Ctor =======================================================================================

    public ColorPalette()
    {
      _Colors = new Color[GetArraySize()];
      for (int i = 0; i < _Colors.Length; i++) _Colors[i] = Color.Transparent;
    }

    public ColorPalette(ColorPalette src)
    {
      if (src is null) throw new ArgumentNullException(nameof(src));

      _Colors = new Color[src._Colors.Length];
      Array.Copy(src._Colors, _Colors, _Colors.Length);
    }

    // Presets ====================================================================================

    /// <summary>VS의 밝은(Light) 기본 팔레트를 생성</summary>
    public static ColorPalette CreateLight()
    {
      var p = new ColorPalette();

      p[Role.AppBack] = Color.FromArgb(245, 245, 245);
      p[Role.PanelBack] = Color.FromArgb(252, 252, 252);
      p[Role.PanelBorder] = Color.FromArgb(210, 210, 210);

      p[Role.Text] = Color.FromArgb(30, 30, 30);
      p[Role.TextDisabled] = Color.FromArgb(140, 140, 140);

      p[Role.TabStripBack] = Color.FromArgb(238, 238, 238);
      p[Role.TabBack] = Color.FromArgb(238, 238, 238);
      p[Role.TabBackHot] = Color.FromArgb(246, 246, 246);
      p[Role.TabBackActive] = Color.FromArgb(252, 252, 252);
      p[Role.TabBorder] = Color.FromArgb(210, 210, 210);
      p[Role.TabText] = Color.FromArgb(60, 60, 60);
      p[Role.TabTextActive] = Color.FromArgb(20, 20, 20);

      // Tab Close (X): 기본은 약하게, Hot/Active는 또렷하게
      p[Role.TabCloseBackHot] = Color.FromArgb(220, 220, 220);
      p[Role.TabCloseGlyph] = Color.FromArgb(140, 60, 60, 60);
      p[Role.TabCloseGlyphHot] = Color.FromArgb(40, 40, 40);
      p[Role.TabCloseGlyphActive] = Color.FromArgb(20, 20, 20);

      p[Role.CaptionBack] = Color.FromArgb(242, 242, 242);
      p[Role.CaptionBackActive] = Color.FromArgb(252, 252, 252);
      p[Role.CaptionText] = Color.FromArgb(60, 60, 60);
      p[Role.CaptionTextActive] = Color.FromArgb(20, 20, 20);
      p[Role.CaptionButtonBackHot] = Color.FromArgb(220, 220, 220);
      p[Role.CaptionButtonGlyph] = Color.FromArgb(40, 40, 40);

      p[Role.Splitter] = Color.FromArgb(210, 210, 210);
      p[Role.Accent] = Color.FromArgb(0, 122, 204);

      p.RebuildPreviewFromAccent(80);

      return p;
    }

    /// <summary>VS 감성의 어두운(Dark) 기본 팔레트를 생성한다.</summary>
    public static ColorPalette CreateDark()
    {
      var p = new ColorPalette();

      p[Role.AppBack] = Color.FromArgb(30, 30, 30);
      p[Role.PanelBack] = Color.FromArgb(37, 37, 38);
      p[Role.PanelBorder] = Color.FromArgb(63, 63, 70);

      p[Role.Text] = Color.FromArgb(241, 241, 241);
      p[Role.TextDisabled] = Color.FromArgb(150, 150, 150);

      p[Role.TabStripBack] = Color.FromArgb(45, 45, 48);
      p[Role.TabBack] = Color.FromArgb(45, 45, 48);
      p[Role.TabBackHot] = Color.FromArgb(62, 62, 64);
      p[Role.TabBackActive] = Color.FromArgb(37, 37, 38);
      p[Role.TabBorder] = Color.FromArgb(63, 63, 70);
      p[Role.TabText] = Color.FromArgb(200, 200, 200);
      p[Role.TabTextActive] = Color.FromArgb(241, 241, 241);

      // Tab Close (X): 기본은 약하게, Hot/Active는 또렷하게
      p[Role.TabCloseBackHot] = Color.FromArgb(62, 62, 64);
      p[Role.TabCloseGlyph] = Color.FromArgb(120, 200, 200, 200);
      p[Role.TabCloseGlyphHot] = Color.FromArgb(241, 241, 241);
      p[Role.TabCloseGlyphActive] = Color.FromArgb(241, 241, 241);

      p[Role.CaptionBack] = Color.FromArgb(45, 45, 48);
      p[Role.CaptionBackActive] = Color.FromArgb(37, 37, 38);
      p[Role.CaptionText] = Color.FromArgb(210, 210, 210);
      p[Role.CaptionTextActive] = Color.FromArgb(241, 241, 241);
      p[Role.CaptionButtonBackHot] = Color.FromArgb(62, 62, 64);
      p[Role.CaptionButtonGlyph] = Color.FromArgb(241, 241, 241);

      p[Role.Splitter] = Color.FromArgb(63, 63, 70);
      p[Role.Accent] = Color.FromArgb(0, 122, 204);

      p.RebuildPreviewFromAccent(90);

      return p;
    }

    // Methods ====================================================================================

    /// <summary>팔레트를 복제하고 특정 Role의 색만 변경한 새 팔레트를 반환한다.</summary>
    public ColorPalette With(Role role, Color color)
    {
      var p = new ColorPalette(this);
      p[role] = color;
      return p;
    }

    /// <summary>Accent 기반으로 DockPreview 색을 갱신한다.</summary>
    public void RebuildPreviewFromAccent(int fillAlpha = 85)
    {
      fillAlpha = MathEx.Clamp(fillAlpha, 0, 255);

      var accent = this[Role.Accent];
      this[Role.DockPreviewBorder] = accent;
      this[Role.DockPreviewFill] = WithAlpha(accent, fillAlpha);
    }

    // Helpers ====================================================================================

    private static int GetArraySize()
    {
      int max = 0;
      foreach (var v in Enum.GetValues(typeof(Role)))
      {
        int i = (int)v;
        if (i > max) max = i;
      }
      return max + 1;
    }

    private static Color WithAlpha(Color c, int alpha)
    {
      alpha = MathEx.Clamp(alpha, 0, 255);
      return Color.FromArgb(alpha, c.R, c.G, c.B);
    }
  }
}

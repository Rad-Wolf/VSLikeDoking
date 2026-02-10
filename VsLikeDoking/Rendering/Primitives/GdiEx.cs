using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

using VsLikeDoking.Rendering.Theme;

namespace VsLikeDoking.Rendering.Primitives
{
  /// <summary>렌더러들이 공통으로 쓰는 GDI+ 저수준 유틸(품질 설정, 라운드 사각형 Path, DPI 스케일, 폰트 생성 등)을 모아두는 파일이다.</summary>
  public static class GdiEx
  {
    // Internal ===================================================================

    private sealed class QualityScope : IDisposable
    {
      private Graphics? _Graphics;
      private readonly SmoothingMode _SmoothingMode;
      private readonly PixelOffsetMode _PixelOffsetMode;
      private readonly InterpolationMode _InterpolationMode;
      private readonly CompositingQuality _CompositingQuality;
      private readonly TextRenderingHint _TextRenderingHint;

      public QualityScope(Graphics g, bool highQuality)
      {
        _Graphics = g;
        _SmoothingMode = g.SmoothingMode;
        _PixelOffsetMode = g.PixelOffsetMode;
        _InterpolationMode = g.InterpolationMode;
        _CompositingQuality = g.CompositingQuality;
        _TextRenderingHint = g.TextRenderingHint;

        if (highQuality) ApplyHighQuality(g);
        else ApplyFast(g);
      }

      public void Dispose()
      {
        var g = _Graphics;
        if (g is null) return;

        g.SmoothingMode = _SmoothingMode;
        g.PixelOffsetMode = _PixelOffsetMode;
        g.InterpolationMode = _InterpolationMode;
        g.CompositingQuality = _CompositingQuality;
        g.TextRenderingHint = _TextRenderingHint;
        _Graphics = null;
      }
    }

    // Quality ===================================================================

    /// <summary>Graphics 품질 관련 상태를 저장/복원 하는 스코프를 만든다.</summary>
    public static IDisposable PushQuality(Graphics g, bool highQuality = true)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));
      return new QualityScope(g, highQuality);
    }

    /// <summary>Graphics에 고품질 렌더링 설정을 적용한다.</summary>
    public static void ApplyHighQuality(Graphics g)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));

      g.SmoothingMode = SmoothingMode.AntiAlias;
      g.PixelOffsetMode = PixelOffsetMode.HighQuality;
      g.InterpolationMode = InterpolationMode.HighQualityBicubic;
      g.CompositingQuality = CompositingQuality.HighQuality;
      g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    /// <summary>Graphics에 빠른 렌더링 설정을 적용한다.</summary>
    public static void ApplyFast(Graphics g)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));

      g.SmoothingMode = SmoothingMode.None;
      g.PixelOffsetMode = PixelOffsetMode.Default;
      g.InterpolationMode = InterpolationMode.Low;
      g.CompositingQuality = CompositingQuality.Default;
      g.TextRenderingHint = TextRenderingHint.SystemDefault;
    }

    // Geometry =================================================================

    /// <summary>라운드 사각형 GraphicsPath를 생성한다.</summary>
    /// <remarks>radius가 0이면 사각형이다.</remarks>
    public static GraphicsPath CreateRoundRectPath(RectangleF rect, float radius)
    {
      var path = new GraphicsPath();

      if (radius <= 0.01f)
      {
        path.AddRectangle(rect);
        path.CloseFigure();
        return path;
      }

      float r = Math.Min(radius, Math.Min(rect.Width, rect.Height) * 0.5f);
      float d = r * 2f;

      var arc = new RectangleF(rect.X, rect.Y, d, d);

      path.AddArc(arc, 180f, 90f); // TopLeft;
      arc.X = rect.Right - d;
      path.AddArc(arc, 270f, 90f); // TopRight;
      arc.Y = rect.Bottom - d;
      path.AddArc(arc, 0f, 90f); // BottomLeft;
      arc.X = rect.X;
      path.AddArc(arc, 90f, 90f); // BottomRight;

      path.CloseFigure();
      return path;
    }

    /// <summary>라운드 사각형을 채운다.</summary>
    public static void FillRoundRect(Graphics g, Brush brush, RectangleF rect, float radius)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));
      if (brush is null) throw new ArgumentNullException(nameof(brush));

      using var path = CreateRoundRectPath(rect, radius);
      g.FillPath(brush, path);
    }

    /// <summary>라운드 사각형 테두리를 그린다.</summary>
    public static void DrawRoundRect(Graphics g, Pen pen, RectangleF rect, float radius)
    {
      if (g is null) throw new ArgumentNullException(nameof(g));
      if (pen is null) throw new ArgumentNullException(nameof(pen));

      using var path = CreateRoundRectPath(rect, radius);
      g.DrawPath(pen, path);
    }

    /// <summary>bounds 안에 정사각형을 정 가운데 만든다.</summary>
    public static Rectangle CenterSquare(Rectangle bounds, int size)
    {
      size = Math.Max(1, size);
      int x = bounds.X + (bounds.Width - size) / 2;
      int y = bounds.Y + (bounds.Height - size) / 2;
      return new Rectangle(x, y, size, size);
    }

    // DPI ======================================================================

    /// <summary>기준 DPI(기본96) 대비 현재 DPI로 픽셀 값을 스케일한다.</summary>
    public static int ScaleByDpi(int valuePx, float dpi, float baseDpi = 96f)
    {
      if (baseDpi <= 0.01f) baseDpi = 96f;
      return (int)Math.Round(valuePx * (dpi / baseDpi));
    }

    /// <summary>컨트롤 DeviceDpi 기준으로 픽셀 값을 스케일한다.</summary>
    public static int ScaleByDpi(Control? control, int valuePx, float baseDpi = 96f)
    {
      float dpi = 96f;
      if (control is not null)
      {
        try { dpi = control.DeviceDpi; }
        catch { dpi = 96f; }
      }
      return ScaleByDpi(valuePx, dpi, baseDpi);
    }

    // Fonts ====================================================================

    /// <summary>FontSpec로 Font를 생성한다.(호출자가 Dispose 해야 한다.)</summary>
    public static Font CreateFont(FontSpec spec)
    {
      spec = spec.Normalize();
      return new Font(spec.Family, spec.Size, spec.Style, GraphicsUnit.Point);
    }
  }
}
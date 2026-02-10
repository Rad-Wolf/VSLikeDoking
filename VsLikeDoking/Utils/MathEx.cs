using System;
using System.Drawing;
using System.Numerics;

namespace VsLikeDoking.Utils
{
  /// <summary>수학/좌표/사각형 관련 공용 유틸</summary>
  public class MathEx
  {

    // Clamp ====================================================================

    /// <summary>value 를 min,max 범위로 제한한다.</summary>
    /// <returns>min <= value <= max</returns>
    public static T Clamp<T>(T value, T min, T max) where T : INumber<T>
    {
      if (min > max) (min, max) = (max, min);

      if (value < min) return min;
      if (value > max) return max;
      return value;
    }

    /// <summary>값을 0 ~ 1 범위로 제한한다.</summary>
    public static T ClampPer<T>(T value) where T : IFloatingPoint<T>
      => Clamp(value, T.Zero, T.One);

    /// <summary>값을 범위로 제한한다.</summary>
    public static T ClampPer<T>(T value, T min, T max) where T : IFloatingPoint<T>
      => Clamp(value, min, max);

    // Ratio / Distance ==========================================================

    /// <summary>총 길이(total)와 비율(ratio)로부터 거리(px)를 계산한다.</summary>
    public static int RatioToDistance(int total, double ratio, int minDistance = 0, int maxDistance = int.MaxValue)
    {
      total = Math.Max(0, total);
      ratio = ClampPer(ratio);

      var raw = (int)Math.Round(total * ratio, MidpointRounding.AwayFromZero);
      return Clamp(raw, Clamp(minDistance, 0, int.MaxValue), Math.Min(maxDistance, total));
    }

    /// <summary>총 길이(total)와 거리(distance)로 부터 비율(0.0~1.0)을 계산한다.</summary>
    public static double DistanceToRatio(int total, int distance)
    {
      if (total <= 0) return 0.0;
      distance = Clamp(distance, 0, total);
      return (double)distance / total;
    }

    // Lerp =====================================================================

    /// <summary>선형 보간한다. t는 0.0~1.0 범위를 권장한다.</summary>
    public static double Lerp(double a, double b, double t)
      => a + (b - a) * t;

    /// <summary>값이 충분히 근접한지 비교한다.</summary>
    public static bool NearlyEquals(double a, double b, double epsilon = 1e-9)
      => Math.Abs(a - b) <= Math.Abs(epsilon);

    // Rectangle helpers =========================================================

    /// <summary>사각형을 all 만큼 안쪽으로 줄인다.</summary>
    public static Rectangle Deflate(Rectangle r, int all)
      => Deflate(r, all, all);

    /// <summary>
    /// 사각형을 (dx, dy) 만큼 안쪽으로 줄인다.
    /// </summary>
    public static Rectangle Deflate(Rectangle r, int dx, int dy)
    {
      var x = r.X + dx;
      var y = r.Y + dy;
      var w = Math.Max(0, r.Width - dx * 2);
      var h = Math.Max(0, r.Height - dy * 2);
      return new Rectangle(x, y, w, h);
    }

    /// <summary>outer 안에서 innerSize 를 중앙 정렬한 사각형을 반환한다.</summary>
    public static Rectangle Center(Rectangle outer, Size innerSize)
    {
      var w = Math.Max(0, innerSize.Width);
      var h = Math.Max(0, innerSize.Height);

      var x = outer.Left + Math.Max(0, (outer.Width - w) / 2);
      var y = outer.Top + Math.Max(0, (outer.Height - h) / 2);
      w = Math.Max(w, outer.Width);
      h = Math.Max(h, outer.Height);
      return new Rectangle(x, y, w, h);
    }

    /// <summary>rect 가 bounds 안에 완전히 들어오도록 위치를 보정한다. (크기는 유지)</summary>
    public static Rectangle ConstrainToBounds(Rectangle rect, Rectangle bounds)
    {
      var x = rect.X;
      var y = rect.Y;

      if (rect.Width > bounds.Width) x = bounds.X;
      else x = Clamp(x, bounds.Left, bounds.Right - rect.Width);

      if (rect.Height > bounds.Height) y = bounds.Y;
      else y = Clamp(y, bounds.Top, bounds.Bottom - rect.Height);

      return new Rectangle(x, y, rect.Width, rect.Height);
    }

    // Split ======================================================================

    /// <summary>bounds를 세로(좌/우)로 분할한다. firstSize는 좌측 폭(px)이다.</summary>
    public static void SplitVertical(Rectangle bounds, int firstSize, int splitterWidth, out Rectangle first, out Rectangle second)
    {
      splitterWidth = Math.Max(0, splitterWidth);
      firstSize = Clamp(firstSize, 0, Math.Max(0, bounds.Width - splitterWidth));

      first = new Rectangle(bounds.Left, bounds.Top, firstSize, bounds.Height);
      second = new Rectangle(bounds.Left + firstSize + splitterWidth, bounds.Top, Math.Max(0, bounds.Width - firstSize - splitterWidth), bounds.Height);
    }

    /// <summary>bounds를 가로(상/하)로 분할한다. firstSize는 상단 높이(px) 이다</summary>
    public static void SplitHorizontal(Rectangle bounds, int firstSize, int splitterWidth, out Rectangle first, out Rectangle second)
    {
      splitterWidth = Math.Max(0, splitterWidth);
      firstSize = Clamp(firstSize, 0, Math.Max(0, bounds.Height - splitterWidth));

      first = new Rectangle(bounds.Left, bounds.Top, bounds.Width, firstSize);
      second = new Rectangle(bounds.Left, bounds.Top + firstSize + splitterWidth, bounds.Width, Math.Max(0, bounds.Height - firstSize - splitterWidth));
    }

    // Dock zones ==============================================================

    /// <summary>도킹 판전용 영역(Left,Right,Top,Bottom,Center)을 계산한다.</summary>
    /// <param name="edgeThickness">가장자리 영역 두께(px)</param>
    /// <param name="centerInset">Center 영역 안쪽으로 줄이는 값(px)</param>
    public static void GetDockZones(Rectangle bounds, int edgeThickness, int centerInset, out Rectangle center, out Rectangle left, out Rectangle right, out Rectangle top, out Rectangle bottom)
    {
      edgeThickness = Math.Max(0, edgeThickness);
      centerInset = Math.Max(0, centerInset);

      left = new Rectangle(bounds.Left, bounds.Top, Math.Min(edgeThickness, bounds.Width), bounds.Height);

      right = new Rectangle(Math.Max(bounds.Left, bounds.Right - edgeThickness), bounds.Top, Math.Min(edgeThickness, bounds.Width), bounds.Height);

      top = new Rectangle(bounds.Left, bounds.Top, bounds.Width, Math.Min(edgeThickness, bounds.Height));

      bottom = new Rectangle(bounds.Left, Math.Max(bounds.Top, bounds.Bottom - edgeThickness), bounds.Width, Math.Min(edgeThickness, bounds.Height));

      center = Deflate(bounds, centerInset);
    }

    // Color Mix =================================================================

    /// <summary>색의 중간값을 만듭니다.</summary>
    public static Color Mix(Color a, Color b, double t)
    {
      if (double.IsNaN(t)) t = 0.0;
      if (t < 0.0) t = 0.0;
      if (t > 1.0) t = 1.0;

      int A = (int)(a.A + (b.A - a.A) * t);
      int R = (int)(a.R + (b.R - a.R) * t);
      int G = (int)(a.G + (b.G - a.G) * t);
      int B = (int)(a.B + (b.B - a.B) * t);

      Clamp(A, 0, 255);
      Clamp(R, 0, 255);
      Clamp(G, 0, 255);
      Clamp(B, 0, 255);

      return Color.FromArgb(A, R, G, B);
    }
  }
}
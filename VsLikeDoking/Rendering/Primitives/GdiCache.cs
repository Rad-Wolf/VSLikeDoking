using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Drawing2D;

using VsLikeDoking.Rendering.Theme;

namespace VsLikeDoking.Rendering.Primitives
{
  /// <summary>렌더링 중 반복 생성되는 Brush/Pen/Font/StringFormat을 색/두께/폰트 스펙 기준으로 캐시해서, GC/깜빡임/성능 문제를 줄이기 위한 클래스</summary>
  public sealed class GdiCache : IDisposable
  {
    // Keys =====================================================================

    private readonly struct PenKey : IEquatable<PenKey>
    {
      public readonly int ARGB;
      public readonly int Width1000;
      public readonly int Alignment;

      public PenKey(int argb, float width, PenAlignment alignment)
      {
        ARGB = argb;
        Width1000 = (int)Math.Round(Math.Max(0.1f, width) * 1000f);
        Alignment = (int)alignment;
      }

      public bool Equals(PenKey other)
        => ARGB == other.ARGB && Width1000 == other.Width1000 && Alignment == other.Alignment;

      public override bool Equals([NotNullWhen(true)] object? obj)
        => obj is PenKey other && Equals(other);

      public override int GetHashCode()
      {
        unchecked
        {
          int hash = ARGB;
          hash = (hash * 397) ^ Width1000;
          hash = (hash * 397) ^ Alignment;
          return hash;
        }
      }
    }

    private readonly struct FontKey : IEquatable<FontKey>
    {
      public readonly string Family;
      public readonly int Size100;
      public readonly int Style;

      public FontKey(string family, float sizePt, FontStyle style)
      {
        Family = family ?? string.Empty;
        Size100 = (int)Math.Round(Math.Max(1f, sizePt) * 100f);
        Style = (int)style;
      }

      public bool Equals(FontKey other)
      {
        return Size100 == other.Size100
          && Style == other.Style
          && string.Equals(Family, other.Family, StringComparison.OrdinalIgnoreCase);
      }

      public override bool Equals(object? obj)
        => obj is FontKey other && Equals(other);

      public override int GetHashCode()
      {
        unchecked
        {
          int h = StringComparer.OrdinalIgnoreCase.GetHashCode(Family);
          h = (h * 397) ^ Size100;
          h = (h * 397) ^ Style;
          return h;
        }
      }
    }

    private readonly struct StringFormatKey : IEquatable<StringFormatKey>
    {
      public readonly int Align;
      public readonly int LineAlign;
      public readonly int Trimming;
      public readonly int Flags;

      public StringFormatKey(StringAlignment align, StringAlignment lineAlign, StringTrimming trimming, StringFormatFlags flags)
      {
        Align = (int)align;
        LineAlign = (int)lineAlign;
        Trimming = (int)trimming;
        Flags = (int)flags;
      }

      public bool Equals(StringFormatKey other)
      {
        return Align == other.Align
          && LineAlign == other.LineAlign
          && Trimming == other.Trimming
          && Flags == other.Flags;
      }

      public override bool Equals(object? obj) => obj is StringFormatKey other && Equals(other);

      public override int GetHashCode()
      {
        unchecked
        {
          int h = Align;
          h = (h * 397) ^ LineAlign;
          h = (h * 397) ^ Trimming;
          h = (h * 397) ^ Flags;
          return h;
        }
      }
    }

    // Fields ====================================================================

    private readonly Dictionary<int, SolidBrush> _Brushes = new();
    private readonly Dictionary<PenKey, Pen> _Pens = new();
    private readonly Dictionary<FontKey, Font> _Fonts = new();
    private readonly Dictionary<StringFormatKey, StringFormat> _StringFormats = new();
    private bool _Disposed;

    // Brushes ==================================================================

    /// <summary>지정 색상의 SolidBrush를 캐시에서 가져오거나 생성한다.</summary>
    public SolidBrush GetBrush(Color color)
    {
      ThrowIfDisposed();

      int key = color.ToArgb();
      if (_Brushes.TryGetValue(key, out var b)) return b;

      b = new SolidBrush(color);
      _Brushes[key] = b;
      return b;
    }

    // Pens =====================================================================

    /// <summary>지정 색/두께/정렬의 Pen을 캐시에서 가져오거나 생성한다.</summary>
    public Pen GetPen(Color color, float width = 1f, PenAlignment alignment = PenAlignment.Center)
    {
      ThrowIfDisposed();

      var key = new PenKey(color.ToArgb(), width, alignment);
      if (_Pens.TryGetValue(key, out var p)) return p;

      p = new Pen(color, Math.Max(0.1f, width)) { Alignment = alignment, LineJoin = LineJoin.Miter };
      _Pens[key] = p;
      return p;
    }

    // Fonts ====================================================================

    /// <summary>FontSpec 기반 font를 캐시에서 가져오거나 생성한다.</summary>
    public Font GetFont(FontSpec spec)
    {
      ThrowIfDisposed();

      spec = spec.Normalize();

      var key = new FontKey(spec.Family, spec.Size, spec.Style);
      if (_Fonts.TryGetValue(key, out var f)) return f;

      f = new Font(spec.Family, spec.Size, spec.Style, GraphicsUnit.Point);
      _Fonts[key] = f;
      return f;
    }

    // StringFormat ==============================================================

    public StringFormat GetStringFormat(StringAlignment align = StringAlignment.Near, StringAlignment lineAlign = StringAlignment.Center, StringTrimming trimming = StringTrimming.EllipsisCharacter, StringFormatFlags flags = StringFormatFlags.NoWrap)
    {
      ThrowIfDisposed();

      var key = new StringFormatKey(align, lineAlign, trimming, flags);
      if (_StringFormats.TryGetValue(key, out var sf)) return sf;

      sf = (StringFormat)StringFormat.GenericDefault.Clone();
      sf.Alignment = align;
      sf.LineAlignment = lineAlign;
      sf.Trimming = trimming;
      sf.FormatFlags = flags;
      _StringFormats[key] = sf;
      return sf;
    }

    // Maintenance =============================================================

    /// <summary>캐시된 GDI 객체를 모두 해제하고 비운다.</summary>
    public void Clear()
    {
      ThrowIfDisposed();

      foreach (var v in _Pens) v.Value.Dispose();
      foreach (var v in _Brushes) v.Value.Dispose();
      foreach (var v in _Fonts) v.Value.Dispose();
      foreach (var v in _StringFormats) v.Value.Dispose();

      _Pens.Clear();
      _Brushes.Clear();
      _Fonts.Clear();
      _StringFormats.Clear();
    }

    /// <summary>캐시 상태를 반환한다. (디버그용)</summary>
    public string GetStats()
    {
      ThrowIfDisposed();
      return $"Brushes = {_Brushes.Count}, Pens = {_Pens.Count}, Fonts = {_Fonts.Count}, StringFormats = {_StringFormats.Count}";
    }
    // Dispose ==================================================================


    public void Dispose()
    {
      if (_Disposed) return;
      _Disposed = true;

      try
      {
        foreach (var v in _Pens) v.Value.Dispose();
        foreach (var v in _Brushes) v.Value.Dispose();
        foreach (var v in _Fonts) v.Value.Dispose();
        foreach (var v in _StringFormats) v.Value.Dispose();
      }
      catch { }

      _Pens.Clear();
      _Brushes.Clear();
      _Fonts.Clear();
      _StringFormats.Clear();
    }

    // Helpers ==================================================================

    private void ThrowIfDisposed()
    {
      if (_Disposed) throw new ObjectDisposedException(nameof(GdiCache));
    }
  }
}
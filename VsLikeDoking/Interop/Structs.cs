using System;
using System.Runtime.InteropServices;

// User32.cs 같은 P/Invoke 선언에서 쓰는 Win32 구조체들(RECT/POINT 등)을 모은 파일

namespace VsLikeDoking.Interop
{
  [StructLayout(LayoutKind.Sequential)]
  internal struct POINT
  {
    public int X;
    public int Y;
    public POINT(int x, int y) { X = x; Y = y; }
    public override string ToString()
      => $"({X},{Y})";
  }

  [StructLayout(LayoutKind.Sequential)]
  internal struct RECT
  {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public RECT(int left, int top, int right, int bottom)
    { Left = left; Top = top; Right = right; Bottom = bottom; }

    public int Width => Right - Left;
    public int Height => Bottom - Top;

    public override string ToString()
      => $"({Left},{Top},{Right},{Bottom})";
  }

  [StructLayout(LayoutKind.Sequential)]
  internal struct SIZE
  {
    public int CX;
    public int CY;

    public SIZE(int cx, int cy)
    { CX = cx; CY = cy; }

    public override string ToString() => $"({CX},{CY})";
  }

  [StructLayout(LayoutKind.Sequential)]
  internal struct MINMAXINFO
  {
    public POINT ptReserved;
    public POINT ptMaxSize;
    public POINT ptMaxPosition;
    public POINT ptMinTrackSize;
    public POINT ptMaxTrackSize;
  }

  [StructLayout(LayoutKind.Sequential)]
  internal struct WINDOWPLACEMENT
  {
    public int length;
    public int flags;
    public int showCmd;
    public POINT ptMinPosition;
    public POINT ptMaxPosition;
    public RECT rcNormalPosition;

    public static WINDOWPLACEMENT Create()
    {
      var wp = new WINDOWPLACEMENT();
      wp.length = Marshal.SizeOf<WINDOWPLACEMENT>();
      return wp;
    }
  }

  [StructLayout(LayoutKind.Sequential)]
  internal struct TRACKMOUSEEVENT
  {
    public int cbSize;
    public uint dwFlags;
    public IntPtr hwndTrack;
    public uint dwHoverTime;

    public static TRACKMOUSEEVENT Create(IntPtr hwnd, uint flags, uint hoverTime = 0)
    {
      return new TRACKMOUSEEVENT { cbSize = Marshal.SizeOf<TRACKMOUSEEVENT>(), hwndTrack = hwnd, dwFlags = flags, dwHoverTime = hoverTime };
    }
  }
}
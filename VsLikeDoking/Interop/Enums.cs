using System;

// User32.cs 같은 P/Invoke에서 쓰는 Win32 상수/플레그/메시지 enum을 모은 파일

namespace VsLikeDoking.Interop
{
  // Messages ==============================================================================================
  internal enum WM : uint
  {
    NULL = 0x0000,
    CREATE = 0x0001,
    DESTROY = 0x0002,
    MOVE = 0x0003,
    SIZE = 0x0005,
    ACTIVATE = 0x0006,
    SETFOCUS = 0x0007,
    KILLFOCUS = 0x0008,
    ENABLE = 0x000A,
    SETREDRAW = 0x000B,
    SETTEXT = 0x000C,
    GETTEXT = 0x000D,
    GETTEXTLENGTH = 0x000E,
    PAINT = 0x000F,
    CLOSE = 0x0010,
    QUIT = 0x0012,
    ERASEBKGND = 0x0014,
    SYSCOLORCHANGE = 0x0015,
    SHOWWINDOW = 0x0018,
    SETCURSOR = 0x0020,
    GETMINMAXINFO = 0x0024,

    WINDOWPOSCHANGING = 0x0046,
    WINDOWPOSCHANGED = 0x0047,

    NCPAINT = 0x0085,
    NCCALCSIZE = 0x0083,
    NCHITTEST = 0x0084,
    NCMOUSEMOVE = 0x00A0,
    NCLBUTTONDOWN = 0x00A1,
    NCLBUTTONUP = 0x00A2,
    NCLBUTTONDBLCLK = 0x00A3,

    SYSCOMMAND = 0x0112,

    MOUSEMOVE = 0x0200,
    LBUTTONDOWN = 0x0201,
    LBUTTONUP = 0x0202,
    LBUTTONDBLCLK = 0x0203,
    RBUTTONDOWN = 0x0204,
    RBUTTONUP = 0x0205,
    MOUSEWHEEL = 0x020A,
    MOUSEHWHEEL = 0x020E,

    ENTERSIZEMOVE = 0x0231,
    EXITSIZEMOVE = 0x0232,

    MOUSEHOVER = 0x02A1,
    MOUSELEAVE = 0x02A3,

    DPICHANGED = 0x02E0
  }

  // HitTest ===============================================================================================
  internal enum HT : int
  {
    NOWHERE = 0,
    CLIENT = 1,
    CAPTION = 2,

    LEFT = 10,
    RIGHT = 11,
    TOP = 12,
    TOPLEFT = 13,
    TOPRIGHT = 14,
    BOTTOM = 15,
    BOTTOMLEFT = 16,
    BOTTOMRIGHT = 17
  }

  // ShowWindow ============================================================================================
  internal enum SW : int
  {
    HIDE = 0,
    SHOWNORMAL = 1,
    SHOWMINIMIZED = 2,
    SHOWMAXIMIZED = 3,
    SHOWNOACTIVATE = 4,
    SHOW = 5,
    MINIMIZE = 6,
    SHOWMINNOACTIVE = 7,
    SHOWNA = 8,
    RESTORE = 9,
    SHOWDEFAULT = 10
  }

  // SetWindowPos Flags ====================================================================================
  [Flags]
  internal enum SWP : uint
  {
    NOSIZE = 0x0001,
    NOMOVE = 0x0002,
    NOZORDER = 0x0004,
    NOREDRAW = 0x0008,
    NOACTIVATE = 0x0010,
    FRAMECHANGED = 0x0020,
    SHOWWINDOW = 0x0040,
    HIDEWINDOW = 0x0080,
    NOCOPYBITS = 0x0100,
    NOOWNERZORDER = 0x0200,
    NOSENDCHANGING = 0x0400,
    DEFERERASE = 0x2000,
    ASYNCWINDOWPOS = 0x4000
  }

  // Window Styles =========================================================================================
  [Flags]
  internal enum WS : uint
  {
    OVERLAPPED = 0x00000000,
    POPUP = 0x80000000,
    CHILD = 0x40000000,
    MINIMIZE = 0x20000000,
    VISIBLE = 0x10000000,
    DISABLED = 0x08000000,
    CLIPSIBLINGS = 0x04000000,
    CLIPCHILDREN = 0x02000000,
    MAXIMIZE = 0x01000000,

    CAPTION = 0x00C00000,
    BORDER = 0x00800000,
    DLGFRAME = 0x00400000,

    VSCROLL = 0x00200000,
    HSCROLL = 0x00100000,

    SYSMENU = 0x00080000,
    THICKFRAME = 0x00040000,

    MINIMIZEBOX = 0x00020000,
    MAXIMIZEBOX = 0x00010000,

    OVERLAPPEDWINDOW = OVERLAPPED | CAPTION | SYSMENU | THICKFRAME | MINIMIZEBOX | MAXIMIZEBOX
  }

  [Flags]
  internal enum WS_EX : uint
  {
    DLGMODALFRAME = 0x00000001,
    TOPMOST = 0x00000008,
    TRANSPARENT = 0x00000020,
    TOOLWINDOW = 0x00000080,
    CLIENTEDGE = 0x00000200,
    CONTROLPARENT = 0x00010000,
    APPWINDOW = 0x00040000,
    LAYERED = 0x00080000,
    NOACTIVATE = 0x08000000
  }

  // TrackMouseEvent Flags =================================================================================
  [Flags]
  internal enum TME : uint
  {
    HOVER = 0x00000001,
    LEAVE = 0x00000002,
    NONCLIENT = 0x00000010,
    QUERY = 0x40000000,
    CANCEL = 0x80000000
  }
}

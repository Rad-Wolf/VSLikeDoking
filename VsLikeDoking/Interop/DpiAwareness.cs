using System;
using System.Runtime.InteropServices;

namespace VsLikeDoking.Interop
{
  /// <summary>프로세스/스레드 DPI Awareness 설정과 윈도우 DPI조회에 필요한 Win32 API를 묶어둔 클래스</summary>
  /// <remarks>PerMonitorV2를 우선 시도하고, 안 되면 자동으로 하위 API로 폴백</remarks>
  internal static class DpiAwareness
  {
    // DPI_AWARENESS_CONTEXT ===============================================

    public static readonly IntPtr DPI_AWARENESS_CONTEXT_UNAWARE = new IntPtr(-1);
    public static readonly IntPtr DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = new IntPtr(-2);
    public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = new IntPtr(-3);
    public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);
    public static readonly IntPtr DPI_AWARENESS_CONTEXT_UNAWARE_GDISCALED = new IntPtr(-5);

    // Delegates ================================================================

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool SetProcessDpiAwarenessContextDelegate(IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr SetThreadDpiAwarenessContextDelegate(IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetDpiForWindowDelegate(IntPtr hWnd);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetDpiForSystemDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool SetProcessDPIAwareDelegate();

    // Cached delegates ========================================================

    private static SetProcessDpiAwarenessContextDelegate? _SetProcessDpiAwarenessContext;
    private static SetThreadDpiAwarenessContextDelegate? _SetThreadDpiAwarenessContext;
    private static GetDpiForWindowDelegate? _GetDpiForWindow;
    private static GetDpiForSystemDelegate? _GetDpiForSystem;
    private static SetProcessDPIAwareDelegate? _SetProcessDPIAware;
    private static bool _Resolved;

    // Public helpers ============================================================

    /// <summary>프로세스를 PerMonitorV2로 설정을 시도한다. 실패시 하위API로 폴백한다.</summary>
    public static bool TryEnablePerMonitorV2()
    {
      EnsureResolved();

      if (_SetProcessDpiAwarenessContext is not null)
      {
        if (_SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)) return true;
        if (_SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE)) return true;
        if (_SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_SYSTEM_AWARE)) return true;
      }

      // Legacy fallback(Vista+)
      if (_SetProcessDPIAware is not null) return _SetProcessDPIAware();

      return false;
    }

    /// <summary>현재 스레드의 DPI Awareness Context를 PerMonitorV2로 설정을 시도하고, 이전 값을 반환한다.</summary>
    /// <remarks>API가 없으면 IntPtr.Zero</remarks>
    public static IntPtr TrySetThreadPerMonitorV2()
    {
      EnsureResolved();

      if (_SetThreadDpiAwarenessContext is null) return IntPtr.Zero;
      return _SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    }

    /// <summary>스레드 DPI Awareness Context를 원복한다. (IntPtr.Zero면 무시)</summary>
    public static void RestoreThreadDpiAwareness(IntPtr previousContext)
    {
      EnsureResolved();

      if (previousContext == IntPtr.Zero) return;
      if (_SetThreadDpiAwarenessContext is null) return;

      _SetThreadDpiAwarenessContext(previousContext);
    }

    /// <summary>hWnd의 DPI를 반환한다. (가능하면 GetDpiForWindow, 아니면 GetDpiForSystem, 최후 96)</summary>
    public static uint GetDpiForHwnd(IntPtr hWnd)
    {
      EnsureResolved();

      if (hWnd != IntPtr.Zero && _GetDpiForWindow is not null)
      {
        try
        {
          uint d = _GetDpiForWindow(hWnd);
          if (d >= 48 && d <= 768) return d;
        }
        catch { }
      }

      if (_GetDpiForSystem is not null)
      {
        try
        {
          uint d = _GetDpiForSystem();
          if (d >= 48 && d <= 768) return d;
        }
        catch { }
      }

      return 96;
    }

    /// <summary>DPI를 96 기준 배율로 변환한다. (예: 144 -> 1.5)</summary>
    public static float ToScale(uint dpi) => dpi <= 0 ? 1.0f : (dpi / 96f);

    // Resolve ==================================================================

    private static void EnsureResolved()
    {
      if (_Resolved) return;
      _Resolved = true;

      IntPtr user32 = LoadLibrary("user32.dll");
      if (user32 != IntPtr.Zero)
      {
        _SetProcessDpiAwarenessContext = GetProcDelegate<SetProcessDpiAwarenessContextDelegate>(user32, "SetProcessDpiAwarenessContext");
        _SetThreadDpiAwarenessContext = GetProcDelegate<SetThreadDpiAwarenessContextDelegate>(user32, "SetThreadDpiAwarenessContext");
        _GetDpiForWindow = GetProcDelegate<GetDpiForWindowDelegate>(user32, "GetDpiForWindow");
        _GetDpiForSystem = GetProcDelegate<GetDpiForSystemDelegate>(user32, "GetDpiForSystem");
      }

      // Legacy (Vista+) : user32.SetProcessDPIAware
      if (user32 != IntPtr.Zero)
        _SetProcessDPIAware = GetProcDelegate<SetProcessDPIAwareDelegate>(user32, "SetProcessDPIAware");
    }

    private static T? GetProcDelegate<T>(IntPtr module, string procName) where T : class
    {
      if (module == IntPtr.Zero) return null;

      IntPtr p = GetProcAddress(module, procName);
      if (p == IntPtr.Zero) return null;

      try
      {
        return (Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T);
      }
      catch
      {
        return null;
      }
    }

    // Kernel32 Imports ==========================================================

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
  }
}
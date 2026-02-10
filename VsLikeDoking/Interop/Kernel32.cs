using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VsLikeDoking.Interop
{
  /// <summary>Win32 호출에서 에러 코드/핸들/메모리 같은 "기초"기능을 제공하는 kernal32.dll P/Invoke를 모음</summary>
  internal static class Kernel32
  {
    // GetLastError ==============================================================

    [DllImport("kernel32.dll", ExactSpelling = true)]
    public static extern uint GetLastError();

    // FormatMessage ==========================================================

    [Flags]
    internal enum FormatMessageFlags : uint
    {
      ALLOCATE_BUFFER = 0x00000100,
      IGNORE_INSERTS = 0x00000200,
      FROM_SYSTEM = 0x00001000,
      FROM_HMODULE = 0x00000800
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint FormatMessage(uint dwFlags, IntPtr lpSource, uint dwMessageId, uint dwLanguageId, StringBuilder lpBuffer, uint nSize, IntPtr Arguments);

    /// <summary>Win32 에러 코드를 사람이 읽을 수 있는 문자열로 변환한다.</summary>
    public static string GetErrorMessage(uint errorCode)
    {
      var sb = new StringBuilder(512);

      uint flags = (uint)(FormatMessageFlags.FROM_SYSTEM | FormatMessageFlags.IGNORE_INSERTS);
      uint len = FormatMessage(flags, IntPtr.Zero, errorCode, 0, sb, (uint)sb.Capacity, IntPtr.Zero);

      if (len == 0) return $"Win32Error={errorCode}";
      return sb.ToString().Trim();
    }
  }
}
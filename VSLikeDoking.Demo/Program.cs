// VsLikeDocking - VsLikeDoking - Demo/Program.cs - Program - Main

using System;
using System.Windows.Forms;

using VsLikeDoking.Demo.Forms;

namespace VSLikeDoking.Demo
{
  internal static class Program
  {
    // 앱 진입점
    [STAThread]
    private static void Main()
    {
      ApplicationConfiguration.Initialize();
      Application.Run(new DockHostUsageGuideForm());
    }
  }
}

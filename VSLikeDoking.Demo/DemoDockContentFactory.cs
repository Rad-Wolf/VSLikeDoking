// VsLikeDocking - VsLikeDoking.Demo - Demo/Docking/DemoDockContentFactory.cs - DemoDockContentFactory - (File)

using System;
using System.Drawing;
using System.Windows.Forms;

using VsLikeDoking.Abstractions;

namespace VsLikeDoking.Demo.Docking
{
  internal sealed class DemoDockContentFactory : IDockContentFactory
  {
    // Fields =====================================================================================================

    private readonly Func<Control>? _LogViewFactory;

    // Ctor =======================================================================================================

    public DemoDockContentFactory(Func<Control>? logViewFactory = null)
    {
      _LogViewFactory = logViewFactory;
    }

    // Public =====================================================================================================

    /// <summary>PersistKey로 컨텐츠를 생성한다.</summary>
    public IDockContent? Create(string persistKey)
    {
      if (string.IsNullOrWhiteSpace(persistKey)) throw new ArgumentException("persistKey");

      var key = persistKey.Trim();

      // "Log"를 ToolWindow 탭으로 넣고 싶을 때: 폼의 Log(TextBox 등)를 그대로 탭에 꽂을 수 있게 지원
      if (IsLogKey(key) && _LogViewFactory is not null)
        return new DemoDockContent(key, "Log", DockContentKind.ToolWindow, canClose: false, _LogViewFactory());

      var kind = GuessKindFromKey(key);
      var title = key;
      var view = CreateDefaultView(key, kind);

      return new DemoDockContent(key, title, kind, canClose: true, view);
    }

    // Helpers =====================================================================================================

    private static bool IsLogKey(string key)
    {
      return string.Equals(key, "Log", StringComparison.OrdinalIgnoreCase)
        || string.Equals(key, "Tool:Log", StringComparison.OrdinalIgnoreCase);
    }

    private static DockContentKind GuessKindFromKey(string key)
    {
      if (key.StartsWith("Doc", StringComparison.OrdinalIgnoreCase)) return DockContentKind.Document;
      if (key.StartsWith("Doc:", StringComparison.OrdinalIgnoreCase)) return DockContentKind.Document;

      if (key.StartsWith("Tool", StringComparison.OrdinalIgnoreCase)) return DockContentKind.ToolWindow;
      if (key.StartsWith("Tool:", StringComparison.OrdinalIgnoreCase)) return DockContentKind.ToolWindow;

      if (key.StartsWith("Output", StringComparison.OrdinalIgnoreCase)) return DockContentKind.ToolWindow;
      if (key.StartsWith("Tool:Output", StringComparison.OrdinalIgnoreCase)) return DockContentKind.ToolWindow;

      if (key.StartsWith("Log", StringComparison.OrdinalIgnoreCase)) return DockContentKind.ToolWindow;
      if (key.StartsWith("Tool:Log", StringComparison.OrdinalIgnoreCase)) return DockContentKind.ToolWindow;

      // 기본값: Document
      return DockContentKind.Document;
    }

    private static Control CreateDefaultView(string key, DockContentKind kind)
    {
      return new TextBox
      {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font(FontFamily.GenericMonospace, 9.0f),
        Dock = DockStyle.Fill,
        Text = $"{kind}: {key}",
      };
    }

    // DemoDockContent =============================================================================================

    private sealed class DemoDockContent : IDockContent
    {
      // Fields ===================================================================================================

      private readonly string _PersistKey;
      private readonly string _Title;
      private readonly DockContentKind _Kind;
      private readonly bool _CanClose;
      private readonly Control _View;

      // Ctor =====================================================================================================

      public DemoDockContent(string persistKey, string title, DockContentKind kind, bool canClose, Control view)
      {
        _PersistKey = persistKey;
        _Title = title;
        _Kind = kind;
        _CanClose = canClose;
        _View = view;
      }

      // IDockPersistable ==========================================================================================

      public string PersistKey => _PersistKey;

      public string? SaveState()
        => null;

      public void LoadState(string? state)
      {
        // no-op
      }

      // IDockContent ==============================================================================================

      public string Title => _Title;

      public Control View => _View;

      public DockContentKind Kind => _Kind;

      public bool CanClose => _CanClose;
    }
  }
}

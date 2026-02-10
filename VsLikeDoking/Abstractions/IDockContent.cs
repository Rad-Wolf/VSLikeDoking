using System.Windows.Forms;

namespace VsLikeDoking.Abstractions
{
  /// <summary>도킹 가능한 컨텐츠(문서/도구창)의 최소 계약.</summary>
  public interface IDockContent : IDockPersistable
  {
    /// <summary>(탭/캡션)에 표시될 제목</summary>
    string Title { get; }

    /// <summary>화면에 표시될 루트 컨트롤</summary>
    Control View { get; }
    /// <summary>컨텐츠 종류(문서/도구창)</summary>
    DockContentKind Kind { get; }
    /// <summary>사용자가 닫을 수 있는지 여부.</summary>
    bool CanClose { get; }
  }

  /// <summary>컨텐츠 종류</summary>
  public enum DockContentKind
  {
    Document = 0,
    ToolWindow = 1
  }
}
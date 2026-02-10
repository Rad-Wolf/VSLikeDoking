namespace VsLikeDoking.Abstractions
{
  /// <summary>도킹 (레이아웃/컨텐츠) 상태를 변경하는 명령의 최소 계약</summary>
  public interface IDockCommand
  {
    /// <summary>(디버그/로그/실행취소) UI 등에 표시할 이름.</summary>
    string Name { get; }

    /// <summary>명령을 실행한다. 성공하면 true</summary>
    bool Execute();

    /// <summary>실행을 되돌린다(Undo). 지원하지 않으면 false</summary>
    /// <returns></returns>
    bool Undo();
  }
}
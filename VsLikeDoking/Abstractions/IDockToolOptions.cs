namespace VsLikeDoking.Abstractions
{
  /// <summary>ToolWindow의 오토하이드 동작 정책(선택 구현).</summary>
  public interface IDockToolOptions
  {
    /// <summary>true면 AutoHide(접기) 가능, false면 항상 확장 상태로 유지한다.</summary>
    bool CanHide { get; }
  }
}


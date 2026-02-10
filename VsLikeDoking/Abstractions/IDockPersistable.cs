namespace VsLikeDoking.Abstractions
{
  /// <summary>도킹 대상(컨텐츠/창)의 상태를 저장/복구하기 위한 계약</summary>
  public interface IDockPersistable
  {
    /// <summary>재시작 후에도 변하지 않는 고정 키.(레이아웃 복원 시 컨텐츠를 재구성하는 기준)</summary>
    string PersistKey { get; }

    /// <summary>(컨텐츠/창)의 상태를 문자열로 저장한다.</summary>
    /// <remarks>상태가 없으면 null을 반환한다.</remarks>
    string? SaveState();

    /// <summary>저장된 문자열 상태를 기반으로 (컨텐츠/창)의 상태를 복구한다.</summary>
    /// <remarks>state가 null이면 무시 가능하다.</remarks>
    void LoadState(string? state);
  }
}
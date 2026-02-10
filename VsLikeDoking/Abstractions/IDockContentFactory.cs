namespace VsLikeDoking.Abstractions
{
  /// <summary>PersistKey를 기반으로 컨텐츠의 인스턴스를 생성/복원하기 위한 팩토리 계약</summary>
  /// <remarks>
  /// 레이아웃 복원 시 (PersistKey -> IDockContetn)매핑이 필요하다.
  /// 생성된 컨텐츠의 LoadState는 DockManager가 호출하는 것을 전제로 한다.
  /// </remarks>
  public interface IDockContentFactory
  {
    /// <summary>PersistKey로 컨텐츠를 생성한다. 지원하지 않는 키면 null을 반환한다.</summary>
    IDockContent? Create(string persistKey);
  }
}
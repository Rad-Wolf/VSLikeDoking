using System;

namespace VsLikeDoking.Core.Commands
{
  /// <summary>Dock 커맨드 실행에 필요한 Core 서비스 참조를 묶는 컨텍스트</summary>
  /// <remarks>커맨드 계약을 안정화시키고 내부 구현 변경의 파급을 줄이기 위한 용도</remarks>
  public sealed class DockCommandContext
  {
    // Properties ================================================================

    /// <summary>도킹 운영(변경 적용/검증/루트 반영)의 중심 매니저</summary>
    public DockManager Manager { get; }

    /// <summary>콘텐츠/키 레지스트리</summary>
    public DockRegistry Registry { get; }

    /// <summary>도킹 설정(옵션/플래그)</summary>
    public DockSettings Settings { get; }

    // Ctor ======================================================================

    /// <summary>커맨드 실행 컨텍스트를 생성</summary>
    public DockCommandContext(DockManager manager, DockRegistry registry, DockSettings settings)
    {
      Manager = manager ?? throw new ArgumentNullException(nameof(manager));
      Registry = registry ?? throw new ArgumentNullException(nameof(registry));
      Settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }
    // Helpers ==================================================================

    /// <summary>Settings 만 교체한 새 컨텍스트 생성</summary>
    public DockCommandContext WithSettings(DockSettings settings)
      => new(Manager, Registry, settings ?? throw new ArgumentNullException(nameof(settings)));

    /// <summary>Registry 만 교체한 새 컨텍스트 생성</summary>
    public DockCommandContext WithRegistry(DockRegistry registry)
      => new DockCommandContext(Manager, registry ?? throw new ArgumentNullException(nameof(registry)), Settings);
  }
}

using System;

using VsLikeDoking.Utils;

namespace VsLikeDoking.Layout.Persistence
{
  /// <summary>저장된 레이아웃 DTO가 구버전일 때, 최신 버전 DTO로 업그레이드(마이그레이션) 하는 전용 클래스</summary>
  /// <remarks>버전이 늘어날 때 여기만 확장하면 된다.</remarks>
  public class DockLayoutVersioning
  {
    // Version ==================================================================

    /// <summary>현재 지원하는 최신 레이아웃 저장 포맷 버전.</summary>
    public const int LatestVersion = 1;

    // Upgrade ==================================================================

    public static DockLayoutDto UpgradeToLatest(DockLayoutDto dto)
    {
      Guard.NotNull(dto);

      if (dto.Version <= 0) dto.Version = 1;
      if (dto.Version > LatestVersion) throw new NotSupportedException($"레이아웃 버전{dto.Version}이 지원되는 최신 버전{LatestVersion}보다 최신 버전입니다.");

      while (dto.Version < LatestVersion)
      {
        dto = UpgradeOnce(dto);
      }

      // 최신 버전에서도 기본 보정
      NormalizeLatest(dto);

      return dto;
    }

    /// <summary>DTO 업그레이드를 시도한다. 실패하면 false.</summary>
    public static bool TryUpgradeToLatest(DockLayoutDto? dto, out DockLayoutDto? upgraded)
    {
      upgraded = null;
      if (dto is null) return false;

      try
      {
        upgraded = UpgradeToLatest(dto);
        return true;
      }
      catch
      {
        upgraded = null;
        return false;
      }
    }

    // Internal ===================================================================

    private static DockLayoutDto UpgradeOnce(DockLayoutDto dto)
    {
      // 구체적인 버전 마이그레이션은 여기서 단계별로 추가
      switch (dto.Version)
      {
        case 1:
          throw new NotSupportedException("버전1은 이미 최신 빌드입니다. 업그레이드 경로가 없습니다.");
        default:
          throw new NotSupportedException($"지원되지 않는 레이아웃 버전{dto.Version} 입니다.");
      }
    }

    private static void NormalizeLatest(DockLayoutDto dto)
    {
      if (dto.Version <= 0) dto.Version = LatestVersion;
      if (dto.Root is null)
      {
        // Root 가 Null이면 호출부에서 기본 레이아웃으로 폴백하도록 두는 편이 안전하다.
        // 여기서는 아무 것도 만들지 않는다.
      }
    }
  }
}
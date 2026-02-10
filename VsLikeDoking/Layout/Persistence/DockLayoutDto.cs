using System.Collections.Generic;

using VsLikeDoking.Abstractions;
using VsLikeDoking.Layout.Nodes;

namespace VsLikeDoking.Layout.Persistence
{
  /// <summary>레이아웃 저장/복원용 루트 DTO</summary>
  public class DockLayoutDto
  {
    /// <summary>저장 포맷 버전.</summary>
    public int Version { get; set; } = 1;
    /// <summary>레이아웃 루트 노드 DTO</summary>
    public DockNodeDto? Root { get; set; }
  }

  /// <summary>노드 저장/복원용 DTO. Kind에 따라 필요한 필드만 사용한다.</summary>
  public sealed class DockNodeDto
  {
    // Common ==================================================================

    public DockNodeKind Kind { get; set; }
    public string? NodeId { get; set; }

    // Group/AutoHide ===========================================================

    public DockContentKind? ContentKind { get; set; }
    public List<DockContentItemDto>? Items { get; set; }
    public string? ActiveKey { get; set; }

    // AutoHide Only ============================================================

    public DockAutoHideSide? Side { get; set; }

    // Split ======================================================================

    public DockSplitOrientation? Orientation { get; set; }
    public double? Ratio { get; set; }
    public DockNodeDto? First { get; set; }
    public DockNodeDto? Second { get; set; }

    // Floating ===================================================================

    public DockRectDto? Bounds { get; set; }
    public DockNodeDto? Root { get; set; }
  }

  /// <summary>그룹/오토하이드 항목 저장용 DTO</summary>
  public sealed class DockContentItemDto
  {
    public string? PersistKey { get; set; }
    public string? State { get; set; }

    /// <summary>오토하이드 팝업 선호 크기(선택). 그룹 탭에서는 보통 null</summary>
    public DockSizeDto? PopupSize { get; set; }
  }

  /// <summary>직렬화 안정성을 위한 Rectangle DTO.</summary>
  public sealed class DockRectDto
  {
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
  }

  /// <summary>직렬화 안정성을 위한 Size DTO.</summary>
  public sealed class DockSizeDto
  {
    public int Width { get; set; }
    public int Height { get; set; }
  }
}

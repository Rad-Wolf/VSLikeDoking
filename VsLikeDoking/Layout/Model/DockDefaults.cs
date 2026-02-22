// VsLikeDocking - VsLikeDoking - Layout/Model/DockDefaults.cs - DockDefaults - (File)

using VsLikeDoking.Abstractions;
using VsLikeDoking.Layout.Nodes;
using VsLikeDoking.Utils;

namespace VsLikeDoking.Layout.Model
{
  public static class DockDefaults
  {
    // Default Ratios =============================================================================================

    /// <summary>새 Document 분할/그룹 생성의 기본 비율(50%).</summary>
    public const double DefaultDocumentNewPaneRatio = 0.50;

    /// <summary>ToolWindow를 Document 영역에 Side 도킹할 때 Tool 쪽 기본 비율(20%).</summary>
    public const double DefaultToolOntoDocumentNewPaneRatio = 0.20;

    /// <summary>ToolWindow를 ToolWindow 영역에 Side 도킹할 때 기본 비율(50%).</summary>
    public const double DefaultToolOntoToolNewPaneRatio = 0.50;

    /// <summary>기본 레이아웃 문서/도구(우측) 비율의 기본값.</summary>
    public const double DefaultDocumentWidthRatio = 0.78;

    /// <summary>기본 레이아웃 상/하 비율의 기본값.</summary>
    public const double DefaultTopHeightRatio = 0.78;

    // Presets ====================================================================================================

    /// <summary>빈 문서 탭 그룹 1개만 가지는 가장 단순한 레이아웃을 생성한다.</summary>
    public static DockNode CreateEmptyDocumentLayout()
    {
      return new DockGroupNode(DockContentKind.Document);
    }

    /// <summary>기본 레이아웃(좌:문서 / 우:도구창 / 하단:출력창)을 생성한다.</summary>
    public static DockNode CreateDefaultLayout(double documentWidthRatio = DefaultDocumentWidthRatio, double topHeightRatio = DefaultTopHeightRatio)
    {
      documentWidthRatio = ClampLayoutRatio(documentWidthRatio);
      topHeightRatio = ClampLayoutRatio(topHeightRatio);

      var documents = new DockGroupNode(DockContentKind.Document);
      return documents;
    }

    /// <summary>기본 레이아웃을 생성한다. ToolWindow 영역(우/하단)을 필요에 따라 제외할 수 있다.</summary>
    /// <remarks>
    /// - 둘 다 false면 Document-only 레이아웃을 반환한다.
    /// - Tool 영역이 나중에 필요해지면(툴 탭 추가) 상위 로직에서 이 레이아웃 형태로 재구성할 수 있다.
    /// </remarks>
    public static DockNode CreateDefaultLayout(bool includeRightToolArea, bool includeBottomToolArea, double documentWidthRatio = DefaultDocumentWidthRatio, double topHeightRatio = DefaultTopHeightRatio)
    {
      documentWidthRatio = ClampLayoutRatio(documentWidthRatio);
      topHeightRatio = ClampLayoutRatio(topHeightRatio);

      return new DockGroupNode(DockContentKind.Document);
    }

    // Policy Helpers ==============================================================================================

    /// <summary>Side 도킹 시 새 Pane 기본 비율을 반환한다.</summary>
    /// <remarks>Center(탭 합치기)에는 적용하지 않는다.</remarks>
    public static double GetDefaultNewPaneRatioForSideDock(DockContentKind sourceKind, DockContentKind targetKind)
    {
      if (sourceKind == DockContentKind.ToolWindow && targetKind == DockContentKind.Document)
        return DefaultToolOntoDocumentNewPaneRatio;

      if (sourceKind == DockContentKind.ToolWindow && targetKind == DockContentKind.ToolWindow)
        return DefaultToolOntoToolNewPaneRatio;

      return DefaultDocumentNewPaneRatio;
    }

    /// <summary>Side 도킹에서 newPaneRatio 요청값을 정책/기본값으로 해석해 반환한다.</summary>
    /// <remarks>
    /// - requestedRatio &lt;= 0 이면 정책 기본값을 사용한다.
    /// - 그 외는 ClampLayoutRatio(0.05~0.95)로 제한한다.
    /// </remarks>
    public static double ResolveNewPaneRatioForSideDock(double requestedRatio, DockContentKind sourceKind, DockContentKind targetKind)
    {
      if (requestedRatio <= 0.0)
        return ClampLayoutRatio(GetDefaultNewPaneRatioForSideDock(sourceKind, targetKind));

      return ClampLayoutRatio(requestedRatio);
    }

    /// <summary>Pane/레이아웃 비율을 안전 범위로 제한한다.</summary>
    public static double ClampLayoutRatio(double ratio)
    {
      return MathEx.Clamp(ratio, 0.05, 0.95);
    }
  }
}

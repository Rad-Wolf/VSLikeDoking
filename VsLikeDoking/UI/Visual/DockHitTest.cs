// VsLikeDocking - VsLikeDoking - UI/Visual/DockHitTest.cs - DockHitTest - (File)

using System.Drawing;

using VsLikeDoking.Utils;

namespace VsLikeDoking.UI.Visual
{
  /// <summary>DockVisualTree의 HitRegions를 기반으로 포인트 히트테스트를 수행한다.</summary>
  public static class DockHitTest
  {
    // Result =====================================================================================

    /// <summary>히트테스트 결과.</summary>
    public readonly struct DockHitTestResult
    {
      /// <summary>히트된 영역 종류</summary>
      public DockVisualTree.RegionKind Kind { get; }

      /// <summary>히트된 영역 사각형</summary>
      public Rectangle Bounds { get; }

      /// <summary>
      /// [ Splitter : Split index ]
      /// [ Group/Tab/Caption/Content : Group index ]
      /// [ AutoHideStrip/AutoHideTab : Strip index ]
      /// [ 그 외 -1 ]
      /// </summary>
      public int PrimaryIndex { get; }

      /// <summary>
      /// [ Tab/TabClose : Tab index ]
      /// [ AutoHideTab : AutoHideTab index ]
      /// [ 그 외 -1 ]
      /// </summary>
      public int SecondaryIndex { get; }

      /// <summary>유효 히트 여부</summary>
      public bool IsHit => Kind != DockVisualTree.RegionKind.None;

      /// <summary>상호작용 가능한 히트 여부(Empty 제외)</summary>
      public bool IsInteractive => Kind is not DockVisualTree.RegionKind.None and not DockVisualTree.RegionKind.Empty;

      /// <summary>Split(일 때만) index</summary>
      public int SplitIndex => Kind == DockVisualTree.RegionKind.Splitter ? PrimaryIndex : -1;

      /// <summary>Group(기반 Kind일 때만) index</summary>
      public int GroupIndex
        => Kind is DockVisualTree.RegionKind.Tab
        or DockVisualTree.RegionKind.TabClose
        or DockVisualTree.RegionKind.TabStrip
        or DockVisualTree.RegionKind.Caption
        or DockVisualTree.RegionKind.CaptionClose
        or DockVisualTree.RegionKind.Content ? PrimaryIndex : -1;

      /// <summary>Tab(Tab/TabClose일 때만) index</summary>
      public int TabIndex => Kind is DockVisualTree.RegionKind.Tab or DockVisualTree.RegionKind.TabClose ? SecondaryIndex : -1;

      /// <summary>AutoHide Strip index(AutoHideStrip/AutoHideTab일 때만)</summary>
      public int AutoHideStripIndex
        => Kind is DockVisualTree.RegionKind.AutoHideStrip or DockVisualTree.RegionKind.AutoHideTab ? PrimaryIndex : -1;

      /// <summary>AutoHide Tab index(AutoHideTab일 때만)</summary>
      public int AutoHideTabIndex => Kind == DockVisualTree.RegionKind.AutoHideTab ? SecondaryIndex : -1;

      private DockHitTestResult(DockVisualTree.RegionKind kind, Rectangle bounds, int primaryIndex, int secondaryIndex)
      {
        Kind = kind;
        Bounds = bounds;
        PrimaryIndex = primaryIndex;
        SecondaryIndex = secondaryIndex;
      }

      /// <summary>히트되지 않은 결과</summary>
      public static DockHitTestResult None()
        => new(DockVisualTree.RegionKind.None, Rectangle.Empty, -1, -1);

      internal static DockHitTestResult FromNormalized(DockVisualTree.RegionKind kind, Rectangle bounds, int primaryIndex, int secondaryIndex)
        => new(kind, bounds, primaryIndex, secondaryIndex);
    }

    // Public API ==================================================================================

    /// <summary>지정된 포인트에 대한 히트테스트를 수행한다.</summary>
    public static DockHitTestResult HitTest(DockVisualTree tree, Point point)
    {
      Guard.NotNull(tree);

      var regions = tree.HitRegions;
      if (regions.Count == 0) return DockHitTestResult.None();

      // 우선순위 : 작은 버튼 / 핵심 상호작용(닫기/스플리터) > AutoHide(얇고 작음) > 탭 > 캡션 > 탭스트립 > 콘텐츠 > 빈 영역
      // 동일 우선순위에서 겹치면:
      //  - 더 "작은" 영역(더 구체적)을 우선
      //  - 면적도 같으면 "나중에 추가된" 영역(뒤 인덱스)을 우선(역순 탐색 유지)

      var bestPriority = int.MaxValue;
      long bestArea = long.MaxValue;
      var bestIndex = -1;

      for (var i = regions.Count - 1; i >= 0; i--)
      {
        var r = regions[i];
        if (r.Kind == DockVisualTree.RegionKind.None) continue;
        if (r.Bounds.IsEmpty) continue;
        if (!r.Bounds.Contains(point)) continue;

        var p = GetPriority(r.Kind);
        var area = (long)r.Bounds.Width * (long)r.Bounds.Height;

        if (p < bestPriority)
        {
          bestPriority = p;
          bestArea = area;
          bestIndex = i;

          if (bestPriority == 0) break;
          continue;
        }

        if (p == bestPriority)
        {
          if (area < bestArea)
          {
            bestArea = area;
            bestIndex = i;
          }
        }
      }

      if (bestIndex < 0) return DockHitTestResult.None();

      var best = regions[bestIndex];
      var p1 = best.PrimaryIndex;
      var p2 = best.SecondaryIndex;

      NormalizeIndices(tree, best.Kind, ref p1, ref p2);

      return DockHitTestResult.FromNormalized(best.Kind, best.Bounds, p1, p2);
    }

    /// <summary>지정된 포인트에 대한 히트테스트를 수행하고, 히트 여부를 반환한다.</summary>
    public static bool TryHitTest(DockVisualTree tree, Point point, out DockHitTestResult result)
    {
      result = HitTest(tree, point);
      return result.IsHit;
    }

    // Internals ===================================================================================

    private static int GetPriority(DockVisualTree.RegionKind kind)
    {
      // 숫자가 낮을수록 우선(더 먼저 선택됨)
      return kind switch
      {
        DockVisualTree.RegionKind.TabClose => 0,
        DockVisualTree.RegionKind.CaptionClose => 1,
        DockVisualTree.RegionKind.Splitter => 2,

        DockVisualTree.RegionKind.AutoHideTab => 3,
        DockVisualTree.RegionKind.AutoHideStrip => 4,

        DockVisualTree.RegionKind.Tab => 5,
        DockVisualTree.RegionKind.Caption => 6,
        DockVisualTree.RegionKind.TabStrip => 7,
        DockVisualTree.RegionKind.Content => 8,
        DockVisualTree.RegionKind.Empty => 9,
        _ => 99
      };
    }

    private static void NormalizeIndices(DockVisualTree tree, DockVisualTree.RegionKind kind, ref int primaryIndex, ref int secondaryIndex)
    {
      // HitRegion 생성 코드가 Primary/Secondary를 뒤집어 넣어도 최대한 복구한다.
      // - Group 기반 Kind: primary=GroupIndex 여야 한다.
      // - Tab 기반 Kind: primary=GroupIndex, secondary=TabIndex(global) 여야 한다.
      // - Splitter: primary=SplitIndex 여야 한다.
      // - AutoHideStrip: primary=StripIndex
      // - AutoHideTab: primary=StripIndex, secondary=AutoHideTabIndex

      var groupCount = tree.Groups.Count;
      var tabCount = tree.Tabs.Count;
      var splitCount = tree.Splits.Count;

      var stripCount = tree.AutoHideStrips.Count;
      var autoHideTabCount = tree.AutoHideTabs.Count;

      static bool InRange(int v, int count)
        => (uint)v < (uint)count;

      static bool AutoHideMatch(DockVisualTree t, int stripIdx, int tabIdx, int stripCnt, int tabCnt)
      {
        if (!InRange(stripIdx, stripCnt)) return false;
        if (!InRange(tabIdx, tabCnt)) return false;
        return t.AutoHideTabs[tabIdx].StripIndex == stripIdx;
      }

      if (kind == DockVisualTree.RegionKind.Splitter)
      {
        if (!InRange(primaryIndex, splitCount) && InRange(secondaryIndex, splitCount))
          (primaryIndex, secondaryIndex) = (secondaryIndex, primaryIndex);

        secondaryIndex = -1;
        return;
      }

      if (kind is DockVisualTree.RegionKind.AutoHideStrip)
      {
        var stripIndex = -1;

        if (InRange(primaryIndex, stripCount)) stripIndex = primaryIndex;
        else if (InRange(secondaryIndex, stripCount)) stripIndex = secondaryIndex;

        primaryIndex = stripIndex;
        secondaryIndex = -1;
        return;
      }

      if (kind is DockVisualTree.RegionKind.AutoHideTab)
      {
        var stripIndex = -1;
        var tabIndex = -1;

        // 1) 가장 안전한 케이스: (primary=strip, secondary=tab) / (primary=tab, secondary=strip) 둘 중 관계가 맞는 것을 우선
        if (AutoHideMatch(tree, primaryIndex, secondaryIndex, stripCount, autoHideTabCount))
        {
          stripIndex = primaryIndex;
          tabIndex = secondaryIndex;
        }
        else if (AutoHideMatch(tree, secondaryIndex, primaryIndex, stripCount, autoHideTabCount))
        {
          stripIndex = secondaryIndex;
          tabIndex = primaryIndex;
        }
        else
        {
          // 2) tabIndex 후보를 먼저 잡고(둘 중 tabCount에 맞는 것), stripIndex는 tabIndex에서 역추론을 시도
          if (InRange(secondaryIndex, autoHideTabCount)) tabIndex = secondaryIndex;
          else if (InRange(primaryIndex, autoHideTabCount)) tabIndex = primaryIndex;

          if (tabIndex >= 0)
          {
            var s = tree.AutoHideTabs[tabIndex].StripIndex;
            if (InRange(s, stripCount)) stripIndex = s;
          }

          // 3) stripIndex가 아직 없으면 입력 인덱스에서 직접 후보를 채택
          if (stripIndex < 0)
          {
            if (InRange(primaryIndex, stripCount)) stripIndex = primaryIndex;
            else if (InRange(secondaryIndex, stripCount)) stripIndex = secondaryIndex;
          }
        }

        primaryIndex = stripIndex;
        secondaryIndex = tabIndex;
        return;
      }

      if (kind is DockVisualTree.RegionKind.Tab or DockVisualTree.RegionKind.TabClose)
      {
        var tabIndex = -1;
        var groupIndex = -1;

        if (InRange(secondaryIndex, tabCount)) tabIndex = secondaryIndex;
        else if (InRange(primaryIndex, tabCount)) tabIndex = primaryIndex;

        if (InRange(primaryIndex, groupCount)) groupIndex = primaryIndex;
        else if (InRange(secondaryIndex, groupCount)) groupIndex = secondaryIndex;

        // groupIndex가 없다면 tabIndex로부터 복구를 시도한다.
        if (groupIndex < 0 && tabIndex >= 0 && InRange(tabIndex, tabCount))
        {
          var g = tree.Tabs[tabIndex].GroupIndex;
          if (InRange(g, groupCount)) groupIndex = g;
        }

        primaryIndex = groupIndex;
        secondaryIndex = tabIndex;
        return;
      }

      if (kind is DockVisualTree.RegionKind.TabStrip
        or DockVisualTree.RegionKind.Caption
        or DockVisualTree.RegionKind.CaptionClose
        or DockVisualTree.RegionKind.Content)
      {
        var groupIndex = -1;

        if (InRange(primaryIndex, groupCount)) groupIndex = primaryIndex;
        else if (InRange(secondaryIndex, groupCount)) groupIndex = secondaryIndex;

        primaryIndex = groupIndex;
        secondaryIndex = -1;
        return;
      }

      secondaryIndex = -1;
    }
  }
}

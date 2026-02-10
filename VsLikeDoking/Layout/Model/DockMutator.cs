// VsLikeDocking - VsLikeDoking - Layout/Model/DockMutator.cs - DockMutator - (File)

using System;
using System.Collections.Generic;
using System.Drawing;

using VsLikeDoking.Abstractions;
using VsLikeDoking.Layout.Nodes;
using VsLikeDoking.Utils;

namespace VsLikeDoking.Layout.Model
{
  /// <summary>DockNode 트리를 변경하는 순수 레이아웃 변이(뮤테이션) 유틸리티</summary>
  public static class DockMutator
  {
    // Const ========================================================================================================

    private const double DefaultAutoHideStripRatio = 0.06; // strip은 "존재 표시" 용도라 작게 유지

    // Find =========================================================================================================

    /// <summary>NodeId로 DockNode를 찾는다(없으면 null).</summary>
    public static DockNode? FindByNodeId(DockNode root, string nodeId)
    {
      Guard.NotNull(root);
      Guard.NotNullOrWhiteSpace(nodeId);

      var id = nodeId.Trim();

      foreach (var n in root.TraverseDepthFirst(true))
        if (string.Equals(n.NodeId, id, StringComparison.Ordinal))
          return n;

      return null;
    }

    /// <summary>지정 Kind의 첫 그룹을 찾는다(없으면 null).</summary>
    public static DockGroupNode? FindFirstGroupByKind(DockNode root, DockContentKind kind)
    {
      Guard.NotNull(root);

      foreach (var n in root.TraverseDepthFirst(true))
        if (n is DockGroupNode g && g.ContentKind == kind)
          return g;

      return null;
    }

    /// <summary>지정 Side의 첫 AutoHide 스트립을 찾는다(없으면 null).</summary>
    public static DockAutoHideNode? FindFirstAutoHideBySide(DockNode root, DockAutoHideSide side)
    {
      Guard.NotNull(root);

      foreach (var n in root.TraverseDepthFirst(true))
        if (n is DockAutoHideNode a && a.Side == side)
          return a;

      return null;
    }

    // AutoHide =====================================================================================================

    /// <summary>AutoHide 스트립이 없으면 생성하여 트리에 붙인다.</summary>
    /// <remarks>
    /// - AutoHide는 “특수 렌더/배치” 대상이므로, 트리에는 leaf로만 매단다.
    /// - side에 따라 Split 방향/순서를 고정한다.
    /// </remarks>
    public static DockNode EnsureAutoHideStrip(DockNode root, DockAutoHideSide side, out DockAutoHideNode strip, DockContentKind kind = DockContentKind.ToolWindow)
    {
      Guard.NotNull(root);

      var existing = FindFirstAutoHideBySide(root, side);
      if (existing is not null)
      {
        strip = existing;
        return root;
      }

      strip = new DockAutoHideNode(side, kind);

      var orientation = (side == DockAutoHideSide.Left || side == DockAutoHideSide.Right)
        ? DockSplitOrientation.Vertical
        : DockSplitOrientation.Horizontal;

      var ratioStrip = NormalizeAutoHideStripRatio(DefaultAutoHideStripRatio);

      DockNode first;
      DockNode second;
      double ratioFirst;

      // DockSplitNode.Ratio는 "First" 비율
      if (side == DockAutoHideSide.Left || side == DockAutoHideSide.Top)
      {
        first = strip;
        second = root;
        ratioFirst = ratioStrip;
      }
      else
      {
        first = root;
        second = strip;
        ratioFirst = 1.0 - ratioStrip;
      }

      var wrapper = new DockSplitNode(orientation, ratioFirst, first, second);

      try { DockValidator.RebuildParents(wrapper); } catch { }
      wrapper.SetParentInternal(null);

      return wrapper;
    }

    /// <summary>PersistKey 컨텐츠를 AutoHide(핀)로 보낸다(그룹 -> AutoHide).</summary>
    public static DockNode PinToAutoHide(DockNode root, string persistKey, DockAutoHideSide side, Size? popupSize = null)
      => PinToAutoHide(root, persistKey, side, out _, popupSize);

    /// <summary>PersistKey 컨텐츠를 AutoHide(핀)로 보낸다(그룹 -> AutoHide).</summary>
    /// <returns>변경된(또는 동일한) 루트</returns>
    public static DockNode PinToAutoHide(DockNode root, string persistKey, DockAutoHideSide side, out bool didChange, Size? popupSize = null)
    {
      Guard.NotNull(root);
      Guard.NotNullOrWhiteSpace(persistKey);

      didChange = false;

      var key = persistKey.Trim();
      if (key.Length == 0) return root;

      // 이미 AutoHide에 있으면 noop
      if (TryFindAutoHideContainingKey(root, key, out _))
        return root;

      // 그룹에 있어야 pin 가능
      if (!TryFindGroupContainingKey(root, key, out var srcGroup))
        return root;

      // 상태 확보(제거 전에)
      var state = TryGetLayoutItemState(srcGroup, key);

      // 그룹에서 제거
      if (!srcGroup.Remove(key))
        return root;

      // AutoHide 스트립 보장(문서/툴 모두 가능하도록 srcGroup.Kind 사용)
      root = EnsureAutoHideStrip(root, side, out var strip, srcGroup.ContentKind);

      // AutoHide에 추가
      strip.Add(key, state, popupSize);

      // 비워진 그룹 제거 정책(문서 그룹 최소 1개 유지)
      if (srcGroup.Items.Count == 0)
      {
        var docCount = CountGroups(root, DockContentKind.Document);

        if (srcGroup.ContentKind == DockContentKind.ToolWindow)
          root = RemoveGroupLeafById(root, srcGroup.NodeId, out _);
        else if (srcGroup.ContentKind == DockContentKind.Document)
        {
          if (docCount > 1)
            root = RemoveGroupLeafById(root, srcGroup.NodeId, out _);
        }
      }

      try { DockValidator.RebuildParents(root); } catch { }

      didChange = true;
      return root;
    }

    /// <summary>PersistKey 컨텐츠를 AutoHide에서 다시 그룹으로 보낸다(Unpin).</summary>
    public static DockNode UnpinFromAutoHide(DockNode root, string persistKey, string? targetGroupNodeId = null, bool makeActive = true)
      => UnpinFromAutoHide(root, persistKey, out _, targetGroupNodeId, makeActive);

    /// <summary>PersistKey 컨텐츠를 AutoHide에서 다시 그룹으로 보낸다(Unpin).</summary>
    /// <remarks>
    /// - targetGroupNodeId가 유효하면 그 그룹으로, 아니면 같은 Kind의 첫 그룹으로 들어간다.
    /// - ToolWindow인데 대상 그룹이 없으면 EnsureToolArea로 Tool 그룹을 만든다.
    /// </remarks>
    public static DockNode UnpinFromAutoHide(DockNode root, string persistKey, out bool didChange, string? targetGroupNodeId = null, bool makeActive = true)
    {
      Guard.NotNull(root);
      Guard.NotNullOrWhiteSpace(persistKey);

      didChange = false;

      var key = persistKey.Trim();
      if (key.Length == 0) return root;

      if (!TryFindAutoHideContainingKey(root, key, out var srcStrip))
        return root;

      // AutoHide 아이템의 state 확보
      var state = TryGetAutoHideItemState(srcStrip, key);

      // AutoHide에서 제거
      if (!srcStrip.Remove(key))
        return root;

      // 대상 그룹 결정
      DockGroupNode? target = null;

      if (!string.IsNullOrWhiteSpace(targetGroupNodeId))
      {
        var id = targetGroupNodeId!.Trim();
        target = FindByNodeId(root, id) as DockGroupNode;
        if (target is not null && target.ContentKind != srcStrip.ContentKind)
          target = null;
      }

      if (target is null)
      {
        target = FindFirstGroupByKind(root, srcStrip.ContentKind);

        // ToolWindow면 없을 때 ToolArea 생성
        if (target is null && srcStrip.ContentKind == DockContentKind.ToolWindow)
        {
          root = EnsureToolArea(root, out var toolGroup, DockToolAreaPlacement.Right, DockDefaults.DefaultToolOntoDocumentNewPaneRatio);
          target = toolGroup;
        }
      }

      if (target is null)
      {
        // 들어갈 그룹이 없으면 안전 복구
        srcStrip.Add(key, state, popupSize: null);
        return root;
      }

      target.Add(key, state);
      if (makeActive) target.SetActive(key);

      // 비어있는 AutoHide strip 제거
      if (srcStrip.Items.Count == 0)
        root = RemoveAutoHideLeafById(root, srcStrip.NodeId, out _);

      try { DockValidator.RebuildParents(root); } catch { }

      didChange = true;
      return root;
    }

    // Tool Area ====================================================================================================

    /// <summary>ToolWindow 영역(그룹)이 없으면 기본 배치로 생성한다.</summary>
    /// <remarks>
    /// - Tool 그룹이 이미 존재하면 root를 그대로 반환한다.
    /// - Tool 그룹이 없다면, root 전체를 기준으로 Right/Bottom 등에 Tool 그룹을 붙이는 상위 Split을 생성한다.
    /// </remarks>
    public static DockNode EnsureToolArea(DockNode root, out DockGroupNode toolGroup, DockToolAreaPlacement placement = DockToolAreaPlacement.Right, double toolPaneRatio = DockDefaults.DefaultToolOntoDocumentNewPaneRatio)
    {
      Guard.NotNull(root);

      var existing = FindFirstGroupByKind(root, DockContentKind.ToolWindow);
      if (existing is not null)
      {
        toolGroup = existing;
        return root;
      }

      toolPaneRatio = NormalizeToolPaneRatio(toolPaneRatio);

      toolGroup = new DockGroupNode(DockContentKind.ToolWindow);

      var vertical = placement is DockToolAreaPlacement.Left or DockToolAreaPlacement.Right;
      var orientation = vertical ? DockSplitOrientation.Vertical : DockSplitOrientation.Horizontal;

      DockNode first;
      DockNode second;
      double ratioFirst;

      if (placement is DockToolAreaPlacement.Left or DockToolAreaPlacement.Top)
      {
        first = toolGroup;
        second = root;
        ratioFirst = toolPaneRatio;
      }
      else
      {
        first = root;
        second = toolGroup;
        ratioFirst = 1.0 - toolPaneRatio;
      }

      var split = new DockSplitNode(orientation, ratioFirst, first, second);

      try { DockValidator.RebuildParents(split); } catch { }
      split.SetParentInternal(null);

      return split;
    }

    // Dock =========================================================================================================

    /// <summary>PersistKey 컨텐츠를 그룹(NodeId)에 도킹한다.</summary>
    /// <remarks>
    /// - Center면 대상 그룹 탭으로 합친다.
    /// - L/R/T/B면 분할하여 새 그룹을 만든다(기본: 대상 그룹 Kind로 생성).
    /// </remarks>
    public static DockNode DockToGroup(DockNode root, string persistKey, string? state, string targetGroupNodeId, DockDropSide side, double newPaneRatio, bool makeActive)
    {
      Guard.NotNull(root);
      Guard.NotNullOrWhiteSpace(persistKey);
      Guard.NotNullOrWhiteSpace(targetGroupNodeId);

      var key = persistKey.Trim();
      var targetId = targetGroupNodeId.Trim();

      var target = FindByNodeId(root, targetId) as DockGroupNode;
      if (target is null) return root;

      if (side == DockDropSide.Center)
      {
        target.Add(key, state);
        if (makeActive) target.SetActive(key);
        return root;
      }

      return DockToGroup(root, key, state, targetId, side, newPaneRatio, makeActive, target.ContentKind);
    }

    /// <summary>PersistKey 컨텐츠를 그룹(NodeId)에 도킹한다(분할 시 새 그룹 Kind를 지정).</summary>
    public static DockNode DockToGroup(DockNode root, string persistKey, string? state, string targetGroupNodeId, DockDropSide side, double newPaneRatio, bool makeActive, DockContentKind newGroupKind)
    {
      Guard.NotNull(root);
      Guard.NotNullOrWhiteSpace(persistKey);
      Guard.NotNullOrWhiteSpace(targetGroupNodeId);

      var key = persistKey.Trim();
      var targetId = targetGroupNodeId.Trim();

      var target = FindByNodeId(root, targetId) as DockGroupNode;
      if (target is null) return root;

      if (side == DockDropSide.Center)
      {
        target.Add(key, state);
        if (makeActive) target.SetActive(key);
        return root;
      }

      newPaneRatio = NormalizeNewPaneRatio(newPaneRatio, newGroupKind, target.ContentKind);

      var newGroup = new DockGroupNode(newGroupKind);
      newGroup.Add(key, state);
      if (makeActive) newGroup.SetActive(key);

      var parent = target.Parent;

      DockNode first;
      DockNode second;
      double ratio;

      if (side == DockDropSide.Left || side == DockDropSide.Top)
      {
        first = newGroup;
        second = target;
        ratio = newPaneRatio;
      }
      else
      {
        first = target;
        second = newGroup;
        ratio = 1.0 - newPaneRatio;
      }

      var orientation = (side == DockDropSide.Left || side == DockDropSide.Right)
        ? DockSplitOrientation.Vertical
        : DockSplitOrientation.Horizontal;

      var split = new DockSplitNode(orientation, ratio, first, second);

      if (parent is null)
      {
        split.SetParentInternal(null);
        return split;
      }

      if (parent is DockSplitNode sp)
      {
        sp.ReplaceChild(target, split);
        return root;
      }

      if (parent is DockFloatingNode fp)
      {
        fp.ReplaceRoot(split);
        return root;
      }

      return root;
    }

    // Close ========================================================================================================

    /// <summary>PersistKey 컨텐츠를 트리에서 제거한다(닫기).</summary>
    public static DockNode CloseContent(DockNode root, string persistKey)
    {
      Guard.NotNull(root);
      Guard.NotNullOrWhiteSpace(persistKey);

      var key = persistKey.Trim();

      var emptiedGroups = new List<EmptiedGroup>(2);
      var emptiedAutoHides = new List<string>(2);

      RemoveContentEverywhereInternal(root, key, emptiedGroups, emptiedAutoHides);

      if (emptiedGroups.Count == 0 && emptiedAutoHides.Count == 0)
      {
        try { DockValidator.RebuildParents(root); } catch { }
        return root;
      }

      var docCount = CountGroups(root, DockContentKind.Document);

      for (int i = 0; i < emptiedGroups.Count; i++)
      {
        var e = emptiedGroups[i];

        if (e.Kind == DockContentKind.ToolWindow)
        {
          root = RemoveGroupLeafById(root, e.GroupId, out _);
          continue;
        }

        if (e.Kind == DockContentKind.Document)
        {
          if (docCount <= 1) continue;

          root = RemoveGroupLeafById(root, e.GroupId, out var removed);
          if (removed) docCount--;
        }
      }

      // 이번 Close로 인해 "비워진 strip"만 타겟팅 제거
      for (int i = 0; i < emptiedAutoHides.Count; i++)
        root = RemoveAutoHideLeafById(root, emptiedAutoHides[i], out _);

      try { DockValidator.RebuildParents(root); } catch { }

      return root;
    }

    // Helpers ======================================================================================================

    private readonly struct EmptiedGroup
    {
      public string GroupId { get; }
      public DockContentKind Kind { get; }

      public EmptiedGroup(string groupId, DockContentKind kind)
      {
        GroupId = groupId;
        Kind = kind;
      }
    }

    private static double NormalizeToolPaneRatio(double ratio)
    {
      if (double.IsNaN(ratio) || ratio <= 0.0)
        ratio = DockDefaults.DefaultToolOntoDocumentNewPaneRatio;

      return DockDefaults.ClampLayoutRatio(ratio);
    }

    private static double NormalizeNewPaneRatio(double ratio, DockContentKind sourceKind, DockContentKind targetKind)
    {
      if (double.IsNaN(ratio) || ratio <= 0.0)
        ratio = DockDefaults.GetDefaultNewPaneRatioForSideDock(sourceKind, targetKind);

      return DockDefaults.ClampLayoutRatio(ratio);
    }

    private static double NormalizeAutoHideStripRatio(double ratio)
    {
      if (double.IsNaN(ratio) || ratio <= 0.0)
        ratio = DefaultAutoHideStripRatio;

      return DockDefaults.ClampLayoutRatio(ratio);
    }

    private static int CountGroups(DockNode root, DockContentKind kind)
    {
      var c = 0;
      foreach (var n in root.TraverseDepthFirst(true))
        if (n is DockGroupNode g && g.ContentKind == kind)
          c++;
      return c;
    }

    private static void RemoveContentEverywhereInternal(DockNode root, string persistKey, List<EmptiedGroup> emptiedGroups, List<string> emptiedAutoHides)
    {
      foreach (var node in root.TraverseDepthFirst(true))
      {
        if (node is DockGroupNode g)
        {
          var before = g.Items.Count;
          if (!g.Remove(persistKey)) continue;

          if (before > 0 && g.Items.Count == 0)
            emptiedGroups.Add(new EmptiedGroup(g.NodeId, g.ContentKind));

          continue;
        }

        if (node is DockAutoHideNode a)
        {
          var before = a.Items.Count;
          if (!a.Remove(persistKey)) continue;

          if (before > 0 && a.Items.Count == 0)
            emptiedAutoHides.Add(a.NodeId);

          continue;
        }
      }
    }

    private static bool TryFindGroupContainingKey(DockNode root, string persistKey, out DockGroupNode group)
    {
      foreach (var n in root.TraverseDepthFirst(true))
      {
        if (n is not DockGroupNode g) continue;

        for (int i = 0; i < g.Items.Count; i++)
          if (string.Equals(g.Items[i].PersistKey, persistKey, StringComparison.Ordinal))
          {
            group = g;
            return true;
          }
      }

      group = null!;
      return false;
    }

    private static bool TryFindAutoHideContainingKey(DockNode root, string persistKey, out DockAutoHideNode autoHide)
    {
      foreach (var n in root.TraverseDepthFirst(true))
      {
        if (n is not DockAutoHideNode a) continue;

        for (int i = 0; i < a.Items.Count; i++)
          if (string.Equals(a.Items[i].PersistKey, persistKey, StringComparison.Ordinal))
          {
            autoHide = a;
            return true;
          }
      }

      autoHide = null!;
      return false;
    }

    private static string? TryGetLayoutItemState(DockGroupNode group, string persistKey)
    {
      for (int i = 0; i < group.Items.Count; i++)
        if (string.Equals(group.Items[i].PersistKey, persistKey, StringComparison.Ordinal))
          return group.Items[i].State;

      return null;
    }

    private static string? TryGetAutoHideItemState(DockAutoHideNode strip, string persistKey)
    {
      for (int i = 0; i < strip.Items.Count; i++)
        if (string.Equals(strip.Items[i].PersistKey, persistKey, StringComparison.Ordinal))
          return strip.Items[i].State;

      return null;
    }

    // Group Leaf Removal (Targeted) =================================================================================

    private static DockNode RemoveGroupLeafById(DockNode root, string groupId, out bool removed)
    {
      var next = RemoveGroupLeafByIdRecursive(root, groupId, out removed);

      if (next is null)
      {
        removed = true;
        return new DockGroupNode(DockContentKind.Document);
      }

      next.SetParentInternal(null);
      return next;
    }

    private static DockNode? RemoveGroupLeafByIdRecursive(DockNode node, string groupId, out bool removed)
    {
      removed = false;

      if (node is DockGroupNode g)
      {
        if (string.Equals(g.NodeId, groupId, StringComparison.Ordinal))
        {
          removed = true;
          g.SetParentInternal(null);
          return null;
        }
        return g;
      }

      if (node is DockSplitNode s)
      {
        var first = RemoveGroupLeafByIdRecursive(s.First, groupId, out var r0);
        var second = RemoveGroupLeafByIdRecursive(s.Second, groupId, out var r1);

        removed = r0 || r1;
        if (!removed) return s;

        if (first is null && second is null) return null;

        if (first is null)
        {
          second!.SetParentInternal(null);
          return second;
        }

        if (second is null)
        {
          first.SetParentInternal(null);
          return first;
        }

        if (!ReferenceEquals(first, s.First)) s.ReplaceChild(s.First, first);
        if (!ReferenceEquals(second, s.Second)) s.ReplaceChild(s.Second, second);

        return s;
      }

      if (node is DockFloatingNode f)
      {
        var inner = RemoveGroupLeafByIdRecursive(f.Root, groupId, out removed);
        if (!removed) return f;

        if (inner is null) return null;

        if (!ReferenceEquals(inner, f.Root)) f.ReplaceRoot(inner);
        return f;
      }

      return node;
    }

    // AutoHide Leaf Removal (Targeted) =============================================================================

    private static DockNode RemoveAutoHideLeafById(DockNode root, string nodeId, out bool removed)
    {
      var next = RemoveAutoHideLeafByIdRecursive(root, nodeId, out removed);

      if (next is null)
      {
        removed = true;
        return new DockGroupNode(DockContentKind.Document);
      }

      next.SetParentInternal(null);
      return next;
    }

    private static DockNode? RemoveAutoHideLeafByIdRecursive(DockNode node, string nodeId, out bool removed)
    {
      removed = false;

      if (node is DockAutoHideNode a)
      {
        if (string.Equals(a.NodeId, nodeId, StringComparison.Ordinal))
        {
          removed = true;
          a.SetParentInternal(null);
          return null;
        }
        return a;
      }

      if (node is DockSplitNode s)
      {
        var first = RemoveAutoHideLeafByIdRecursive(s.First, nodeId, out var r0);
        var second = RemoveAutoHideLeafByIdRecursive(s.Second, nodeId, out var r1);

        removed = r0 || r1;
        if (!removed) return s;

        if (first is null && second is null) return null;

        if (first is null)
        {
          second!.SetParentInternal(null);
          return second;
        }

        if (second is null)
        {
          first.SetParentInternal(null);
          return first;
        }

        if (!ReferenceEquals(first, s.First)) s.ReplaceChild(s.First, first);
        if (!ReferenceEquals(second, s.Second)) s.ReplaceChild(s.Second, second);

        return s;
      }

      if (node is DockFloatingNode f)
      {
        var inner = RemoveAutoHideLeafByIdRecursive(f.Root, nodeId, out removed);
        if (!removed) return f;

        if (inner is null) return null;

        if (!ReferenceEquals(inner, f.Root)) f.ReplaceRoot(inner);
        return f;
      }

      return node;
    }
  }

  /// <summary>ToolWindow 영역 생성 위치</summary>
  public enum DockToolAreaPlacement
  {
    Right,
    Bottom,
    Left,
    Top
  }

  /// <summary>드롭 방향</summary>
  public enum DockDropSide
  {
    Center,
    Left,
    Right,
    Top,
    Bottom
  }
}

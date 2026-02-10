
using System;
using System.Collections.Generic;
using System.Drawing;

using VsLikeDoking.Abstractions;
using VsLikeDoking.Layout.Nodes;
using VsLikeDoking.Utils;

namespace VsLikeDoking.Layout.Model
{
  /// <summary>Layout 트리(DockNode 계열)를 정합성 검사 + 자동 보정하는 역할</summary>
  /// <remarks>UI와 무관하게 "트리가 깨지지 않게" 만드는것(Parent 재설정, 빈 그룹정리, Split 축약, Ratio 범위 보정, ActiveKey 유요화, 중복키 제거)이다.</remarks>
  public static class DockValidator
  {
    // Public ====================================================================

    /// <summary>레이아웃 트리를 검사하고 가능한 범위에서 자동 보정한 뒤 루트를 반환한다.</summary>
    /// <remarks>
    /// 기본 모드에서는 "구조 유지"를 우선한다(빈 Group/AutoHide를 이유로 Split을 축약하지 않는다).
    /// 닫기 후 빈 ToolWindow를 실제로 접어야 할 때만 ValidateAndFix(root, pruneEmptyToolLeaves:true)를 사용한다.
    /// </remarks>
    public static DockNode ValidateAndFix(DockNode root)
      => ValidateAndFix(root, pruneEmptyToolLeaves: false);

    /// <summary>레이아웃 트리를 검사/보정하고, 옵션에 따라 빈 ToolWindow leaf를 축약한다.</summary>
    public static DockNode ValidateAndFix(DockNode root, bool pruneEmptyToolLeaves)
    {
      Guard.NotNull(root);

      DockNode fixedRoot = FixNodeRecursive(root, null, pruneEmptyToolLeaves);
      fixedRoot.SetParentInternal(null);
      return fixedRoot;
    }

    public static void RebuildParents(DockNode root)
    {
      Guard.NotNull(root);
      RebuildParentsRecursive(root, null);
    }

    // Core =====================================================================

    private static DockNode FixNodeRecursive(DockNode node, DockNode? parent, bool pruneEmptyToolLeaves)
    {
      node.SetParentInternal(parent);

      switch (node.Kind)
      {
        case DockNodeKind.Group:
          FixGroup((DockGroupNode)node);
          return node;

        case DockNodeKind.Split:
          return FixSplit((DockSplitNode)node, parent, pruneEmptyToolLeaves);

        case DockNodeKind.Floating:
          return FixFloating((DockFloatingNode)node, parent, pruneEmptyToolLeaves);

        case DockNodeKind.AutoHide:
          FixAutoHide((DockAutoHideNode)node);
          return node;

        default:
          return node;
      }
    }

    private static DockNode FixSplit(DockSplitNode split, DockNode? parent, bool pruneEmptyToolLeaves)
    {
      split.Ratio = MathEx.Clamp(split.Ratio, 0.05, 0.95);

      var first = FixNodeRecursive(split.First, split, pruneEmptyToolLeaves);
      var second = FixNodeRecursive(split.Second, split, pruneEmptyToolLeaves);

      if (!ReferenceEquals(first, split.First)) split.ReplaceChild(split.First, first);
      if (!ReferenceEquals(second, split.Second)) split.ReplaceChild(split.Second, second);

      var firstEmpty = IsEmptyLeaf(split.First, pruneEmptyToolLeaves);
      var secondEmpty = IsEmptyLeaf(split.Second, pruneEmptyToolLeaves);

      if (firstEmpty && !secondEmpty)
      {
        split.First.SetParentInternal(null);
        split.Second.SetParentInternal(parent);
        return split.Second;
      }
      if (!firstEmpty && secondEmpty)
      {
        split.Second.SetParentInternal(null);
        split.First.SetParentInternal(parent);
        return split.First;
      }

      if (firstEmpty && secondEmpty)
      {
        split.Second.SetParentInternal(null);
        split.First.SetParentInternal(parent);
        return split.First;
      }

      return split;
    }

    private static DockNode FixFloating(DockFloatingNode floating, DockNode? parent, bool pruneEmptyToolLeaves)
    {
      var bounds = floating.Bounds;
      if (bounds.Width <= 0 || bounds.Height <= 0)
      {
        floating.Bounds = new Rectangle(bounds.X, bounds.Y, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
      }

      var root = FixNodeRecursive(floating.Root, floating, pruneEmptyToolLeaves);
      if (!ReferenceEquals(root, floating.Root)) floating.ReplaceRoot(root);

      return floating;
    }

    private static void FixGroup(DockGroupNode node)
    {
      RemoveDuplicateGroupItems(node);

      if (node.ActiveKey is not null)
      {
        if (!ContainsGroupKey(node, node.ActiveKey))
          node.SetActive(node.Items.Count > 0 ? node.Items[0].PersistKey : node.ActiveKey);

        if (node.Items.Count == 0) node.SetActive(node.ActiveKey);
      }
      else
      {
        if (node.Items.Count > 0) node.SetActive(node.Items[0].PersistKey);
      }
    }

    private static void FixAutoHide(DockAutoHideNode node)
    {
      RemoveDuplicateAutoHideItems(node);

      if (node.ActiveKey is not null)
      {
        if (!ContainsAutoHideKey(node, node.ActiveKey))
          node.SetActive(node.Items.Count > 0 ? node.Items[0].PersistKey : node.ActiveKey);

        if (node.Items.Count == 0) node.SetActive(node.ActiveKey);
      }
      else
      {
        if (node.Items.Count > 0) node.SetActive(node.Items[0].PersistKey);
      }
    }

    // Parent Only ===============================================================

    private static void RebuildParentsRecursive(DockNode node, DockNode? parent)
    {
      node.SetParentInternal(parent);

      foreach (var child in node.EnumerateChildren())
      {
        if (child is null) continue;
        RebuildParentsRecursive(child, node);
      }
    }

    // Helpers ==================================================================

    private static bool IsEmptyLeaf(DockNode node, bool pruneEmptyToolLeaves)
    {
      // 기본(구조 유지) 모드에서는 "비어있다"는 이유로 split을 축약하지 않는다.
      if (!pruneEmptyToolLeaves) return false;

      if (node is DockGroupNode g)
      {
        // 문서 영역은 VS처럼 "빈 문서 영역" 유지
        if (g.ContentKind == DockContentKind.Document) return false;

        // ToolWindow는 "닫기 후"에만 빈 leaf면 접는다.
        return g.Items.Count == 0;
      }

      if (node is DockAutoHideNode a)
        return a.Items.Count == 0;

      return false;
    }

    private static bool ContainsGroupKey(DockGroupNode node, string key)
    {
      for (int i = 0; i < node.Items.Count; i++)
        if (string.Equals(node.Items[i].PersistKey, key, StringComparison.Ordinal)) return true;

      return false;
    }

    private static void RemoveDuplicateGroupItems(DockGroupNode node)
    {
      if (node.Items.Count <= 1) return;

      var seen = new HashSet<string>(StringComparer.Ordinal);
      var toRemove = new List<string>();

      for (int i = 0; i < node.Items.Count; i++)
      {
        var key = node.Items[i].PersistKey;
        if (!seen.Add(key)) toRemove.Add(key);
      }

      for (int i = 0; i < toRemove.Count; i++)
        node.Remove(toRemove[i]);
    }

    private static bool ContainsAutoHideKey(DockAutoHideNode node, string key)
    {
      for (int i = 0; i < node.Items.Count; i++)
        if (string.Equals(node.Items[i].PersistKey, key, StringComparison.Ordinal)) return true;

      return false;
    }

    private static void RemoveDuplicateAutoHideItems(DockAutoHideNode node)
    {
      if (node.Items.Count <= 1) return;

      var seen = new HashSet<string>(StringComparer.Ordinal);
      var toRemove = new List<string>();

      for (int i = 0; i < node.Items.Count; i++)
      {
        var key = node.Items[i].PersistKey;
        if (!seen.Add(key)) toRemove.Add(key);
      }

      for (int i = 0; i < toRemove.Count; i++)
        node.Remove(toRemove[i]);
    }
  }
}

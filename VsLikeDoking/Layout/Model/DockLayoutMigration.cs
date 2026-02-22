using System;
using System.Collections.Generic;

using VsLikeDoking.Abstractions;
using VsLikeDoking.Layout.Nodes;

namespace VsLikeDoking.Layout.Model
{
  /// <summary>구버전 레이아웃을 현재 역할 규칙(문서/툴)로 정규화한다.</summary>
  public static class DockLayoutMigration
  {
    /// <summary>문서/툴 배치 규칙을 위반한 항목을 이동시킨다.</summary>
    public static DockNode NormalizeRoles(DockNode root)
    {
      var docFromAutoHide = new List<(string Key, string? State)>();
      var toolFromGroups = new List<(string Key, string? State)>();

      foreach (var node in root.TraverseDepthFirst(true))
      {
        if (node is DockAutoHideNode ah && ah.ContentKind == DockContentKind.Document)
        {
          for (int i = 0; i < ah.Items.Count; i++)
            docFromAutoHide.Add((ah.Items[i].PersistKey, ah.Items[i].State));
          ah.Clear();
        }

        if (node is DockGroupNode g && g.ContentKind == DockContentKind.ToolWindow)
        {
          for (int i = 0; i < g.Items.Count; i++)
            toolFromGroups.Add((g.Items[i].PersistKey, g.Items[i].State));
          g.Clear();
        }
      }

      if (docFromAutoHide.Count > 0)
      {
        var docGroup = DockMutator.FindFirstGroupByKind(root, DockContentKind.Document) ?? new DockGroupNode(DockContentKind.Document);
        if (!ReferenceEquals(docGroup, root) && docGroup.Parent is null && root is not DockGroupNode)
          root = new DockSplitNode(DockSplitOrientation.Vertical, 0.8, docGroup, root);

        for (int i = 0; i < docFromAutoHide.Count; i++)
          docGroup.Add(docFromAutoHide[i].Key, docFromAutoHide[i].State);
      }

      if (toolFromGroups.Count > 0)
      {
        root = DockMutator.EnsureAutoHideStrip(root, DockAutoHideSide.Right, out var rightStrip, DockContentKind.ToolWindow);
        for (int i = 0; i < toolFromGroups.Count; i++)
          rightStrip.Add(toolFromGroups[i].Key, toolFromGroups[i].State);
      }

      return DockValidator.ValidateAndFix(root, pruneEmptyToolLeaves: true);
    }
  }
}

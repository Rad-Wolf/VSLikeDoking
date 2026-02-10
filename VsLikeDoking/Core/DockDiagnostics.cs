using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

using VsLikeDoking.Layout.Nodes;
using VsLikeDoking.Utils;

namespace VsLikeDoking.Core
{
  public static class DockDiagnostics
  {
    // Dump ====================================================================

    /// <summary>레이아웃 트리를 사람이 읽을 수 있는 문자열로 덤프한다.</summary>
    public static string DumpTree(DockNode root, bool includeNodeIds = true, bool includeItems = true)
    {
      Guard.NotNull(root);

      var sb = new StringBuilder(2048);
      DumpNodeInternal(sb, root, 0, includeNodeIds, includeItems);
      return sb.ToString();
    }

    /// <summary>레이아웃 트리의 요약(노드 개수/종류/컨탠츠 개수)을 반환한다.</summary>
    public static DockLayoutSummary GetSummary(DockNode root)
    {
      Guard.NotNull(root);

      int _tn, _gn, _sn, _fn, _an, _tgi, _tai;
      _tn = 0; _gn = 0; _sn = 0; _fn = 0; _an = 0; _tgi = 0; _tai = 0;

      foreach (var node in root.TraverseDepthFirst(true))
      {
        _tn++;
        if (node is DockGroupNode gn)
        {
          _gn++;
          _tgi += gn.Items.Count;
        }
        else if (node is DockSplitNode sn)
        {
          _sn++;
        }
        else if (node is DockFloatingNode fn)
        {
          _fn++;
        }
        else if (node is DockAutoHideNode an)
        {
          _an++;
          _tai += an.Items.Count;
        }
      }
      return new DockLayoutSummary { TotalNodes = _tn, GroupNodes = _gn, TotalGroupItems = _tgi, SplitNodes = _sn, FloatingNodes = _fn, AutoHideNodes = _an, TotalAutoHideItems = _tai };
    }

    // Validate ==================================================================

    /// <summary>NodeId 중복검사</summary>
    /// <remarks>중복이 있으면 false. duplicates에 중복된 NodeId  목록이 채워진다.</remarks>
    public static bool ValidateUniqueNodeIds(DockNode root, out List<string> duplicates)
    {
      Guard.NotNull(root);

      duplicates = new List<string>();
      var seen = new HashSet<string>(StringComparer.Ordinal);

      foreach (var node in root.TraverseDepthFirst(true))
      {
        var id = node.NodeId ?? string.Empty;
        if (id.Length == 0) continue;

        if (!seen.Add(id)) duplicates.Add(id);
      }
      return duplicates.Count == 0;
    }

    /// <summary>PersistKey 중복을 검사한다.</summary>
    /// <remarks>Group/AutoHide 전체 합산. 중복이 있으면 false, 중복된 목록은 duplicates에 채워진다.</remarks>
    public static bool ValidateUniquePersistKeys(DockNode root, out List<string> duplicates)
    {
      Guard.NotNull(root);

      duplicates = new List<string>();
      var seen = new HashSet<string>(StringComparer.Ordinal);

      foreach (var node in root.TraverseDepthFirst(true))
      {
        if (node is DockGroupNode gn)
        {
          for (int i = 0; i < gn.Items.Count; i++)
          {
            var key = gn.Items[i].PersistKey ?? string.Empty;
            if (key.Length == 0) continue;
            if (!seen.Add(key)) duplicates.Add(key);
          }
        }
        else if (node is DockAutoHideNode an)
        {
          for (int i = 0; i < an.Items.Count; i++)
          {
            var key = an.Items[i].PersistKey ?? string.Empty;
            if (key.Length == 0) continue;
            if (!seen.Add(key)) duplicates.Add(key);
          }
        }
      }
      return duplicates.Count == 0;
    }

    public static string Diagnose(DockNode root)
    {
      Guard.NotNull(root);
      var sb = new StringBuilder();

      if (!ValidateUniqueNodeIds(root, out var dupIds))
      {
        sb.Append("[Error] Duplicate NodeId:");
        for (int i = 0; i < dupIds.Count; i++) sb.Append($"  - {dupIds[i]}");
      }
      if (!ValidateUniquePersistKeys(root, out var dupKeys))
      {
        sb.Append("[Error] Duplicate PersistKey:");
        for (int i = 0; i < dupKeys.Count; i++) sb.Append($"  -  {dupKeys[i]}");
      }
      return sb.ToString();
    }

    // Internal ===================================================================

    private static void DumpNodeInternal(StringBuilder sb, DockNode node, int depth, bool includeNodeIds, bool includeItems)
    {
      AppendIndent(sb, depth);

      if (node is DockGroupNode gn)
      {
        sb.Append("Group");
        if (includeNodeIds) sb.Append($" id = {gn.NodeId}");
        sb.Append($" kind = {gn.ContentKind} items = {gn.Items.Count}");
        if (!string.IsNullOrWhiteSpace(gn.ActiveKey)) sb.Append($" active = {gn.ActiveKey}");
        sb.AppendLine();

        if (includeItems)
        {
          for (int i = 0; i < gn.Items.Count; i++)
          {
            AppendIndent(sb, depth + 1);
            sb.Append($"- {gn.Items[i].PersistKey}");
            if (!string.IsNullOrWhiteSpace(gn.Items[i].State)) sb.Append($" (state!)");
            sb.AppendLine();
          }
        }
        return;
      }

      if (node is DockSplitNode sn)
      {
        sb.Append("Split");
        if (includeNodeIds) sb.Append($" id = {sn.NodeId}");
        sb.Append($" ori = {sn.Orientation} ratio = {sn.Ratio:0.###}");
        sb.AppendLine();

        DumpNodeInternal(sb, sn.First, depth + 1, includeNodeIds, includeItems);
        DumpNodeInternal(sb, sn.Second, depth + 1, includeNodeIds, includeItems);
        return;
      }

      if (node is DockFloatingNode fn)
      {
        sb.Append("Floating");
        if (includeNodeIds) sb.Append($" id = {fn.NodeId}");
        sb.Append($" bounds = {RectToText(fn.Bounds)}");
        sb.AppendLine();

        DumpNodeInternal(sb, fn.Root, depth + 1, includeNodeIds, includeItems);
        return;
      }

      if (node is DockAutoHideNode an)
      {
        sb.Append("AutoHide");
        if (includeNodeIds) sb.Append($" id = {an.NodeId}");
        sb.Append($" side = {an.Side} kind = {an.ContentKind} items = {an.Items.Count}");
        if (!string.IsNullOrWhiteSpace(an.ActiveKey)) sb.Append($" active = {an.ActiveKey}");
        sb.AppendLine();

        if (includeItems)
        {
          for (int i = 0; i < an.Items.Count; i++)
          {
            AppendIndent(sb, depth + 1);
            sb.Append($" -  {an.Items[i].PersistKey}");
            if (an.Items[i].PopupSize.HasValue) sb.Append($" popup = {SizeToText(an.Items[i].PopupSize!.Value)}");
            if (!string.IsNullOrWhiteSpace(an.Items[i].State)) sb.Append(" (state!)");
            sb.AppendLine();
          }
        }
        return;
      }

      sb.Append(node.Kind.ToString());
      if (includeNodeIds) sb.Append($" id = {node.NodeId}");
      sb.AppendLine();

      foreach (var child in node.EnumerateChildren())
        DumpNodeInternal(sb, child, depth + 1, includeNodeIds, includeItems);
    }

    private static void AppendIndent(StringBuilder sb, int depth)
    {
      for (int i = 0; i < depth; i++) sb.Append("  ");
    }

    private static string RectToText(Rectangle rect)
      => $"({rect.X},{rect.Y},{rect.Width},{rect.Height})";
    private static string SizeToText(Size size)
      => $"({size.Width},{size.Height})";
  }

  public readonly struct DockLayoutSummary
  {
    public int TotalNodes { get; init; }
    public int GroupNodes { get; init; }
    public int SplitNodes { get; init; }
    public int FloatingNodes { get; init; }
    public int AutoHideNodes { get; init; }

    public int TotalGroupItems { get; init; }
    public int TotalAutoHideItems { get; init; }

    public override string ToString()
    {
      return $"nodes={TotalNodes} (group={GroupNodes}, split={SplitNodes}, float={FloatingNodes}, autohide={AutoHideNodes}), items=(group={TotalGroupItems}, autohide={TotalAutoHideItems})";
    }
  }
}
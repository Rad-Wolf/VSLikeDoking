using System;
using System.Collections.Generic;
using System.Drawing;

using VsLikeDoking.Abstractions;
using VsLikeDoking.Layout.Model;
using VsLikeDoking.Layout.Nodes;
using VsLikeDoking.Utils;

namespace VsLikeDoking.Layout.Persistence
{
  /// <summary>Layout 트리(DockNode) ↔ 저장용 DTO(DockLayoutDto) 변환 담당</summary>
  /// <remarks>파일 읽기/쓰기는 DockLayoutJson에서한다.</remarks>
  public static class DockLayoutSerializer
  {
    // Public ====================================================================

    /// <summary>레이아웃 트리를 저장용 DTO로 변환한다.</summary>
    public static DockLayoutDto ToDto(DockNode root, int version = 1)
    {
      Guard.NotNull(root);

      return new DockLayoutDto { Version = version, Root = ToNodeDto(root) };
    }

    /// <summary>저장용 DTO를 레이아웃 트리로 변환한다. (기본:ValidateAndFix 수행)</summary>
    public static DockNode FromDto(DockLayoutDto dto, bool validate = true)
    {
      Guard.NotNull(dto);

      if (dto.Root is null) return DockDefaults.CreateEmptyDocumentLayout();

      var root = NormalizeRoles(FromNodeDto(dto.Root));
      if (validate) root = DockValidator.ValidateAndFix(root);
      else root.SetParentInternal(null);

      return root;
    }

    // Node -> DTO =============================================================

    private static DockNodeDto ToNodeDto(DockNode node)
    {
      switch (node.Kind)
      {
        case DockNodeKind.Group:
          return ToGroupDto((DockGroupNode)node);
        case DockNodeKind.Split:
          return ToSplitDto((DockSplitNode)node);
        case DockNodeKind.Floating:
          return ToFloatingDto((DockFloatingNode)node);
        case DockNodeKind.AutoHide:
          return ToAutoHideDto((DockAutoHideNode)node);
        default:
          return ToGroupDto(new DockGroupNode(DockContentKind.Document, node.NodeId));
      }
    }

    private static DockNodeDto ToGroupDto(DockGroupNode node)
    {
      var items = new List<DockContentItemDto>(node.Items.Count);
      for (int i = 0; i < node.Items.Count; i++)
      {
        items.Add(new DockContentItemDto { PersistKey = node.Items[i].PersistKey, State = node.Items[i].State });
      }
      return new DockNodeDto { Kind = DockNodeKind.Group, NodeId = node.NodeId, ContentKind = node.ContentKind, Items = items, ActiveKey = node.ActiveKey };
    }

    private static DockNodeDto ToSplitDto(DockSplitNode node)
    {
      return new DockNodeDto { Kind = DockNodeKind.Split, NodeId = node.NodeId, Orientation = node.Orientation, Ratio = node.Ratio, First = ToNodeDto(node.First), Second = ToNodeDto(node.Second) };
    }
    private static DockNodeDto ToFloatingDto(DockFloatingNode node)
    {
      return new DockNodeDto { Kind = DockNodeKind.Floating, NodeId = node.NodeId, Bounds = ToRectDto(node.Bounds), Root = ToNodeDto(node.Root) };
    }
    private static DockNodeDto ToAutoHideDto(DockAutoHideNode node)
    {
      var items = new List<DockContentItemDto>(node.Items.Count);
      for (int i = 0; i < node.Items.Count; i++)
      {
        var it = node.Items[i];
        items.Add(new DockContentItemDto { PersistKey = it.PersistKey, State = it.State, PopupSize = it.PopupSize.HasValue ? ToSizeDto(it.PopupSize.Value) : null });
      }
      return new DockNodeDto { Kind = DockNodeKind.AutoHide, NodeId = node.NodeId, Side = node.Side, ContentKind = node.ContentKind, Items = items, ActiveKey = node.ActiveKey };
    }

    // DTO -> Node =============================================================

    private static DockNode FromNodeDto(DockNodeDto dto)
    {
      switch (dto.Kind)
      {
        case DockNodeKind.Group:
          return FromGroupDto(dto);

        case DockNodeKind.Split:
          return FromSplitDto(dto);

        case DockNodeKind.Floating:
          return FromFloatingDto(dto);

        case DockNodeKind.AutoHide:
          return FromAutoHideDto(dto);

        default:
          return new DockGroupNode(DockContentKind.Document, dto.NodeId);
      }
    }

    private static DockNode FromGroupDto(DockNodeDto dto)
    {
      var kind = dto.ContentKind ?? DockContentKind.Document;
      var group = new DockGroupNode(kind, dto.NodeId);

      if (dto.Items is not null)
      {
        for (int i = 0; i < dto.Items.Count; i++)
        {
          var it = dto.Items[i];
          if (string.IsNullOrWhiteSpace(it.PersistKey)) continue;
          group.Add(it.PersistKey!, it.State);
        }
      }

      if (!string.IsNullOrWhiteSpace(dto.ActiveKey)) group.SetActive(dto.ActiveKey!);

      return group;
    }

    private static DockNode FromSplitDto(DockNodeDto dto)
    {
      var orientation = dto.Orientation ?? DockSplitOrientation.Vertical;
      var ratio = dto.Ratio ?? 0.5;
      var first = dto.First is null ? new DockGroupNode(DockContentKind.Document) : FromNodeDto(dto.First);
      var second = dto.Second is null ? new DockGroupNode(DockContentKind.ToolWindow) : FromNodeDto(dto.Second);
      return new DockSplitNode(orientation, ratio, first, second, dto.NodeId);
    }

    private static DockNode FromFloatingDto(DockNodeDto dto)
    {
      var root = dto.Root is null ? new DockGroupNode(DockContentKind.Document) : FromNodeDto(dto.Root);
      var bounds = dto.Bounds is null ? new Rectangle(100, 100, 800, 600) : FromRectDto(dto.Bounds);
      return new DockFloatingNode(root, bounds, dto.NodeId);
    }

    private static DockNode FromAutoHideDto(DockNodeDto dto)
    {
      var side = dto.Side ?? DockAutoHideSide.Left;
      var kind = dto.ContentKind ?? DockContentKind.ToolWindow;
      var node = new DockAutoHideNode(side, kind, dto.NodeId);

      if (dto.Items is not null)
      {
        for (int i = 0; i < dto.Items.Count; i++)
        {
          var it = dto.Items[i];
          if (string.IsNullOrWhiteSpace(it.PersistKey)) continue;

          Size? popupSize = null;
          if (it.PopupSize is not null) popupSize = FromSizeDto(it.PopupSize);

          node.Add(it.PersistKey!, it.State, popupSize);
        }
      }
      if (!string.IsNullOrWhiteSpace(dto.ActiveKey)) node.SetActive(dto.ActiveKey!);

      return node;
    }

    
    private static DockNode NormalizeRoles(DockNode root)
    {
      var toolBySide = new Dictionary<DockAutoHideSide, List<DockAutoHideItem>>();
      foreach (DockAutoHideSide side in Enum.GetValues(typeof(DockAutoHideSide)))
        toolBySide[side] = new List<DockAutoHideItem>();

      var documentOverflow = new List<DockGroupItem>();

      var normalizedDocumentRoot = NormalizeDocumentNode(root, toolBySide, documentOverflow) ?? new DockGroupNode(DockContentKind.Document);

      if (documentOverflow.Count > 0)
      {
        var firstDoc = DockMutator.FindFirstGroupByKind(normalizedDocumentRoot, DockContentKind.Document) ?? (normalizedDocumentRoot as DockGroupNode);
        if (firstDoc is null)
        {
          firstDoc = new DockGroupNode(DockContentKind.Document);
          normalizedDocumentRoot = new DockSplitNode(DockSplitOrientation.Vertical, 0.5, normalizedDocumentRoot, firstDoc);
        }

        for (int i = 0; i < documentOverflow.Count; i++)
        {
          var it = documentOverflow[i];
          if (firstDoc.IndexOf(it.PersistKey) >= 0) continue;
          firstDoc.Add(it);
        }
      }

      DockNode next = normalizedDocumentRoot;
      foreach (var pair in toolBySide)
      {
        if (pair.Value.Count == 0) continue;

        next = DockMutator.EnsureAutoHideStrip(next, pair.Key, out var strip, DockContentKind.ToolWindow);
        for (int i = 0; i < pair.Value.Count; i++)
        {
          var it = pair.Value[i];
          if (strip.Contains(it.PersistKey)) continue;
          strip.Add(it);
        }
      }

      return next;
    }

    private static DockNode? NormalizeDocumentNode(DockNode node, Dictionary<DockAutoHideSide, List<DockAutoHideItem>> toolBySide, List<DockGroupItem> documentOverflow)
    {
      if (node is DockGroupNode group)
      {
        if (group.ContentKind == DockContentKind.ToolWindow)
        {
          for (int i = 0; i < group.Items.Count; i++)
            toolBySide[DockAutoHideSide.Right].Add(new DockAutoHideItem(group.Items[i].PersistKey, group.Items[i].State));
          return null;
        }

        var doc = new DockGroupNode(DockContentKind.Document, group.NodeId);
        for (int i = 0; i < group.Items.Count; i++)
          doc.Add(group.Items[i].PersistKey, group.Items[i].State);

        if (!string.IsNullOrWhiteSpace(group.ActiveKey)) doc.SetActive(group.ActiveKey);
        return doc;
      }

      if (node is DockAutoHideNode ah)
      {
        if (ah.ContentKind == DockContentKind.Document)
        {
          for (int i = 0; i < ah.Items.Count; i++)
            documentOverflow.Add(new DockGroupItem(ah.Items[i].PersistKey, ah.Items[i].State));
          return null;
        }

        for (int i = 0; i < ah.Items.Count; i++)
          toolBySide[ah.Side].Add(new DockAutoHideItem(ah.Items[i].PersistKey, ah.Items[i].State) { PopupSize = ah.Items[i].PopupSize });
        return null;
      }

      if (node is DockSplitNode split)
      {
        var first = NormalizeDocumentNode(split.First, toolBySide, documentOverflow);
        var second = NormalizeDocumentNode(split.Second, toolBySide, documentOverflow);

        if (first is null && second is null) return null;
        if (first is null) return second;
        if (second is null) return first;

        return new DockSplitNode(split.Orientation, split.Ratio, first, second, split.NodeId);
      }

      if (node is DockFloatingNode floating)
      {
        var fr = NormalizeDocumentNode(floating.Root, toolBySide, documentOverflow);
        if (fr is null) return null;
        return new DockFloatingNode(fr, floating.Bounds, floating.NodeId);
      }

      return node;
    }
// DTO Convert ==============================================================

    private static DockRectDto ToRectDto(Rectangle r)
    {
      return new DockRectDto { X = r.X, Y = r.Y, Width = r.Width, Height = r.Height };
    }

    private static Rectangle FromRectDto(DockRectDto r)
    {
      var w = Math.Max(1, r.Width);
      var h = Math.Max(1, r.Height);
      return new Rectangle(r.X, r.Y, w, h);
    }

    private static DockSizeDto ToSizeDto(Size s)
    {
      return new DockSizeDto { Width = s.Width, Height = s.Height };
    }

    private static Size FromSizeDto(DockSizeDto s)
    {
      var w = Math.Max(1, s.Width);
      var h = Math.Max(1, s.Height);
      return new Size(w, h);
    }
  }
}
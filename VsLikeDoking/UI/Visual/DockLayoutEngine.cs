// VsLikeDocking - VsLikeDoking - UI/Visual/DockLayoutEngine.cs - DockLayoutEngine - (File)

using System;
using System.Collections;
using System.Drawing;
using System.Reflection;

using VsLikeDoking.Abstractions;
using VsLikeDoking.Layout.Nodes;
using VsLikeDoking.Rendering.Theme;
using VsLikeDoking.Utils;

namespace VsLikeDoking.UI.Visual
{
  /// <summary>DockNode 트리를 사각형 캐시(DockVisualTree)로 변환하는 엔진</summary>
  /// <remarks>렌더/히트테스트가 빠르게 동작하도록 Split/Group/Tab/버튼 영역을 평탄화해 저장한다.</remarks>
  public sealed class DockLayoutEngine
  {
    // Fields =====================================================================================================

    private DockMetrics _Metrics;

    // Properties ==================================================================================================

    /// <summary>픽셀 규격(높이/패딩/두께 등)</summary>
    public DockMetrics Metrics
    {
      get { return _Metrics; }
      set { _Metrics = value ?? throw new ArgumentNullException(nameof(value)); }
    }

    /// <summary>알 수 없는 DockNode(예: Floating/AutoHide 등)를 외부에서 빌드할 수 있는 확장 지점</summary>
    /// <remarks>true를 반환하면 처리 완료로 간주하고, 기본 처리(AddEmptyRegion)를 수행하지 않는다.</remarks>
    public Func<DockNode, Rectangle, DockVisualTree, bool>? UnknownNodeBuilder { get; set; }

    // Ctor =======================================================================================================

    /// <summary>DockLayoutEngine을 생성한다.</summary>
    public DockLayoutEngine(DockMetrics metrics)
    {
      _Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    // Public =====================================================================================================

    /// <summary>DockNode 트리를 DockVisualTree로 계산한다.</summary>
    public void Build(DockNode? root, Rectangle bounds, DockVisualTree target)
    {
      if (target is null) throw new ArgumentNullException(nameof(target));

      target.BeginFrame(bounds);

      if (root is null || bounds.IsEmpty)
      {
        target.AddEmptyRegion(bounds);
        return;
      }

      VisitNode(root, bounds, target);
    }

    // Internals ==================================================================================================

    private void VisitNode(DockNode node, Rectangle bounds, DockVisualTree tree)
    {
      if (bounds.IsEmpty) return;

      // type 기반으로 단순화(ContentKind switch + cast 중복 제거)
      if (node is DockGroupNode g)
      {
        BuildGroup(g, bounds, tree);
        return;
      }

      if (node is DockSplitNode s)
      {
        BuildSplit(s, bounds, tree);
        return;
      }

      // 확장 지점(예: Floating/AutoHide 등)
      if (UnknownNodeBuilder is not null && UnknownNodeBuilder(node, bounds, tree))
        return;

      // AutoHide 정식 노드 지원(스트립 생성 + 내부 bounds shrink + child 방문)
      if (node is DockAutoHideNode ah)
      {
        BuildAutoHideNode(ah, bounds, tree);
        return;
      }

      // AutoHide 최소 지원(리플렉션 기반). 성공하면 비지 않는다.
      if (TryBuildAutoHideFallback(node, bounds, tree))
        return;

      // 최소 안전망:
      // 타입명이 Floating/AutoHide 계열로 보이면, 내부 DockNode를 reflection으로 찾아서 방문을 시도한다.
      // (정식 구현 전까지 "내용이 비는 문제"를 줄이기 위한 임시 처리)
      if (TryVisitChildNodeFallback(node, bounds, tree))
        return;

      // Floating/AutoHide 등은 현재 최소 구현(비어있는 영역으로 처리)
      tree.AddEmptyRegion(bounds);
    }

    private void BuildSplit(DockSplitNode node, Rectangle bounds, DockVisualTree tree)
    {
      if (TryBuildAutoHideSideSplit(node, bounds, tree))
        return;

      // AutoHide leaf가 비어있는 경우(아이템 없음/유효 키 없음)에는
      // split 비율을 그대로 쓰면 "빈 strip 공간"만 남는다.
      // 이 경우 반대편을 전체 bounds로 그려 시각적인 빈 레일을 제거한다.
      var collapseFirst = IsEffectivelyEmptyVisualLeaf(node.First);
      var collapseSecond = IsEffectivelyEmptyVisualLeaf(node.Second);

      if (collapseFirst && !collapseSecond)
      {
        VisitNode(node.Second, bounds, tree);
        return;
      }

      if (!collapseFirst && collapseSecond)
      {
        VisitNode(node.First, bounds, tree);
        return;
      }

      if (collapseFirst && collapseSecond)
      {
        tree.AddEmptyRegion(bounds);
        return;
      }

      var thickness = _Metrics.SplitterVisualThickness;
      if (thickness < 1) thickness = 1;

      var ratio = MathEx.ClampPer(node.Ratio); // 0..1
      if (node.Orientation == DockSplitOrientation.Vertical)
      {
        var wAvail = bounds.Width - thickness;
        if (wAvail < 0) wAvail = 0;

        var w1 = (int)(wAvail * ratio);
        if (w1 < 0) w1 = 0;
        else if (w1 > wAvail) w1 = wAvail;

        var w2 = wAvail - w1;

        var firstBounds = new Rectangle(bounds.X, bounds.Y, w1, bounds.Height);
        var splitterBounds = new Rectangle(bounds.X + w1, bounds.Y, thickness, bounds.Height);
        var secondBounds = new Rectangle(bounds.X + w1 + thickness, bounds.Y, w2, bounds.Height);

        tree.AddSplit(node, bounds, splitterBounds, DockVisualTree.SplitAxis.Vertical, (float)ratio);
        VisitNode(node.First, firstBounds, tree);
        VisitNode(node.Second, secondBounds, tree);
        return;
      }

      var hAvail = bounds.Height - thickness;
      if (hAvail < 0) hAvail = 0;

      var h1 = (int)(hAvail * ratio);
      if (h1 < 0) h1 = 0;
      else if (h1 > hAvail) h1 = hAvail;

      var h2 = hAvail - h1;

      var firstBoundsH = new Rectangle(bounds.X, bounds.Y, bounds.Width, h1);
      var splitterBoundsH = new Rectangle(bounds.X, bounds.Y + h1, bounds.Width, thickness);
      var secondBoundsH = new Rectangle(bounds.X, bounds.Y + h1 + thickness, bounds.Width, h2);

      tree.AddSplit(node, bounds, splitterBoundsH, DockVisualTree.SplitAxis.Horizontal, (float)ratio);
      VisitNode(node.First, firstBoundsH, tree);
      VisitNode(node.Second, secondBoundsH, tree);
    }

    private bool TryBuildAutoHideSideSplit(DockSplitNode node, Rectangle bounds, DockVisualTree tree)
    {
      DockAutoHideNode? stripNode = null;
      DockNode? contentNode = null;

      if (node.First is DockAutoHideNode ahFirst && node.Second is not DockAutoHideNode)
      {
        stripNode = ahFirst;
        contentNode = node.Second;
      }
      else if (node.Second is DockAutoHideNode ahSecond && node.First is not DockAutoHideNode)
      {
        stripNode = ahSecond;
        contentNode = node.First;
      }

      if (stripNode is null || contentNode is null) return false;

      var side = stripNode.Side;
      var edge = side switch
      {
        DockAutoHideSide.Left => DockVisualTree.DockEdge.Left,
        DockAutoHideSide.Right => DockVisualTree.DockEdge.Right,
        DockAutoHideSide.Top => DockVisualTree.DockEdge.Top,
        _ => DockVisualTree.DockEdge.Bottom,
      };

      var thickness = GetMetricsInt("AutoHideStripThickness", _Metrics.TabStripHeight);
      if (thickness < 1) thickness = Math.Max(1, _Metrics.TabStripHeight);

      var stripBounds = ComputeEdgeBounds(bounds, edge, thickness);
      var contentBounds = ShrinkBoundsByEdge(bounds, edge, thickness);

      if (!stripBounds.IsEmpty)
        VisitNode(stripNode, stripBounds, tree);

      if (!contentBounds.IsEmpty)
        VisitNode(contentNode, contentBounds, tree);

      return true;
    }

    private static bool IsEffectivelyEmptyVisualLeaf(DockNode node)
    {
      if (node is DockAutoHideNode ah)
      {
        var items = ah.Items;
        if (items is null || items.Count == 0) return true;

        for (int i = 0; i < items.Count; i++)
        {
          var key = NormalizeKey(items[i].PersistKey);
          if (key is not null) return false;
        }

        return true;
      }

      // 비어 있는 ToolWindow 그룹도 화면상으로는 의미 없는 공간이므로 split에서 접는다.
      if (node is DockGroupNode g)
        return g.ContentKind == DockContentKind.ToolWindow && g.Items.Count == 0;

      // 비어 있는 브랜치 전파: 자식이 모두 비어 있으면 상위 split도 비어 있다고 본다.
      if (node is DockSplitNode s)
        return IsEffectivelyEmptyVisualLeaf(s.First) && IsEffectivelyEmptyVisualLeaf(s.Second);

      if (node is DockFloatingNode f)
        return IsEffectivelyEmptyVisualLeaf(f.Root);

      return false;
    }

    private void BuildGroup(DockGroupNode node, Rectangle bounds, DockVisualTree tree)
    {
      var captionH = (node.ContentKind == DockContentKind.ToolWindow) ? _Metrics.CaptionHeight : 0;
      var tabStripH = _Metrics.TabStripHeight;

      if (captionH < 0) captionH = 0;
      if (tabStripH < 0) tabStripH = 0;

      var x = bounds.X;
      var y = bounds.Y;
      var w = bounds.Width;
      var h = bounds.Height;

      Rectangle captionBounds = Rectangle.Empty;
      Rectangle tabStripBounds = Rectangle.Empty;

      if (captionH > 0 && h > 0)
      {
        var ch = (captionH <= h) ? captionH : h;
        captionBounds = new Rectangle(x, y, w, ch);
        y += ch;
        h -= ch;
      }

      if (tabStripH > 0 && h > 0)
      {
        var th = (tabStripH <= h) ? tabStripH : h;
        tabStripBounds = new Rectangle(x, y, w, th);
        y += th;
        h -= th;
      }

      if (h < 0) h = 0;

      var contentBounds = new Rectangle(x, y, w, h);
      var groupIndex = tree.AddGroup(node, bounds, tabStripBounds, captionBounds, contentBounds);

      // Caption Close 버튼 (현재: ToolWindow에서만)
      if (!captionBounds.IsEmpty)
      {
        var btn = _Metrics.CaptionButtonSize;
        if (btn > 0)
        {
          var padX = _Metrics.CaptionPaddingX;
          if (padX < 0) padX = 0;

          var cx = captionBounds.Right - padX - btn;
          var cy = captionBounds.Y + (captionBounds.Height - btn) / 2;

          if (cx >= captionBounds.X)
            tree.AddCaptionClose(groupIndex, new Rectangle(cx, cy, btn, btn));
        }
      }

      // Tabs
      if (tabStripBounds.IsEmpty) return;

      var items = node.Items;
      var count = items.Count;
      if (count <= 0) return;

      var gap = _Metrics.TabGap;
      if (gap < 0) gap = 0;

      var activeKey = node.ActiveKey;
      if (string.IsNullOrWhiteSpace(activeKey)) activeKey = items[0].PersistKey;

      var hasActive = !string.IsNullOrWhiteSpace(activeKey);

      var wAvail = tabStripBounds.Width - (count - 1) * gap;
      if (wAvail < 0) wAvail = 0;

      var wEach = (count > 0) ? (wAvail / count) : 0;

      var minW = _Metrics.TabMinWidth;
      var maxW = _Metrics.TabMaxWidth;

      if (wEach < minW) wEach = minW;
      if (wEach > maxW) wEach = maxW;
      if (wEach < 1) wEach = 1;

      var stripRight = tabStripBounds.Right;
      var tabY = tabStripBounds.Y;
      var tabH = tabStripBounds.Height;

      var closeSize = _Metrics.TabCloseButtonSize;
      var closePadR = _Metrics.TabCloseButtonPaddingRight;
      if (closePadR < 0) closePadR = 0;

      var tx = tabStripBounds.X;

      for (int i = 0; i < count; i++)
      {
        if (tx >= stripRight) break;

        var tabW = wEach;
        var right = tx + tabW;
        if (right > stripRight) tabW = stripRight - tx;
        if (tabW <= 0) break;

        var tabBounds = new Rectangle(tx, tabY, tabW, tabH);

        Rectangle closeBounds = Rectangle.Empty;
        if (closeSize > 0)
        {
          var closeX = tabBounds.Right - closePadR - closeSize;
          if (closeX >= tabBounds.X + 2)
          {
            var closeY = tabBounds.Y + (tabBounds.Height - closeSize) / 2;
            closeBounds = new Rectangle(closeX, closeY, closeSize, closeSize);
          }
        }

        var key = items[i].PersistKey;
        var isActive = hasActive && string.Equals(activeKey, key, StringComparison.Ordinal);

        tree.AddTab(groupIndex, key, tabBounds, closeBounds, isActive);

        tx += wEach + gap;
      }
    }

    // AutoHide (DockAutoHideNode) ================================================================================

    private void BuildAutoHideNode(DockAutoHideNode node, Rectangle bounds, DockVisualTree tree)
    {
      if (bounds.IsEmpty) return;

      var nt = node.GetType();

      var sideValue =
        TryGetPropertyValue(nt, node, "Side")
        ?? TryGetPropertyValue(nt, node, "Edge")
        ?? TryGetPropertyValue(nt, node, "DockEdge")
        ?? TryGetPropertyValue(nt, node, "Position");

      if (!TryMapAutoHideSideToDockEdge(sideValue, out var edge))
        edge = DockVisualTree.DockEdge.Left;

      var thickness = GetMetricsInt("AutoHideStripThickness", _Metrics.TabStripHeight);
      if (thickness < 1) thickness = Math.Max(1, _Metrics.TabStripHeight);

      var stripBounds = ComputeEdgeBounds(bounds, edge, thickness);
      if (!stripBounds.IsEmpty)
      {
        var activeKey =
          TryGetStringProperty(nt, node, "ActiveKey")
          ?? TryGetStringProperty(nt, node, "SelectedKey");

        activeKey = NormalizeKey(activeKey);

        IEnumerable? items = null;
        try { items = node.Items; } catch { items = null; }

        var tabExtent = GetMetricsInt("AutoHideTabExtent", Math.Max(48, _Metrics.TabMinWidth));
        if (tabExtent < 8) tabExtent = 8;

        var gap = GetMetricsInt("AutoHideTabGap", _Metrics.TabGap);
        if (gap < 0) gap = 0;

        var pad = GetMetricsInt("AutoHideStripPadding", 0);
        if (pad < 0) pad = 0;

        if (items is not null && HasRenderableAutoHideTab(items, edge, stripBounds, tabExtent, pad))
        {
          var stripIndex = tree.AddAutoHideStrip(edge, stripBounds);
          var builtCount = BuildAutoHideTabs(tree, stripIndex, edge, stripBounds, items, activeKey, tabExtent, gap, pad);

          // 탭이 실제로 하나도 그려지지 않았으면 strip 두께를 소비하지 않는다.
          if (builtCount <= 0) thickness = 0;
        }
        else
        {
          thickness = 0;
        }
      }

      var inner = thickness > 0 ? ShrinkBoundsByEdge(bounds, edge, thickness) : bounds;
      if (inner.IsEmpty) return;

      var child =
        TryGetDockNodeProperty(nt, node, "Root")
        ?? TryGetDockNodeProperty(nt, node, "Docked")
        ?? TryGetDockNodeProperty(nt, node, "DockedRoot")
        ?? TryGetDockNodeProperty(nt, node, "Node")
        ?? TryGetDockNodeProperty(nt, node, "Child")
        ?? TryGetDockNodeProperty(nt, node, "Content");

      if (child is null || ReferenceEquals(child, node))
      {
        tree.AddEmptyRegion(inner);
        return;
      }

      VisitNode(child, inner, tree);
    }

    private static Rectangle ShrinkBoundsByEdge(Rectangle bounds, DockVisualTree.DockEdge edge, int thickness)
    {
      if (bounds.IsEmpty) return Rectangle.Empty;
      if (thickness < 0) thickness = 0;

      switch (edge)
      {
        case DockVisualTree.DockEdge.Left:
          {
            var d = Math.Min(thickness, bounds.Width);
            return new Rectangle(bounds.X + d, bounds.Y, bounds.Width - d, bounds.Height);
          }
        case DockVisualTree.DockEdge.Right:
          {
            var d = Math.Min(thickness, bounds.Width);
            return new Rectangle(bounds.X, bounds.Y, bounds.Width - d, bounds.Height);
          }
        case DockVisualTree.DockEdge.Top:
          {
            var d = Math.Min(thickness, bounds.Height);
            return new Rectangle(bounds.X, bounds.Y + d, bounds.Width, bounds.Height - d);
          }
        case DockVisualTree.DockEdge.Bottom:
          {
            var d = Math.Min(thickness, bounds.Height);
            return new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height - d);
          }
        default:
          return bounds;
      }
    }

    private static bool TryMapAutoHideSideToDockEdge(object? value, out DockVisualTree.DockEdge edge)
    {
      edge = DockVisualTree.DockEdge.Left;
      if (value is null) return false;

      if (value is DockVisualTree.DockEdge de)
      {
        edge = de;
        return true;
      }

      var t = value.GetType();

      if (t.IsEnum)
      {
        var name = Enum.GetName(t, value);
        if (!string.IsNullOrWhiteSpace(name))
        {
          if (name.Contains("Left", StringComparison.OrdinalIgnoreCase)) { edge = DockVisualTree.DockEdge.Left; return true; }
          if (name.Contains("Right", StringComparison.OrdinalIgnoreCase)) { edge = DockVisualTree.DockEdge.Right; return true; }
          if (name.Contains("Top", StringComparison.OrdinalIgnoreCase)) { edge = DockVisualTree.DockEdge.Top; return true; }
          if (name.Contains("Bottom", StringComparison.OrdinalIgnoreCase)) { edge = DockVisualTree.DockEdge.Bottom; return true; }

          if (Enum.TryParse(name, ignoreCase: true, out DockVisualTree.DockEdge parsed))
          {
            edge = parsed;
            return true;
          }
        }

        return false;
      }

      return TryMapDockEdge(value, out edge);
    }

    // AutoHide (Fallback) ========================================================================================

    private bool TryBuildAutoHideFallback(DockNode node, Rectangle bounds, DockVisualTree tree)
    {
      var t = node.GetType();
      if (!t.Name.Contains("AutoHide", StringComparison.OrdinalIgnoreCase)) return false;

      var strips =
        TryGetEnumerableProperty(t, node, "Strips")
        ?? TryGetEnumerableProperty(t, node, "AutoHideStrips")
        ?? TryGetEnumerableProperty(t, node, "DockStrips");

      if (strips is null) return false;

      var anyBuilt = false;

      var thickness = GetMetricsInt("AutoHideStripThickness", _Metrics.TabStripHeight);
      if (thickness < 1) thickness = Math.Max(1, _Metrics.TabStripHeight);

      var tabExtent = GetMetricsInt("AutoHideTabExtent", Math.Max(48, _Metrics.TabMinWidth));
      if (tabExtent < 8) tabExtent = 8;

      var gap = GetMetricsInt("AutoHideTabGap", _Metrics.TabGap);
      if (gap < 0) gap = 0;

      var pad = GetMetricsInt("AutoHideStripPadding", 0);
      if (pad < 0) pad = 0;

      var globalActiveKey =
        TryGetStringProperty(t, node, "ActiveKey")
        ?? TryGetStringProperty(t, node, "SelectedKey");

      globalActiveKey = NormalizeKey(globalActiveKey);

      foreach (var stripObj in strips)
      {
        if (stripObj is null) continue;

        var st = stripObj.GetType();

        var edgeValue =
          TryGetPropertyValue(st, stripObj, "Edge")
          ?? TryGetPropertyValue(st, stripObj, "DockEdge")
          ?? TryGetPropertyValue(st, stripObj, "Side")
          ?? TryGetPropertyValue(st, stripObj, "Position");

        if (!TryMapDockEdge(edgeValue, out var edge)) continue;

        var items =
          TryGetEnumerableProperty(st, stripObj, "Items")
          ?? TryGetEnumerableProperty(st, stripObj, "Tabs")
          ?? TryGetEnumerableProperty(st, stripObj, "Contents");

        if (items is null) continue;

        var stripBounds = ComputeEdgeBounds(bounds, edge, thickness);
        if (stripBounds.IsEmpty) continue;
        if (!HasRenderableAutoHideTab(items, edge, stripBounds, tabExtent, pad)) continue;

        var stripIndex = tree.AddAutoHideStrip(edge, stripBounds);

        var activeKey =
          TryGetStringProperty(st, stripObj, "ActiveKey")
          ?? TryGetStringProperty(st, stripObj, "SelectedKey")
          ?? globalActiveKey;

        activeKey = NormalizeKey(activeKey);

        var builtCount = BuildAutoHideTabs(tree, stripIndex, edge, stripBounds, items, activeKey, tabExtent, gap, pad);
        anyBuilt |= builtCount > 0;
      }

      return anyBuilt;
    }

    private int BuildAutoHideTabs(
      DockVisualTree tree,
      int stripIndex,
      DockVisualTree.DockEdge edge,
      Rectangle stripBounds,
      IEnumerable items,
      string? activeKey,
      int tabExtent,
      int gap,
      int pad)
    {
      var isHorizontal = edge is DockVisualTree.DockEdge.Top or DockVisualTree.DockEdge.Bottom;
      activeKey = NormalizeKey(activeKey);

      var minExtent = GetMetricsInt("AutoHideTabMinExtent", 28);
      if (minExtent < 8) minExtent = 8;

      var maxExtent = GetMetricsInt("AutoHideTabMaxExtent", 120);
      if (maxExtent < minExtent) maxExtent = minExtent;

      tabExtent = MathEx.Clamp(tabExtent, minExtent, maxExtent);

      var built = 0;

      if (isHorizontal)
      {
        var x = stripBounds.X + pad;
        var limit = stripBounds.Right - pad;

        foreach (var item in items)
        {
          if (!TryGetPersistKeyAndPopupSize(item, out var key, out var popupSize)) continue;

          if (x + tabExtent > limit) break;

          var tabBounds = new Rectangle(x, stripBounds.Y, tabExtent, stripBounds.Height);
          var isActive = !string.IsNullOrEmpty(activeKey) && string.Equals(activeKey, key, StringComparison.Ordinal);

          // ContentKey는 string(그리기/히트테스트 유지), PopupSize는 별도 전달(메타 캐시 목적)
          tree.AddAutoHideTab(stripIndex, key, tabBounds, isActive, popupSize);
          built++;
          x += tabExtent + gap;
        }

        return built;
      }

      var y = stripBounds.Y + pad;
      var yLimit = stripBounds.Bottom - pad;

      foreach (var item in items)
      {
        if (!TryGetPersistKeyAndPopupSize(item, out var key, out var popupSize)) continue;

        if (y + tabExtent > yLimit) break;

        var tabBounds = new Rectangle(stripBounds.X, y, stripBounds.Width, tabExtent);
        var isActive = !string.IsNullOrEmpty(activeKey) && string.Equals(activeKey, key, StringComparison.Ordinal);

        tree.AddAutoHideTab(stripIndex, key, tabBounds, isActive, popupSize);
        built++;

        y += tabExtent + gap;
      }

      return built;
    }


    private static bool HasRenderableAutoHideTab(IEnumerable items, DockVisualTree.DockEdge edge, Rectangle stripBounds, int tabExtent, int pad)
    {
      if (items is null) return false;

      var isHorizontal = edge is DockVisualTree.DockEdge.Top or DockVisualTree.DockEdge.Bottom;
      var usable = isHorizontal
        ? Math.Max(0, stripBounds.Width - (pad * 2))
        : Math.Max(0, stripBounds.Height - (pad * 2));

      // 세로 AutoHide는 split 폭이 얇기 때문에 tabExtent(가로 탭 기준값)으로 막으면
      // 탭이 생성되지 않아 제목이 '-'처럼 보이거나 strip만 남을 수 있다.
      var minRequired = isHorizontal ? Math.Max(8, tabExtent) : 28;
      if (usable < minRequired) return false;

      foreach (var item in items)
      {
        if (TryGetPersistKeyAndPopupSize(item, out _, out _))
          return true;
      }

      return false;
    }

    private static Rectangle ComputeEdgeBounds(Rectangle bounds, DockVisualTree.DockEdge edge, int thickness)
    {
      if (bounds.IsEmpty) return Rectangle.Empty;
      if (thickness < 1) thickness = 1;

      return edge switch
      {
        DockVisualTree.DockEdge.Left => new Rectangle(bounds.X, bounds.Y, Math.Min(thickness, bounds.Width), bounds.Height),
        DockVisualTree.DockEdge.Right => new Rectangle(bounds.Right - Math.Min(thickness, bounds.Width), bounds.Y, Math.Min(thickness, bounds.Width), bounds.Height),
        DockVisualTree.DockEdge.Top => new Rectangle(bounds.X, bounds.Y, bounds.Width, Math.Min(thickness, bounds.Height)),
        DockVisualTree.DockEdge.Bottom => new Rectangle(bounds.X, bounds.Bottom - Math.Min(thickness, bounds.Height), bounds.Width, Math.Min(thickness, bounds.Height)),
        _ => Rectangle.Empty
      };
    }

    private int GetMetricsInt(string propertyName, int fallback)
    {
      if (_Metrics is null) return fallback;

      var t = _Metrics.GetType();
      var p = t.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      if (p is null || !p.CanRead) return fallback;

      try
      {
        var v = p.GetValue(_Metrics);
        if (v is null) return fallback;

        return v switch
        {
          int i => i,
          float f => (int)Math.Round(f),
          double d => (int)Math.Round(d),
          _ => Convert.ToInt32(v)
        };
      }
      catch
      {
        return fallback;
      }
    }

    private static IEnumerable? TryGetEnumerableProperty(Type t, object instance, string propertyName)
    {
      var p = t.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      if (p is null || !p.CanRead) return null;

      try
      {
        return p.GetValue(instance) as IEnumerable;
      }
      catch
      {
        return null;
      }
    }

    private static object? TryGetPropertyValue(Type t, object instance, string propertyName)
    {
      var p = t.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      if (p is null || !p.CanRead) return null;

      try
      {
        return p.GetValue(instance);
      }
      catch
      {
        return null;
      }
    }

    private static string? TryGetStringProperty(Type t, object instance, string propertyName)
    {
      var p = t.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      if (p is null || !p.CanRead) return null;

      try
      {
        return p.GetValue(instance) as string;
      }
      catch
      {
        return null;
      }
    }

    private static bool TryMapDockEdge(object? value, out DockVisualTree.DockEdge edge)
    {
      edge = DockVisualTree.DockEdge.Left;

      if (value is null) return false;

      if (value is DockVisualTree.DockEdge e)
      {
        edge = e;
        return true;
      }

      var t = value.GetType();

      if (t.IsEnum)
      {
        var name = Enum.GetName(t, value);
        if (!string.IsNullOrWhiteSpace(name) && Enum.TryParse(name, ignoreCase: true, out DockVisualTree.DockEdge parsed))
        {
          edge = parsed;
          return true;
        }

        try
        {
          var i = Convert.ToInt32(value);
          if ((uint)i <= 3u)
          {
            edge = (DockVisualTree.DockEdge)i;
            return true;
          }
        }
        catch { }

        return false;
      }

      if (value is int iv && (uint)iv <= 3u)
      {
        edge = (DockVisualTree.DockEdge)iv;
        return true;
      }

      return false;
    }

    // AutoHide Item Helpers ======================================================================================

    private static string? NormalizeKey(string? s)
    {
      if (string.IsNullOrWhiteSpace(s)) return null;
      var t = s.Trim();
      return t.Length == 0 ? null : t;
    }

    private static bool TryGetPersistKeyAndPopupSize(object? item, out string key, out Size? popupSize)
    {
      key = string.Empty;
      popupSize = null;

      if (item is null) return false;

      if (item is string s)
      {
        var nk = NormalizeKey(s);
        if (nk is null) return false;

        key = nk;
        return true;
      }

      var t = item.GetType();

      var pk =
        TryGetStringProperty(t, item, "PersistKey")
        ?? TryGetStringProperty(t, item, "Key")
        ?? TryGetStringProperty(t, item, "Id")
        ?? TryGetStringProperty(t, item, "Name");

      var nk2 = NormalizeKey(pk);
      if (nk2 is null) return false;

      key = nk2;

      popupSize =
        TryGetNullableSizeMember(t, item, "PopupSize")
        ?? TryGetNullableSizeMember(t, item, "Size");

      return true;
    }

    private static Size? TryGetNullableSizeMember(Type t, object instance, string name)
    {
      const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

      try
      {
        var p = t.GetProperty(name, Flags);
        if (p is not null && p.CanRead)
          return NormalizeSizeValue(p.GetValue(instance));

        var f = t.GetField(name, Flags);
        if (f is not null)
          return NormalizeSizeValue(f.GetValue(instance));
      }
      catch { }

      return null;
    }

    private static Size? NormalizeSizeValue(object? v)
    {
      if (v is null) return null;
      if (v is Size s) return s;
      if (v is Rectangle r) return r.Size;
      return null;
    }

    // Unknown Node Fallback =======================================================================

    private bool TryVisitChildNodeFallback(DockNode node, Rectangle bounds, DockVisualTree tree)
    {
      var t = node.GetType();
      var name = t.Name;

      var looksLikeSpecial =
        name.Contains("AutoHide", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Floating", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Float", StringComparison.OrdinalIgnoreCase);

      if (!looksLikeSpecial) return false;

      var child =
        TryGetDockNodeProperty(t, node, "Root")
        ?? TryGetDockNodeProperty(t, node, "Docked")
        ?? TryGetDockNodeProperty(t, node, "Node")
        ?? TryGetDockNodeProperty(t, node, "Child")
        ?? TryGetDockNodeProperty(t, node, "Content");

      if (child is null) return false;
      if (ReferenceEquals(child, node)) return false;

      VisitNode(child, bounds, tree);
      return true;
    }

    private DockNode? TryGetDockNodeProperty(Type t, DockNode instance, string propertyName)
    {
      var p = t.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      if (p is null) return null;
      if (!typeof(DockNode).IsAssignableFrom(p.PropertyType)) return null;
      if (!p.CanRead) return null;

      try
      {
        return (DockNode?)p.GetValue(instance);
      }
      catch
      {
        return null;
      }
    }
  }
}

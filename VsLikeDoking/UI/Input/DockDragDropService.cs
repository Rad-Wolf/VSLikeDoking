// VsLikeDocking - VsLikeDoking - UI/Input/DockDragDropService.cs - DockDragDropService - (File)

using System;
using System.Drawing;
using System.Windows.Forms;

using VsLikeDoking.UI.Visual;

namespace VsLikeDoking.UI.Input
{
  /// <summary>탭 드래그 도킹 서비스(입력 상태 머신)</summary>
  /// <remarks>
  /// - VisualTree + HitTest 결과를 이용해 드래그/드랍 의도를 해석한다.
  /// - 레이아웃 변경은 하지 않고, 요청 이벤트만 발생시킨다.
  /// </remarks>
  public sealed class DockDragDropService
  {
    // Types =====================================================================================

    public enum DragState : byte { None = 0, Candidate, Dragging }

    public enum DropKind : byte { None = 0, InsertTab/*탭스트립/탭 영역에 삽입*/, DockToZone/*콘텐츠 영역에 도킹 존(L,R,T,B,C)*/ }

    public enum DockZone : byte { Center = 0, Left = 1, Right = 2, Top = 3, Bottom = 4 }

    public readonly struct DragInfo
    {
      public int SourceGroupIndex { get; }
      public int SourceTabIndex { get; } // VisualTree Tabs index (global)
      public object? SourceContentKey { get; }
      public Point DownPoint { get; }
      public Point CurrentPoint { get; }

      public DragInfo(int sourceGroupIndex, int sourceTabIndex, object? sourceContentKey, Point downPoint, Point currentPoint)
      {
        SourceGroupIndex = sourceGroupIndex;
        SourceTabIndex = sourceTabIndex;
        SourceContentKey = sourceContentKey;
        DownPoint = downPoint;
        CurrentPoint = currentPoint;
      }
    }

    public readonly struct DropInfo
    {
      public DropKind Kind { get; }

      // InsertTab :
      public int TargetGroupIndex { get; }
      public int InsertIndex { get; }

      // DockToZone :
      public DockZone Zone { get; }

      // 공통 : 계산 기준 영역(주로 Group의 TabStrip/Content bounds)
      public Rectangle TargetBounds { get; }

      public bool IsValid => Kind != DropKind.None;

      private DropInfo(DropKind kind, int targetGroupIndex, int insertIndex, DockZone zone, Rectangle targetBounds)
      {
        Kind = kind;
        TargetGroupIndex = targetGroupIndex;
        InsertIndex = insertIndex;
        Zone = zone;
        TargetBounds = targetBounds;
      }

      public static DropInfo None()
        => new(DropKind.None, -1, -1, DockZone.Center, Rectangle.Empty);

      public static DropInfo InsertTab(int targetGroupIndex, int insertIndex, Rectangle tabStripBounds)
        => new(DropKind.InsertTab, targetGroupIndex, insertIndex, DockZone.Center, tabStripBounds);

      public static DropInfo DockToZone(int targetGroupIndex, DockZone zone, Rectangle contentBounds)
        => new(DropKind.DockToZone, targetGroupIndex, -1, zone, contentBounds);
    }

    // Fields =====================================================================================

    private Control? _Surface;
    private DockVisualTree? _Tree;

    private DragState _State;

    private Point _DownPoint;
    private int _SourceGroupIndex;
    private int _SourceTabIndex;
    private object? _SourceContentKey;

    private DropInfo _LastDrop;

    // Properties =================================================================================

    /// <summary>입력 대상 Surface</summary>
    public Control? Surface
      => _Surface;

    /// <summary>현재 VisualTree(히트테스트/드랍 계산에 사용)</summary>
    public DockVisualTree? VisualTree
    {
      get { return _Tree; }
      set { _Tree = value; }
    }

    /// <summary>현재 드래그 상태</summary>
    public DragState State => _State;

    /// <summary>드래그 시작 판정(픽셀). 기본값은 OS DragSize 기반</summary>
    public Size DragSize { get; set; } = SystemInformation.DragSize;

    /// <summary>DockToZone 판정 시, 가장자리 존 비율 (0 ~ 0.5)</summary>
    public float ZoneRatio { get; set; } = 0.25f;

    // Events (Requests) ==========================================================================

    /// <summary>드래그가 시작되었다.</summary>
    public event Action<DragInfo>? DragBegun;

    /// <summary>드래그 중 드랍 타겟(프리뷰)이 갱신되었다.</summary>
    public event Action<DragInfo, DropInfo>? DragUpdated;

    /// <summary>드래그가 커밋되었다(마우스업)</summary>
    public event Action<DragInfo, DropInfo>? DragCommitted;

    /// <summary>드래그가 취소되었다(캡쳐 해제/포커스 손실/Esc)</summary>
    public event Action<DragInfo>? DragCanceled;

    /// <summary>드래그 프리뷰 등으로 화면 갱신이 필요할 때</summary>
    public event Action? VisualStateChanged;

    // Public API =================================================================================

    /// <summary>Surface(Control)에 드래그 서비스를 연결한다.</summary>
    public void Attach(Control surface)
    {
      if (surface is null) throw new ArgumentNullException(nameof(surface));
      if (ReferenceEquals(_Surface, surface)) return;

      Detach();

      _Surface = surface;

      surface.MouseDown += OnMouseDown;
      surface.MouseMove += OnMouseMove;
      surface.MouseUp += OnMouseUp;
      surface.MouseCaptureChanged += OnMouseCaptureChanged;
      surface.LostFocus += OnLostFocus;
      surface.KeyDown += OnKeyDown;
    }

    /// <summary>Surface(Control)에서 드래그 서비스를 해제한다.</summary>
    public void Detach()
    {
      if (_Surface is null) return;

      // 중요 : Surface를 null로 만들기 전에 Cancel로 캡쳐를 풀고 상태를 정리한다.
      Cancel();

      var s = _Surface;

      s.MouseDown -= OnMouseDown;
      s.MouseMove -= OnMouseMove;
      s.MouseUp -= OnMouseUp;
      s.MouseCaptureChanged -= OnMouseCaptureChanged;
      s.LostFocus -= OnLostFocus;
      s.KeyDown -= OnKeyDown;

      _Surface = null;
      Reset();
    }

    /// <summary>상태를 초기화한다.(드래그/후보 해제)</summary>
    public void Reset()
    {
      _State = DragState.None;

      _DownPoint = Point.Empty;
      _SourceGroupIndex = -1;
      _SourceTabIndex = -1;
      _SourceContentKey = null;

      _LastDrop = DropInfo.None();
    }

    // Input Handlers ==============================================================================

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
      if (_Surface is null) return;
      if (e.Button != MouseButtons.Left) return;
      if (_Tree is null) return;

      if (_State == DragState.Dragging) return;

      var hit = DockHitTest.HitTest(_Tree, e.Location);
      if (hit.Kind != DockVisualTree.RegionKind.Tab) return;

      _State = DragState.Candidate;
      _DownPoint = e.Location;

      _SourceGroupIndex = hit.GroupIndex;
      _SourceTabIndex = hit.TabIndex;

      _SourceContentKey = GetContentKeySafe(_Tree, _SourceTabIndex);

      _LastDrop = DropInfo.None();
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
      if (_Surface is null) return;
      if (_Tree is null) return;

      if (_State == DragState.None) return;

      if (_State == DragState.Candidate)
      {
        if (!IsDragThresholdExceeded(_DownPoint, e.Location)) return;

        _State = DragState.Dragging;
        _Surface.Capture = true;

        var info = CreateDragInfo(e.Location);
        DragBegun?.Invoke(info);

        UpdateDropPreview(e.Location, info);
        return;
      }

      if (_State == DragState.Dragging)
      {
        var info = CreateDragInfo(e.Location);
        UpdateDropPreview(e.Location, info);
      }
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
      if (_Surface is null) return;
      if (e.Button != MouseButtons.Left) return;

      if (_State == DragState.Dragging)
      {
        Commit(e.Location);
        return;
      }

      Reset();
      _Surface.Capture = false;
    }

    private void OnMouseCaptureChanged(object? sender, EventArgs e)
    {
      if (_Surface is null)
      {
        Reset();
        return;
      }

      if (_State == DragState.Dragging)
      {
        if (Control.MouseButtons == MouseButtons.None)
        {
          var pt = _Surface.PointToClient(Cursor.Position);
          Commit(pt);
          return;
        }

        Cancel();
        return;
      }

      if (_State == DragState.Candidate)
      {
        Reset();
        return;
      }
    }

    private void OnLostFocus(object? sender, EventArgs e)
    {
      if (_State == DragState.Dragging) Cancel();
      else if (_State == DragState.Candidate) Reset();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
      if (e.KeyCode == Keys.Escape && _State == DragState.Dragging)
      {
        Cancel();
        e.Handled = true;
      }
    }

    // Commit / Cancel / Preview ===================================================================

    private void Commit(Point pt)
    {
      if (_Surface is null)
      {
        Reset();
        return;
      }

      if (_State != DragState.Dragging)
      {
        Reset();
        _Surface.Capture = false;
        return;
      }

      var info = CreateDragInfo(pt);
      var drop = ComputeDrop(pt);

      DragCommitted?.Invoke(info, drop);

      _LastDrop = DropInfo.None();
      VisualStateChanged?.Invoke();

      Reset();
      _Surface.Capture = false;
    }

    private void Cancel()
    {
      if (_Surface is null)
      {
        Reset();
        return;
      }

      if (_State == DragState.Dragging)
      {
        var pt = _Surface.PointToClient(Cursor.Position);
        var info = CreateDragInfo(pt);

        DragCanceled?.Invoke(info);

        _LastDrop = DropInfo.None();
        VisualStateChanged?.Invoke();
      }

      Reset();
      _Surface.Capture = false;
    }

    private void UpdateDropPreview(Point pt, DragInfo info)
    {
      var drop = ComputeDrop(pt);

      // 같은 결과면 이벤트 스팸 방지 (TargetBounds도 비교해야 프리뷰가 리사이즈/재레이아웃을 따라간다)
      if (IsSameDrop(_LastDrop, drop)) return;

      _LastDrop = drop;

      DragUpdated?.Invoke(info, drop);
      VisualStateChanged?.Invoke();
    }

    // Drop Compute =================================================================================

    private DropInfo ComputeDrop(Point pt)
    {
      if (_Tree is null) return DropInfo.None();

      var hit = DockHitTest.HitTest(_Tree, pt);

      if (hit.Kind is DockVisualTree.RegionKind.TabStrip or DockVisualTree.RegionKind.Tab or DockVisualTree.RegionKind.TabClose)
      {
        var gIndex = hit.GroupIndex;
        if ((uint)gIndex >= (uint)_Tree.Groups.Count) return DropInfo.None();

        var g = _Tree.Groups[gIndex];
        var strip = g.TabStripBounds;
        if (strip.IsEmpty) return DropInfo.None();

        var insertIndex = ComputeInsertIndex(gIndex, strip, pt);
        return DropInfo.InsertTab(gIndex, insertIndex, strip);
      }

      if (hit.Kind is DockVisualTree.RegionKind.Content or DockVisualTree.RegionKind.Caption or DockVisualTree.RegionKind.CaptionClose)
      {
        var gIndex = hit.GroupIndex;
        if ((uint)gIndex >= (uint)_Tree.Groups.Count) return DropInfo.None();

        var g = _Tree.Groups[gIndex];
        var content = g.ContentBounds;
        if (content.IsEmpty) return DropInfo.None();

        var zone = ComputeZone(content, pt);
        return DropInfo.DockToZone(gIndex, zone, content);
      }

      return DropInfo.None();
    }

    private int ComputeInsertIndex(int targetGroupIndex, Rectangle strip, Point pt)
    {
      if (_Tree is null) return -1;
      if ((uint)targetGroupIndex >= (uint)_Tree.Groups.Count) return -1;

      var g = _Tree.Groups[targetGroupIndex];

      if (g.TabCount <= 0) return 0;

      var start = g.TabStart;
      var end = start + g.TabCount;

      if (pt.X <= strip.X) return 0;
      if (pt.X >= strip.Right) return g.TabCount;

      for (int i = start; i < end; i++)
      {
        var t = _Tree.Tabs[i].Bounds;

        var mid = t.X + (t.Width / 2);
        if (pt.X < mid) return i - start;
      }

      return g.TabCount;
    }

    private DockZone ComputeZone(Rectangle content, Point pt)
    {
      var zr = ZoneRatio;
      if (zr < 0.05f) zr = 0.05f;
      if (zr > 0.45f) zr = 0.45f;

      var leftW = (int)Math.Floor(content.Width * zr);
      var topH = (int)Math.Floor(content.Height * zr);

      var x = pt.X;
      var y = pt.Y;

      if (x < content.X + leftW) return DockZone.Left;
      if (x > content.Right - leftW) return DockZone.Right;

      if (y < content.Y + topH) return DockZone.Top;
      if (y > content.Bottom - topH) return DockZone.Bottom;

      return DockZone.Center;
    }

    private static bool IsSameDrop(DropInfo a, DropInfo b)
    {
      if (a.Kind != b.Kind) return false;
      if (a.TargetGroupIndex != b.TargetGroupIndex) return false;
      if (a.InsertIndex != b.InsertIndex) return false;
      if (a.Zone != b.Zone) return false;

      // 리사이즈/레이아웃 재계산 시 bounds가 바뀌면 프리뷰 갱신이 필요하다.
      if (a.TargetBounds != b.TargetBounds) return false;

      return true;
    }

    // Utils ======================================================================================

    private bool IsDragThresholdExceeded(Point a, Point b)
    {
      var dx = Math.Abs(b.X - a.X);
      var dy = Math.Abs(b.Y - a.Y);

      var tx = Math.Max(1, DragSize.Width / 2);
      var ty = Math.Max(1, DragSize.Height / 2);

      return dx >= tx || dy >= ty;
    }

    private static object? GetContentKeySafe(DockVisualTree tree, int tabIndex)
    {
      if ((uint)tabIndex >= (uint)tree.Tabs.Count) return null;
      return tree.Tabs[tabIndex].ContentKey;
    }

    private DragInfo CreateDragInfo(Point current)
      => new(_SourceGroupIndex, _SourceTabIndex, _SourceContentKey, _DownPoint, current);
  }
}

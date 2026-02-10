using System;
using System.Drawing;

namespace VsLikeDoking.UI.Input
{
  /// <summary>탭 드래그 세션(후보/드래그 상태 + 소스 정보 + 포인터 좌표)을 보관한다.</summary>
  public sealed class DockDragSession
  {
    // Types ====================================================================

    /// <summary>드래그 세션 상태</summary>
    public enum DockDragSessionState : byte { None = 0, Candidate = 1, Dragging = 2 }

    // Fields ====================================================================

    private DockDragSessionState _State;

    private int _SourceGroupIndex;
    private int _SourceTabIndex;
    private object? _SourceContentKey;

    private Point _DownPoint;
    private Point _CurrentPoint;

    // Properties ================================================================

    /// <summary>현재 세션 상태</summary>
    public DockDragSessionState State => _State;

    /// <summary>후보 또는 드래그 중인지 여부</summary>
    public bool IsActive => _State != DockDragSessionState.None;

    /// <summary>실제 드래그 중인지 여부</summary>
    public bool IsDragging => _State == DockDragSessionState.Dragging;

    /// <summary>소스 그룹 인덱스(없으면 -1)</summary>
    public int SourceGroupIndex => _SourceGroupIndex;

    /// <summary>소스 탭 인덱스(VisualTree의 tab index. 없으면 -1)</summary>
    public int SourceTabIndex => _SourceTabIndex;

    /// <summary>소스 콘텐츠 키(없을 수 있음)</summary>
    public object? SourceContentKey => _SourceContentKey;

    /// <summary>마우스 다운 좌표(세션 시작 점)</summary>
    public Point DownPoint => _DownPoint;

    /// <summary>현재 포인터 좌표</summary>
    public Point CurrentPoint => _CurrentPoint;

    // Ctor ======================================================================

    /// <summary>DockDragSession을 생성한다.</summary>
    public DockDragSession()
    {
      Reset();
    }

    // Public ====================================================================

    /// <summary>세션을 기본값으로 만든다.</summary>
    public void Reset()
    {
      _State = DockDragSessionState.None;

      _SourceGroupIndex = -1;
      _SourceTabIndex = -1;
      _SourceContentKey = null;

      _DownPoint = Point.Empty;
      _CurrentPoint = Point.Empty;
    }

    /// <summary>탭 드래그 후보로 진입한다(MouseDown 시점).</summary>
    public void BeginCandidate(int sourceGroupIndex, int sourceTabIndex, object? sourceContentKey, Point downPoint)
    {
      _State = DockDragSessionState.Candidate;

      _SourceGroupIndex = sourceGroupIndex >= 0 ? sourceGroupIndex : -1;
      _SourceTabIndex = sourceTabIndex >= 0 ? sourceTabIndex : -1;
      _SourceContentKey = sourceContentKey;

      _DownPoint = downPoint;
      _CurrentPoint = downPoint;
    }

    /// <summary>현재 포인터 좌표를 갱신한다.</summary>
    public void UpdatePointer(Point currentPoint)
    {
      if (_State == DockDragSessionState.None) return;
      _CurrentPoint = currentPoint;
    }

    /// <summary>후보 상태에서, 드래그 임계치를 넘으면 드래그 상태로 전환한다.</summary>
    /// <returns>이번 호출로 Dragging으로 전환되었으면 true</returns>
    public bool TryStartDragging(Point currentPoint, Size dragSize)
    {
      if (_State != DockDragSessionState.Candidate) return false;

      _CurrentPoint = currentPoint;

      if (!IsDragThresholdExceeded(_DownPoint, currentPoint, dragSize)) return false;

      _State = DockDragSessionState.Dragging;
      return true;
    }

    /// <summary>세션을 취소한다.(캡쳐 해제/포커스 손실/ESC 등)</summary>
    public void Cancel()
    {
      Reset();
    }

    /// <summary>드레그를 종료한다(커밋 후 호출)</summary>
    public void End()
    {
      Reset();
    }

    // Internals =================================================================

    private static bool IsDragThresholdExceeded(Point a, Point b, Size dragSize)
    {
      var dx = Math.Abs(b.X - a.X);
      var dy = Math.Abs(b.Y - a.Y);

      var tx = Math.Max(1, dragSize.Width / 2);
      var ty = Math.Max(1, dragSize.Height / 2);

      return dx >= tx || dy >= ty;
    }
  }
}

using System;
using System.Drawing;
using System.Windows.Forms;

using VsLikeDoking.UI.Visual;

using DockHitTestResult = VsLikeDoking.UI.Visual.DockHitTest.DockHitTestResult;

namespace VsLikeDoking.UI.Input
{
  public sealed partial class DockInputRouter
  {
    // Input Handlers ==============================================================================

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
      if (_Surface is null) return;

      // 스플리터 후보/드래그 중에는 해당 흐름을 우선한다.
      if (_SplitterDrag.IsCandidate)
      {
        if (_SplitterDrag.IsDragging)
        {
          if (_SplitterDrag.TryUpdate(e.Location, out var ratio))
            RaiseRequest(DockInputRequest.SplitterDrag(_SplitterDrag.SplitIndex, ratio, DockSplitterDragPhase.Update));
          return;
        }

        if (_LeftDown && _SplitterDrag.TryStart(e.Location, out float beginRatio))
        {
          _Surface.Capture = true;
          RaiseRequest(DockInputRequest.SplitterDrag(_SplitterDrag.SplitIndex, beginRatio, DockSplitterDragPhase.Begin));

          // 시작 프레임에 1회 Update 시도(움직임이 거의 없으면 false일 수 있음)
          if (_SplitterDrag.TryUpdate(e.Location, out var ratio))
            RaiseRequest(DockInputRequest.SplitterDrag(_SplitterDrag.SplitIndex, ratio, DockSplitterDragPhase.Update));

          return;
        }

        // 후보만 유지 중이던 hover 업데이트만 수행
        UpdateHover(e.Location);
        return;
      }

      // 일반 클릭도 “임계치 초과 이동”이면 클릭을 억제한다(탭 드래그 등과 충돌 방지)
      if (_LeftDown && !_SuppressClick && IsDragThresholdExceeded(_DownPoint, e.Location, DragSize))
        _SuppressClick = true;

      UpdateHover(e.Location);
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
      if (_Surface is null) return;
      if (e.Button != MouseButtons.Left) return;

      _LeftDown = true;
      _DownPoint = e.Location;
      _SuppressClick = false;

      var hit = Hit(e.Location);
      SetPressed(hit);

      // 스플리터는 "클릭 즉시"가 아니라, 드래그 임계치 초과 시 Begin
      if (hit.Kind == DockVisualTree.RegionKind.Splitter && _Tree is not null)
      {
        // Splitter 후보 진입
        if (_SplitterDrag.BeginCandidate(_Tree, hit, e.Location))
        {
          _Surface.Capture = true;
          return;
        }
      }
      // 이 외는 일반 클릭 흐름(캡처 없음)
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
      if (_Surface is null) return;
      if (e.Button != MouseButtons.Left) return;

      // Splitter drag 중이면 End
      if (_SplitterDrag.IsDragging)
      {
        if (_SplitterDrag.End(out var endRatio))
          RaiseRequest(DockInputRequest.SplitterDrag(_SplitterDrag.SplitIndex, endRatio, DockSplitterDragPhase.End));

        _Surface.Capture = false;
        _LeftDown = false;

        _SuppressClick = false;

        SetPressed(DockHitTestResult.None());
        UpdateHover(e.Location);
        return;
      }

      // 후보만 있었고 드래그 시작 안 했으면 후보 해제(요청 없음)
      if (_SplitterDrag.IsCandidate) _SplitterDrag.Cancel(out _);

      // 드래그로 판단된 경우(임계치 초과 이동) 클릭 요청은 올리지 않는다.
      if (!_SuppressClick)
      {
        // 클릭 판정 : down 때 눌렀던 대상과 up 때 대상이 같아야 클릭으로 본다.
        var up = Hit(e.Location);

        if (IsSameTarget(_Pressed, up))
          _ = RaiseClickRequest(_Pressed);
      }

      _LeftDown = false;
      _Surface.Capture = false;

      _SuppressClick = false;

      SetPressed(DockHitTestResult.None());
      UpdateHover(e.Location);
    }

    private void OnMouseLeave(object? sender, EventArgs e)
    {
      SetHover(DockHitTestResult.None());
    }

    private void OnMouseCaptureChanged(object? sender, EventArgs e)
    {
      // 드래그 중 캡쳐가 풀리면 취소로 본다.
      if (_SplitterDrag.IsCandidate)
        CancelSplitter(true);

      _SuppressClick = false;
    }

    private void OnLostFocus(object? sender, EventArgs e)
    {
      if (_SplitterDrag.IsCandidate)
        CancelSplitter(true);

      _SuppressClick = false;
      SetHover(DockHitTestResult.None());
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
      HandleKeyDown(e.KeyData);
    }

    // Key Handling ================================================================================

    private void HandleKeyDown(Keys keyData)
    {
      if (keyData != Keys.Escape) return;

      if (_SplitterDrag.IsCandidate)
        CancelSplitter(true);

      RaiseRequest(DockInputRequest.DismissAutoHidePopup());
    }

    // Hit / State =================================================================================

    private DockHitTestResult Hit(Point point)
    {
      if (_Tree is null) return DockHitTestResult.None();
      return DockHitTest.HitTest(_Tree, point);
    }

    private void UpdateHover(Point point)
    {
      var hit = Hit(point);
      SetHover(hit);
    }

    private void SetHover(DockHitTestResult hit)
    {
      if (IsSameTarget(_Hover, hit)) return;
      _Hover = hit;
      VisualStateChanged?.Invoke();
    }

    private void SetPressed(DockHitTestResult hit)
    {
      if (IsSameTarget(_Pressed, hit)) return;
      _Pressed = hit;
      VisualStateChanged?.Invoke();
    }

    private static bool IsSameTarget(DockHitTestResult a, DockHitTestResult b)
    {
      if (a.Kind != b.Kind) return false;
      if (a.PrimaryIndex != b.PrimaryIndex) return false;
      if (a.SecondaryIndex != b.SecondaryIndex) return false;
      return true;
    }

    private void ResetPointerStates()
    {
      _LeftDown = false;
      _DownPoint = Point.Empty;

      _SuppressClick = false;

      _Hover = DockHitTestResult.None();
      _Pressed = DockHitTestResult.None();

      VisualStateChanged?.Invoke();
    }

    // Click Requests ============================================================================

    private bool RaiseClickRequest(DockHitTestResult pressed)
    {
      switch (pressed.Kind)
      {
        case DockVisualTree.RegionKind.Tab:
          RaiseRequest(DockInputRequest.ActivateTab(pressed.GroupIndex, pressed.TabIndex));
          return true;

        case DockVisualTree.RegionKind.TabClose:
          RaiseRequest(DockInputRequest.CloseTab(pressed.GroupIndex, pressed.TabIndex));
          return true;

        case DockVisualTree.RegionKind.CaptionClose:
          RaiseRequest(DockInputRequest.CloseGroup(pressed.GroupIndex));
          return true;

        case DockVisualTree.RegionKind.AutoHideTab:
          RaiseRequest(DockInputRequest.ActivateAutoHideTab(pressed.AutoHideStripIndex, pressed.AutoHideTabIndex));
          return true;

        default:
          return false;
      }
    }

    private void RaiseRequest(DockInputRequest request)
    {
      RequestRaised?.Invoke(request);
    }

    // Splitter Cancel ============================================================================

    private void CancelSplitter(bool raiseCancelRequest)
    {
      if (!_SplitterDrag.IsCandidate) return;

      var splitIndex = _SplitterDrag.IsDragging ? _SplitterDrag.SplitIndex : _Pressed.SplitIndex;

      if (_SplitterDrag.Cancel(out var ratio))
      {
        // 사용자 입력으로 드래그/후보가 강제로 끊긴 경우에만 Cancel 요청을 올린다.
        if (raiseCancelRequest && splitIndex >= 0)
          RaiseRequest(DockInputRequest.SplitterDrag(splitIndex, ratio, DockSplitterDragPhase.Cancel));
      }

      if (_Surface is not null) _Surface.Capture = false;
      VisualStateChanged?.Invoke();
    }

    // Utils ======================================================================================

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
// VsLikeDocking - VsLikeDoking - UI/Input/DockInputRouter.cs - DockInputRouter - (File)

using System;
using System.Drawing;
using System.Windows.Forms;

using VsLikeDoking.UI.Visual;
using VsLikeDoking.Utils;

using DockHitTestResult = VsLikeDoking.UI.Visual.DockHitTest.DockHitTestResult;

namespace VsLikeDoking.UI.Input
{
  /// <summary>단일 Surface 입력 라우터(히트테스트 + 클릭/드래그 상태 머신)</summary>
  /// <remarks>UI는 입력을 해석해 “요청”만 발생시키고, 실제 레이아웃 변경은 Core/Layout로 위임한다.</remarks>
  public sealed class DockInputRouter
  {
    // Fields =====================================================================================

    private const float DefaultAutoHidePopupRatio = 0.35f;
    private const int MinAutoHidePopupSize = 120;

    private Control? _Surface;
    private DockVisualTree? _Tree;

    private DockHitTestResult _Hover;
    private DockHitTestResult _Pressed;

    private bool _LeftDown;
    private Point _DownPoint;

    private bool _SuppressClick;

    private readonly DockSplitterDrag _SplitterDrag = new();

    // (PATCH) 외부(자식 View) 클릭이 "팝업 안"인지 판별하기 위한 가정 상태
    private int _AssumedAutoHideStripIndex = -1;
    private int _AssumedAutoHideTabIndex = -1;
    private Rectangle _AssumedAutoHidePopupBounds = Rectangle.Empty;
    private bool _AssumedAutoHidePopupBoundsDirty = true;

    // Properties =================================================================================

    /// <summary>히트테스트에 사용할 VisualTree</summary>
    public DockVisualTree? VisualTree
    {
      get { return _Tree; }
      set
      {
        _Tree = value;
        _AssumedAutoHidePopupBoundsDirty = true;
      }
    }

    /// <summary>현재 hover 결과</summary>
    public DockHitTestResult Hover => _Hover;

    /// <summary>현재 pressed 결과(왼쪽 버튼 기준)</summary>
    public DockHitTestResult Pressed => _Pressed;

    /// <summary>스플리터 드래깅 여부</summary>
    public bool IsSplitterDragging => _SplitterDrag.IsDragging;

    /// <summary>드래그 시작 판정(픽셀). 기본값은 OS DragSize 기반</summary>
    public Size DragSize
    {
      get { return _SplitterDrag.DragSize; }
      set { _SplitterDrag.DragSize = value; }
    }

    /// <summary>스플리터 ratio 최소값(드래깅 중 clamp)</summary>
    public float MinSplitterRatio
    {
      get { return _SplitterDrag.MinRatio; }
      set { _SplitterDrag.MinRatio = value; }
    }

    /// <summary>스플리터 ratio 최대값(드래깅 중 clamp)</summary>
    public float MaxSplitterRatio
    {
      get { return _SplitterDrag.MaxRatio; }
      set { _SplitterDrag.MaxRatio = value; }
    }

    /// <summary>ratio 변경 이벤트를 너무 자주 내기 않기 위한 최소 변화량</summary>
    public float RatioEpsilon
    {
      get { return _SplitterDrag.RatioEpsilon; }
      set { _SplitterDrag.RatioEpsilon = value; }
    }

    // Events =====================================================================================

    /// <summary>입력 결과로 '요청'이 발생했을 때</summary>
    public event Action<DockInputRequest>? RequestRaised;

    /// <summary>hover/pressed 상태가 바뀌어 다시 그려야 할 때</summary>
    public event Action? VisualStateChanged;

    // Ctor =======================================================================================

    public DockInputRouter()
    {
      _Hover = DockHitTestResult.None();
      _Pressed = DockHitTestResult.None();
    }

    // Attach / Detach =============================================================================

    /// <summary>Surface(Control)에 입력 라우터를 연결한다.</summary>
    public void Attach(Control surface)
    {
      Guard.NotNull(surface);
      if (ReferenceEquals(_Surface, surface)) return;

      Detach();
      _Surface = surface;

      surface.MouseMove += OnMouseMove;
      surface.MouseDown += OnMouseDown;
      surface.MouseUp += OnMouseUp;
      surface.MouseLeave += OnMouseLeave;
      surface.MouseCaptureChanged += OnMouseCaptureChanged;
      surface.LostFocus += OnLostFocus;
      surface.KeyDown += OnKeyDown;
    }

    /// <summary>Surface(Control)에서 입력 라우터를 해제한다</summary>
    public void Detach()
    {
      if (_Surface is null) return;

      var s = _Surface;

      s.MouseMove -= OnMouseMove;
      s.MouseDown -= OnMouseDown;
      s.MouseUp -= OnMouseUp;
      s.MouseLeave -= OnMouseLeave;
      s.MouseCaptureChanged -= OnMouseCaptureChanged;
      s.LostFocus -= OnLostFocus;
      s.KeyDown -= OnKeyDown;

      _Surface = null;

      CancelSplitter(false);
      ResetPointerStates();

      ClearAssumedAutoHidePopup();
    }

    // Public API =================================================================================

    /// <summary>현재 hover/pressed/drag 상태를 초기화한다.</summary>
    public void Reset()
    {
      CancelSplitter(false);
      ResetPointerStates();
      ClearAssumedAutoHidePopup();
    }

    /// <summary>
    /// Surface 위의 "자식 컨트롤(컨텐츠 View)"에서 발생한 클릭을 라우터로 포워딩한다.
    /// AutoHide 팝업의 "바깥 클릭"에 의해 Hide 되어야 하는 케이스를 처리하기 위한 용도다.
    /// </summary>
    public void NotifyExternalMouseDown()
    {
      if (_Surface is null) return;

      // (PATCH) 팝업 가정 정보가 있을 때만 "팝업 안/밖"을 판별한다.
      // - 팝업 안이면 dismiss 금지
      // - 팝업 밖이면 dismiss
      if (TryGetAssumedAutoHidePopupBounds(out var popup) && !popup.IsEmpty)
      {
        Point p;
        try { p = _Surface.PointToClient(Control.MousePosition); }
        catch { p = Point.Empty; }

        if (popup.Contains(p)) return;

        RaiseRequest(DockInputRequest.DismissAutoHidePopup());
        ClearAssumedAutoHidePopup();
        return;
      }

      // 가정 정보가 없으면 여기서 임의 dismiss를 올리지 않는다(오판 닫힘 방지).
    }

    /// <summary>
    /// Surface 바깥(또는 자식 View)에서 발생한 KeyDown을 라우터로 포워딩한다.
    /// 주로 ESC로 AutoHide 팝업을 닫기 위한 용도다.
    /// </summary>
    public void NotifyExternalKeyDown(Keys keyData)
    {
      HandleKeyDown(keyData);
    }

    // Requests ===================================================================================

    /// <summary>입력 요청 종류</summary>
    public enum DockInputRequestKind : byte
    {
      None = 0,
      ActivateTab,
      CloseTab,
      CloseGroup,
      SplitterDrag,

      // AutoHide
      ActivateAutoHideTab,
      DismissAutoHidePopup,
    }

    /// <summary>스플리터 드래그 단계</summary>
    public enum DockSplitterDragPhase : byte { Begin = 0, Update = 1, End = 2, Cancel = 3, }

    /// <summary>입력 해석 결과로 발생하는 요청</summary>
    public readonly struct DockInputRequest
    {
      /// <summary>요청 종류</summary>
      public DockInputRequestKind Kind { get; }

      /// <summary>[Tab/Group] 그룹 인덱스, [AutoHide] Strip 인덱스</summary>
      public int GroupIndex { get; }

      /// <summary>[Tab] 탭 인덱스(global), [AutoHide] AutoHideTab 인덱스</summary>
      public int TabIndex { get; }

      /// <summary>스플릿 인덱스(스플리터 드래그)</summary>
      public int SplitIndex { get; }

      /// <summary>스플리터 ratio(스플리터 드래그)</summary>
      public float Ratio { get; }

      /// <summary>스플리터 드래그 단계</summary>
      public DockSplitterDragPhase Phase { get; }

      /// <summary>AutoHide Strip 인덱스(ActivateAutoHideTab일 때만)</summary>
      public int AutoHideStripIndex
        => Kind == DockInputRequestKind.ActivateAutoHideTab ? GroupIndex : -1;

      /// <summary>AutoHide Tab 인덱스(ActivateAutoHideTab일 때만)</summary>
      public int AutoHideTabIndex
        => Kind == DockInputRequestKind.ActivateAutoHideTab ? TabIndex : -1;

      public DockInputRequest(DockInputRequestKind kind, int groupIndex, int tabIndex, int splitIndex, float ratio, DockSplitterDragPhase phase)
      {
        Kind = kind;
        GroupIndex = groupIndex;
        TabIndex = tabIndex;
        SplitIndex = splitIndex;
        Ratio = ratio;
        Phase = phase;
      }

      /// <summary>탭 활성화 요청</summary>
      public static DockInputRequest ActivateTab(int groupIndex, int tabIndex)
        => new(DockInputRequestKind.ActivateTab, groupIndex, tabIndex, -1, 0.0f, DockSplitterDragPhase.Update);

      /// <summary>탭 닫기 요청</summary>
      public static DockInputRequest CloseTab(int groupIndex, int tabIndex)
        => new(DockInputRequestKind.CloseTab, groupIndex, tabIndex, -1, 0.0f, DockSplitterDragPhase.Update);

      /// <summary>그룹(캡션) 닫기 요청</summary>
      public static DockInputRequest CloseGroup(int groupIndex)
        => new(DockInputRequestKind.CloseGroup, groupIndex, -1, -1, 0.0f, DockSplitterDragPhase.Update);

      /// <summary>스플리터 드래그 요청</summary>
      public static DockInputRequest SplitterDrag(int splitIndex, float ratio, DockSplitterDragPhase phase)
        => new(DockInputRequestKind.SplitterDrag, -1, -1, splitIndex, ratio, phase);

      /// <summary>AutoHide 탭 활성화 요청</summary>
      public static DockInputRequest ActivateAutoHideTab(int stripIndex, int autoHideTabIndex)
        => new(DockInputRequestKind.ActivateAutoHideTab, stripIndex, autoHideTabIndex, -1, 0.0f, DockSplitterDragPhase.Update);

      /// <summary>AutoHide 팝업 숨김 요청(바깥 클릭/포커스 아웃 등)</summary>
      public static DockInputRequest DismissAutoHidePopup()
        => new(DockInputRequestKind.DismissAutoHidePopup, -1, -1, -1, 0.0f, DockSplitterDragPhase.Update);
    }

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

      TryFocusSurface();

      _LeftDown = true;
      _DownPoint = e.Location;
      _SuppressClick = false;

      var hit = Hit(e.Location);
      SetPressed(hit);

      // AutoHide 탭을 누른 게 아니면 "바깥 클릭"로 간주하고 Hide 요청을 올린다.
      // (실제 Hide 여부는 Host에서 DockManager 상태를 보고 판단)
      if (hit.Kind != DockVisualTree.RegionKind.AutoHideTab)
      {
        RaiseRequest(DockInputRequest.DismissAutoHidePopup());
        ClearAssumedAutoHidePopup();
      }

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
        if (IsSameTarget(_Pressed, up)) RaiseClickRequest(_Pressed);
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

      // 포커스가 나가면 hover도 지우는 편이 안전
      SetHover(DockHitTestResult.None());

      // 자식 컨트롤로 포커스가 이동한 경우(= Surface 내부 포커스 유지)는 닫지 않는다.
      // (AutoHide 팝업 컨텐츠 클릭 시 즉시 닫히는 버그 방지)
      if (_Surface is not null && _Surface.ContainsFocus) return;

      // Surface 밖으로 포커스가 나가면 AutoHide 팝업은 닫는 쪽이 안전
      RaiseRequest(DockInputRequest.DismissAutoHidePopup());
      ClearAssumedAutoHidePopup();
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
      ClearAssumedAutoHidePopup();
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

    private void RaiseClickRequest(DockHitTestResult pressed)
    {
      switch (pressed.Kind)
      {
        case DockVisualTree.RegionKind.Tab:
          RaiseRequest(DockInputRequest.ActivateTab(pressed.GroupIndex, pressed.TabIndex));
          return;

        case DockVisualTree.RegionKind.TabClose:
          RaiseRequest(DockInputRequest.CloseTab(pressed.GroupIndex, pressed.TabIndex));
          return;

        case DockVisualTree.RegionKind.CaptionClose:
          RaiseRequest(DockInputRequest.CloseGroup(pressed.GroupIndex));
          return;

        case DockVisualTree.RegionKind.AutoHideTab:
          RememberAssumedAutoHidePopup(pressed.AutoHideStripIndex, pressed.AutoHideTabIndex);
          RaiseRequest(DockInputRequest.ActivateAutoHideTab(pressed.AutoHideStripIndex, pressed.AutoHideTabIndex));
          return;

        default:
          return;
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

    // AutoHide Popup (Assumed) ===================================================================

    private void RememberAssumedAutoHidePopup(int stripIndex, int tabIndex)
    {
      _AssumedAutoHideStripIndex = stripIndex;
      _AssumedAutoHideTabIndex = tabIndex;
      _AssumedAutoHidePopupBoundsDirty = true;
    }

    private void ClearAssumedAutoHidePopup()
    {
      _AssumedAutoHideStripIndex = -1;
      _AssumedAutoHideTabIndex = -1;
      _AssumedAutoHidePopupBounds = Rectangle.Empty;
      _AssumedAutoHidePopupBoundsDirty = true;
    }

    private bool TryGetAssumedAutoHidePopupBounds(out Rectangle popup)
    {
      popup = Rectangle.Empty;

      if (_Surface is null) return false;
      if (_Tree is null) return false;

      if (_AssumedAutoHideStripIndex < 0) return false;
      if ((uint)_AssumedAutoHideStripIndex >= (uint)_Tree.AutoHideStrips.Count) return false;

      if (_AssumedAutoHidePopupBoundsDirty)
      {
        _AssumedAutoHidePopupBoundsDirty = false;

        var strip = _Tree.AutoHideStrips[_AssumedAutoHideStripIndex];
        if (strip.Bounds.IsEmpty)
        {
          _AssumedAutoHidePopupBounds = Rectangle.Empty;
        }
        else
        {
          _AssumedAutoHidePopupBounds = ComputeAutoHidePopupBounds(_Surface.ClientRectangle, strip.Edge, strip.Bounds, DefaultAutoHidePopupRatio);
        }
      }

      popup = _AssumedAutoHidePopupBounds;
      return true;
    }

    private static Rectangle ComputeAutoHidePopupBounds(Rectangle client, DockVisualTree.DockEdge edge, Rectangle stripBounds, float ratio)
    {
      if (client.Width <= 0 || client.Height <= 0) return Rectangle.Empty;

      if (ratio < 0.15f) ratio = 0.15f;
      if (ratio > 0.60f) ratio = 0.60f;

      var desiredW = Math.Max(MinAutoHidePopupSize, (int)Math.Round(client.Width * ratio));
      var desiredH = Math.Max(MinAutoHidePopupSize, (int)Math.Round(client.Height * ratio));

      switch (edge)
      {
        case DockVisualTree.DockEdge.Left:
          {
            var x = stripBounds.Right;
            var w = Math.Min(desiredW, client.Right - x);
            if (w <= 0) return Rectangle.Empty;
            return new Rectangle(x, client.Y, w, client.Height);
          }

        case DockVisualTree.DockEdge.Right:
          {
            var avail = stripBounds.X - client.X;
            var w = Math.Min(desiredW, avail);
            if (w <= 0) return Rectangle.Empty;
            var x = stripBounds.X - w;
            return new Rectangle(x, client.Y, w, client.Height);
          }

        case DockVisualTree.DockEdge.Top:
          {
            var y = stripBounds.Bottom;
            var h = Math.Min(desiredH, client.Bottom - y);
            if (h <= 0) return Rectangle.Empty;
            return new Rectangle(client.X, y, client.Width, h);
          }

        case DockVisualTree.DockEdge.Bottom:
          {
            var avail = stripBounds.Y - client.Y;
            var h = Math.Min(desiredH, avail);
            if (h <= 0) return Rectangle.Empty;
            var y = stripBounds.Y - h;
            return new Rectangle(client.X, y, client.Width, h);
          }

        default:
          return Rectangle.Empty;
      }
    }

    // Utils ======================================================================================

    private void TryFocusSurface()
    {
      if (_Surface is null) return;

      try
      {
        if (_Surface.CanFocus && !_Surface.Focused)
          _Surface.Focus();
      }
      catch
      {
        // ignore
      }
    }

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

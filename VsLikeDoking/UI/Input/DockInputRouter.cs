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
  public sealed partial class DockInputRouter
  {
    // Fields =====================================================================================

    private Control? _Surface;
    private DockVisualTree? _Tree;

    private DockHitTestResult _Hover;
    private DockHitTestResult _Pressed;

    private bool _LeftDown;
    private Point _DownPoint;

    private bool _SuppressClick;

    private readonly DockSplitterDrag _SplitterDrag = new();

    // Properties =================================================================================

    /// <summary>히트테스트에 사용할 VisualTree</summary>
    public DockVisualTree? VisualTree
    {
      get { return _Tree; }
      set { _Tree = value; }
    }

    /// <summary>현재 hover 결과</summary>
    public DockHitTestResult Hover => _Hover;

    /// <summary>현재 pressed 결과(왼쪽 버튼 기준)</summary>
    public DockHitTestResult Pressed => _Pressed;

    /// <summary>현재 왼쪽 버튼 down 상태</summary>
    public bool IsLeftButtonDown => _LeftDown;

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
      s.KeyDown -= OnKeyDown;

      _Surface = null;

      CancelSplitter(false);
      ResetPointerStates();
    }

    // Public API =================================================================================

    /// <summary>현재 hover/pressed/drag 상태를 초기화한다.</summary>
    public void Reset()
    {
      CancelSplitter(false);
      ResetPointerStates();
    }

    /// <summary>
    /// Surface 위의 "자식 컨트롤(컨텐츠 View)"에서 발생한 클릭을 라우터로 포워딩한다.
    /// AutoHide 팝업의 "바깥 클릭"에 의해 Hide 되어야 하는 케이스를 처리하기 위한 용도다.
    /// </summary>
    public void NotifyExternalMouseDown()
    {
      RaiseRequest(DockInputRequest.DismissAutoHidePopup());
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

  }
}

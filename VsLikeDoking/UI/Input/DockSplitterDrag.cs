// VsLikeDocking - VsLikeDoking - UI/Input/DockSplitterDrag.cs - DockSplitterDrag - (File)

using System;
using System.Drawing;
using System.Windows.Forms;

using VsLikeDoking.UI.Visual;
using VsLikeDoking.Utils;

using DockHitTestResult = VsLikeDoking.UI.Visual.DockHitTest.DockHitTestResult;

namespace VsLikeDoking.UI.Input
{
  /// <summary>스플리터 드래그 상태 머신(후보 &gt; 시작 &gt; 업데이트 &gt; 종료/취소)과 ratio 계산을 담당</summary>
  /// <remarks>
  /// - 실제 Layout 변경은 이 클래스에서 하지 않는다.
  /// - UI(InputRouter)는 이 클래스로부터 ratio를 받아 Core/Layout에 '요청'만 한다.
  /// </remarks>
  public sealed class DockSplitterDrag
  {
    // Fields ====================================================================

    private bool _IsCandidate;
    private bool _IsDragging;
    private int _SplitIndex;
    private Point _DownPoint;
    private DockVisualTree.SplitAxis _Axis;
    private Rectangle _Bounds;
    private int _Thickness;
    private int _Avail;
    private float _StartRatio;
    private float _LastRatio;

    // Properties ================================================================

    /// <summary>드래그 후보 상태인지 여부(MouseDown이 Splitter였던 상태)</summary>
    public bool IsCandidate => _IsCandidate;

    /// <summary>실제 드래그 중인지 여부</summary>
    public bool IsDragging => _IsDragging;

    /// <summary>최근 설정된 Split index(후보/드래그/종료 직후에도 유지, Reset/Cancel 시 -1)</summary>
    public int SplitIndex => _SplitIndex;

    /// <summary>드래그 시작 시 Ratio</summary>
    public float StartRatio => _StartRatio;

    /// <summary>마지막 계산 ratio</summary>
    public float LastRatio => _LastRatio;

    /// <summary>드래그 시작 판정(픽셀). 기본값은 OS DragSize 기반</summary>
    public Size DragSize { get; set; } = SystemInformation.DragSize;

    /// <summary>ratio 최소값(드래그 중 clamp)</summary>
    public float MinRatio { get; set; } = 0.05f;

    /// <summary>ratio 최대값(드래그 중 clamp)</summary>
    public float MaxRatio { get; set; } = 0.95f;

    /// <summary>Update 시 너무 잦은 요청을 막기 위한 최소 변화량</summary>
    public float RatioEpsilon { get; set; } = 0.0005f;

    // Ctor ======================================================================

    /// <summary>DockSplitterDrag 인스턴스를 생성한다.</summary>
    public DockSplitterDrag()
    {
      Reset();
    }

    // Public API ================================================================

    /// <summary>모든 상태를 기본으로 되돌린다.</summary>
    public void Reset()
    {
      _IsCandidate = false;
      _IsDragging = false;

      _SplitIndex = -1;
      _DownPoint = Point.Empty;

      _Axis = DockVisualTree.SplitAxis.Horizontal;
      _Bounds = Rectangle.Empty;

      _Thickness = 0;
      _Avail = 0;

      _StartRatio = 0.0f;
      _LastRatio = 0.0f;
    }

    /// <summary>MouseDown 시점에서 스플리터 후보로 진입한다.</summary>
    /// <param name="tree">현재 VisualTree</param>
    /// <param name="pressed">MouseDown 시점의 HitTest 결과(Kind=Splitter여야 함)</param>
    /// <param name="downPoint">MouseDown 좌표</param>
    /// <returns>후보 진입 성공 여부</returns>
    public bool BeginCandidate(DockVisualTree tree, DockHitTestResult pressed, Point downPoint)
    {
      if (tree is null) throw new ArgumentNullException(nameof(tree));

      Reset();

      if (pressed.Kind != DockVisualTree.RegionKind.Splitter) return false;

      var splitIndex = pressed.SplitIndex;
      if ((uint)splitIndex >= (uint)tree.Splits.Count) return false;

      var split = tree.Splits[splitIndex];

      // 드래그 중 VisualTree가 다시 계산되더라도, 계산 기준은 드래그 시작 시점의 bounds를 고정한다.
      _IsCandidate = true;
      _SplitIndex = splitIndex;
      _DownPoint = downPoint;
      _Axis = split.Axis;
      _Bounds = split.Bounds;

      _StartRatio = MathEx.Clamp(MathEx.ClampPer(split.Ratio), MinRatio, MaxRatio);
      _LastRatio = _StartRatio;

      // thickness/avail은 ratio 계산의 핵심 값. 미리 계산해둔다.
      _Thickness = (_Axis == DockVisualTree.SplitAxis.Vertical) ? split.SplitterBounds.Width : split.SplitterBounds.Height;
      if (_Thickness < 1) _Thickness = 1;

      _Avail = (_Axis == DockVisualTree.SplitAxis.Vertical) ? (_Bounds.Width - _Thickness) : (_Bounds.Height - _Thickness);
      if (_Avail < 0) _Avail = 0;

      // 유효 영역이 아니면 후보 취소
      if (_Bounds.IsEmpty || _Avail <= 1)
      {
        Reset();
        return false;
      }

      return true;
    }

    /// <summary>MouseMove에서 드래그 임계치를 넘었는지 판단하고, 넘었다면 드래그를 시작한다.</summary>
    /// <param name="currentPoint">현재 마우스 좌표</param>
    /// <param name="beginRatio">드래그 시작 시 ratio</param>
    /// <returns>이번 호출로 드래그가 시작되었는지 여부</returns>
    public bool TryStart(Point currentPoint, out float beginRatio)
    {
      beginRatio = 0.0f;

      if (!_IsCandidate) return false;
      if (_IsDragging) return false;

      if (!IsDragThresholdExceeded(_DownPoint, currentPoint)) return false;

      _IsDragging = true;
      beginRatio = _StartRatio;
      return true;
    }

    /// <summary>드래그 중 ratio를 업데이트한다.</summary>
    /// <param name="currentPoint">현재 마우스 좌표</param>
    /// <param name="ratio">계산된 ratio</param>
    /// <returns>ratio가 의미 있게 변했는지 여부(이 true일 때만 요청/적용 권장)</returns>
    public bool TryUpdate(Point currentPoint, out float ratio)
    {
      ratio = _LastRatio;

      if (!_IsDragging) return false;

      var r = ComputeRatio(currentPoint);

      // 스팸 방지 : 너무 작은 변화는 무시
      if (Math.Abs(r - _LastRatio) < RatioEpsilon) return false;

      _LastRatio = r;
      ratio = r;
      return true;
    }

    /// <summary>드래그 종료</summary>
    /// <param name="endRatio">종료 시점 ratio(마지막 ratio)</param>
    /// <returns>종료 성공 여부(드래그 중이 아니면 false)</returns>
    public bool End(out float endRatio)
    {
      endRatio = _LastRatio;
      if (!_IsDragging) return false;

      _IsCandidate = false;
      _IsDragging = false;

      return true;
    }

    /// <summary>드래그를 취소한다.(캡처 해제/포커스 손실 등)</summary>
    /// <param name="cancelRatio">취소 시점 ratio(마지막 ratio)</param>
    /// <returns>취소 성공 여부(후보/드래그 상태가 아니면 false)</returns>
    public bool Cancel(out float cancelRatio)
    {
      cancelRatio = _LastRatio;

      if (!_IsCandidate && !_IsDragging) return false;

      // 취소는 상태만 초기화 함(복구는 상위에서 결정)
      Reset();
      return true;
    }

    // Internals ==================================================================

    private float ComputeRatio(Point pt)
    {
      // LayoutEngine의 분할 로직과 동일한 기준을 사용한다.
      // avail = boundsSize - thickness
      // firstSize ~= mousePos - thickness/2 (스플리터 중심 기준)
      if (_Avail <= 0) return MathEx.Clamp(_LastRatio, MinRatio, MaxRatio);

      var pos = (_Axis == DockVisualTree.SplitAxis.Vertical) ? (pt.X - _Bounds.X) : (pt.Y - _Bounds.Y);
      var firstSize = pos - (_Thickness / 2.0f);

      var ratio = firstSize / _Avail;
      ratio = MathEx.Clamp(MathEx.ClampPer(ratio), MinRatio, MaxRatio);

      return ratio;
    }

    private bool IsDragThresholdExceeded(Point a, Point b)
    {
      // DragSize는 OS 설정 기반(드래그 시작 임계). 중심 기준 반영으로 본다.
      var dx = Math.Abs(b.X - a.X);
      var dy = Math.Abs(b.Y - a.Y);

      var tx = Math.Max(1, DragSize.Width / 2);
      var ty = Math.Max(1, DragSize.Height / 2);

      return dx >= tx || dy >= ty;
    }
  }
}

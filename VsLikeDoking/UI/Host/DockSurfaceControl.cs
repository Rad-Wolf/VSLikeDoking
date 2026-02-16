// VsLikeDocking - VsLikeDoking - UI/Host/DockSurfaceControl.cs - DockSurfaceControl - (File)

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

using VsLikeDoking.Abstractions;
using VsLikeDoking.Core;
using VsLikeDoking.Layout.Model;
using VsLikeDoking.Layout.Nodes;
using VsLikeDoking.Rendering;
using VsLikeDoking.Rendering.Theme;
using VsLikeDoking.UI.Content;
using VsLikeDoking.UI.Input;
using VsLikeDoking.UI.Visual;
using VsLikeDoking.Utils;

namespace VsLikeDoking.UI.Host
{
  /// <summary>단일 Surface: VisualTree 계산 + 입력(HitTest/상태) + 렌더 호출 + 컨텐츠 배치</summary>
  /// <remarks>DockHostControl이 리플렉션으로 주입하는 Manager/Renderer/Root를 받아 최소 컨트롤로 동작한다.</remarks>
  public sealed class DockSurfaceControl : Control
  {
    // Fields =====================================================================================

    private bool _InvalidateQueued;
    private bool _VisualDirty;

    private DockAutoHideSide _AutoHidePopupSideCache;
    private string? _AutoHidePopupSideCacheKey;

    private bool _MetricsAppliedFromRenderer;

    private bool _ApplyingSplitDrag;

    private bool _PostPresentRepairQueued;

    private int _SplitDragSplitIndex;
    private float _SplitDragOriginalRatio;
    private bool _SplitDragHasBackup;

    private DockManager? _Manager;
    private VsDockRenderer? _Renderer;
    private DockNode? _Root;

    private readonly DockVisualTree _Tree;
    private readonly DockLayoutEngine _LayoutEngin;

    private readonly DockInputRouter _InputRouter;
    private readonly DockContentPresenter _Presenter;

    private readonly DockDragDropService _TabDragDrop;
    private bool _HasTabDropPreview;
    private DockDragDropService.DropInfo _TabDropPreview;

    private DockPreviewOverlayForm? _Overlay;
    private OverlayPreviewMode _OvMode;
    private bool _OvVisible;
    private Rectangle _OvBoundsScreen;
    private Point _OvLineP0;
    private Point _OvLineP1;

    private bool _AutoHideActivating;
    private DateTime _AutoHideActivationHoldUntilUtc;

    // AutoHide Popup Host ========================================================================

    private Panel? _AutoHidePopupHost;
    private string? _AutoHidePopupKey;

    // (PATCH) 팝업 View가 Presenter/다른 컨테이너에 의해 "뺏기는" 상황 방지용 캐시
    private Control? _AutoHidePopupView;

    // (PATCH) PersistKey별 AutoHide 팝업 크기 캐시(UI 레벨에서만 유지)
    private readonly Dictionary<string, Size> _AutoHidePopupSizeCache = new(StringComparer.Ordinal);

    // (PATCH) AutoHide 팝업 리사이즈 그립(팝업이 열렸을 때만 표시)
    private Panel? _AutoHideResizeGrip;
    private bool _AutoHideResizeDragging;
    private Point _AutoHideResizeDownScreen;
    private Size _AutoHideResizeStartSize;
    private DockAutoHideSide _AutoHideResizeSide;
    private string? _AutoHideResizeKey;

    // Content MouseDown Forwarding (AutoHide Dismiss) =============================================

    private readonly HashSet<Control> _ForwardedMouseDownHooks = new();

    // Properties ==================================================================================

    /// <summary>도킹 매니저(레이아웃/컨텐츠 운영)</summary>
    public DockManager? Manager
    {
      get { return _Manager; }
      set
      {
        if (ReferenceEquals(_Manager, value)) return;

        if (IsHandleCreated && InvokeRequired)
        {
          TryBeginInvoke(() => Manager = value, true);
          return;
        }

        DetachManagerEvents(_Manager);
        _Manager = value;
        AttachManagerEvents(_Manager);

        if (_Manager is not null)
        {
          _Presenter.Bind(this, _Manager);
          if (_Manager.Root is not null) _Root = _Manager.Root;
        }
        else
        {
          _Presenter.Unbind();
          _Root = null;

          // 매니저가 사라지면 AutoHide 팝업도 즉시 정리
          HideAutoHidePopupHost(removeView: true);
        }

        MarkVisualDirtyAndRender();
      }
    }

    /// <summary>스타일 렌더러</summary>
    public VsDockRenderer? Renderer
    {
      get { return _Renderer; }
      set
      {
        if (ReferenceEquals(_Renderer, value)) return;

        if (IsHandleCreated && InvokeRequired)
        {
          TryBeginInvoke(() => Renderer = value, true);
          return;
        }

        _Renderer = value;

        // (PATCH) Renderer.Metrics가 늦게 유효해질 수 있으니 적용 여부 플래그로 관리
        _MetricsAppliedFromRenderer = false;

        if (_Renderer is not null)
          TryApplyRendererMetricsToLayoutEngine(_Renderer);

        UpdatePreviewResources();
        MarkVisualDirtyAndRender();
      }
    }

    /// <summary>현재 루트 레이아웃 노드</summary>
    public DockNode? Root
    {
      get { return _Root; }
      set
      {
        if (IsHandleCreated && InvokeRequired)
        {
          TryBeginInvoke(() => Root = value, true);
          return;
        }

        _Root = value;
        MarkVisualDirtyAndRender();
      }
    }

    /// <summary>현재 계산된 VisualTree(그리기/히트테스트용 Rect 캐시)</summary>
    public DockVisualTree VisualTree => _Tree;

    // Ctor ========================================================================================

    /// <summary>DockSurfaceControl 생성</summary>
    public DockSurfaceControl()
    {
      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
      TabStop = true;

      _Tree = new DockVisualTree();
      _LayoutEngin = new DockLayoutEngine(DockMetrics.CreateVsLike());
      _Presenter = new DockContentPresenter();

      _InputRouter = new DockInputRouter();
      _InputRouter.Attach(this);
      _InputRouter.VisualTree = _Tree;
      _InputRouter.VisualStateChanged += OnVisualStateChanged;
      _InputRouter.RequestRaised += OnInputRequest;

      MouseUp += OnSurfaceMouseUp;

      _TabDragDrop = new DockDragDropService();
      _TabDragDrop.Attach(this);
      _TabDragDrop.VisualTree = _Tree;
      _TabDragDrop.DragBegun += OnTabDragBegun;
      _TabDragDrop.DragUpdated += OnTabDragUpdated;
      _TabDragDrop.DragCommitted += OnTabDragCommitted;
      _TabDragDrop.DragCanceled += OnTabDragCanceled;
      _TabDragDrop.VisualStateChanged += OnTabDragVisualStateChanged;

      _SplitDragSplitIndex = -1;
      _SplitDragOriginalRatio = 0.0f;
      _SplitDragHasBackup = false;

      _HasTabDropPreview = false;
      _TabDropPreview = DockDragDropService.DropInfo.None();

      _OvMode = OverlayPreviewMode.None;
      _OvVisible = false;
      _OvBoundsScreen = Rectangle.Empty;
      _OvLineP0 = Point.Empty;
      _OvLineP1 = Point.Empty;

      _AutoHideActivating = false;
      _AutoHideActivationHoldUntilUtc = DateTime.MinValue;

      _AutoHidePopupHost = null;
      _AutoHidePopupKey = null;
      _AutoHidePopupView = null;

      _AutoHideResizeGrip = null;
      _AutoHideResizeDragging = false;
      _AutoHideResizeDownScreen = Point.Empty;
      _AutoHideResizeStartSize = Size.Empty;
      _AutoHideResizeSide = DockAutoHideSide.Left;
      _AutoHideResizeKey = null;

      // 컨텐츠(View) 내부 클릭도 AutoHide "바깥 클릭"으로 해석할 수 있도록 포워딩 훅을 건다.
      ControlAdded += OnSurfaceControlAdded;
      ControlRemoved += OnSurfaceControlRemoved;

      UpdatePreviewResources();

      _AutoHidePopupSideCache = DockAutoHideSide.Left;
      _AutoHidePopupSideCacheKey = null;

      _VisualDirty = true;
      _MetricsAppliedFromRenderer = false;
    }

    // Bindings (Host Reflection Friendly) =========================================================

    /// <summary>Host가 리플렉션으로 호출할 수 있는 매니저 설정 매서드</summary>
    public void SetManager(DockManager manager)
      => Manager = manager;

    /// <summary>Host가 리플렉션으로 호출할 수 있는 렌더러 설정 메서드</summary>
    public void SetRenderer(VsDockRenderer renderer)
      => Renderer = renderer;

    /// <summary>Host가 리플렉션으로 호출할 수 있는 루트 설정 메서드</summary>
    public void SetRoot(DockNode? root)
      => Root = root;

    // Render Policy =================================================================================

    /// <summary>렌더를 요청한다(Invalidate coalescing)</summary>
    public void RequestRender()
    {
      if (IsDisposed) return;

      if (!IsHandleCreated)
      {
        _InvalidateQueued = true;
        return;
      }

      if (_InvalidateQueued) return;
      _InvalidateQueued = true;

      TryBeginInvoke(FlushInvalidate, false);
    }

    // Overrides ===================================================================================

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
      // base 호출로 깜빡임 유발 가능. Surface는 OnPaint에서 통으로 그린다.
      // (오버레이는 별도 Form이므로 배경/투명 처리와 충돌하지 않는다.)
    }

    protected override void OnPaint(PaintEventArgs e)
    {
      base.OnPaint(e);

      var g = e.Graphics;
      var bounds = ClientRectangle;

      if (_Renderer is not null) g.Clear(_Renderer.Palette[ColorPalette.Role.AppBack]);
      else g.Clear(BackColor);

      if (bounds.Width <= 0 || bounds.Height <= 0) return;

      EnsureVisuals(bounds);

      // (PATCH) “안 보이는데 클릭만 먹는” 케이스 방지: 매 프레임이라도 팝업 View 소유권 보정
      EnsureAutoHidePopupViewAttachedByManagerState();

      if (_Renderer is null) return;

      DrawSplits(g);
      DrawGroups(g);

      if (_Root is null) _Renderer.DrawDockPreview(g, bounds);

      DrawAutoHide(g);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
      try { ClearOverlayPreview(); } catch { }
      try { DestroyOverlay(); } catch { }

      try { HideAutoHidePopupHost(removeView: true); } catch { }

      base.OnHandleDestroyed(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
      base.OnHandleCreated(e);

      if (_InvalidateQueued) BeginInvoke(new Action(FlushInvalidate));
    }

    protected override void OnLocationChanged(EventArgs e)
    {
      base.OnLocationChanged(e);

      // 드래그 중이면 오버레이 위치가 틀어질 수 있으니 즉시 갱신
      if (_TabDragDrop.State == DockDragDropService.DragState.Dragging && _TabDropPreview.IsValid)
        UpdateOverlayPreview(_TabDropPreview);

      // AutoHide 팝업도 위치 보정
      UpdateAutoHidePopupHostLayout(ClientRectangle);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
      base.OnSizeChanged(e);
      MarkVisualDirtyAndRender();

      if (_TabDragDrop.State == DockDragDropService.DragState.Dragging && _TabDropPreview.IsValid)
        UpdateOverlayPreview(_TabDropPreview);

      // AutoHide 팝업도 위치 보정
      UpdateAutoHidePopupHostLayout(ClientRectangle);
    }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        try { ClearOverlayPreview(); } catch { }
        try { DestroyOverlay(); } catch { }

        DetachManagerEvents(_Manager);

        _InputRouter.VisualStateChanged -= OnVisualStateChanged;
        _InputRouter.RequestRaised -= OnInputRequest;
        MouseUp -= OnSurfaceMouseUp;
        _InputRouter.Detach();

        _TabDragDrop.DragBegun -= OnTabDragBegun;
        _TabDragDrop.DragUpdated -= OnTabDragUpdated;
        _TabDragDrop.DragCommitted -= OnTabDragCommitted;
        _TabDragDrop.DragCanceled -= OnTabDragCanceled;
        _TabDragDrop.VisualStateChanged -= OnTabDragVisualStateChanged;
        _TabDragDrop.Detach();

        // 자식 컨트롤 포워딩 훅 정리
        ControlAdded -= OnSurfaceControlAdded;
        ControlRemoved -= OnSurfaceControlRemoved;

        try { UnhookForwardedMouseDownAll(); } catch { }

        try { _Presenter.Dispose(); }
        catch { }

        try { DestroyAutoHidePopupHost(); } catch { }

        _Manager = null;
        _Renderer = null;
        _Root = null;

        _InvalidateQueued = false;
        _VisualDirty = false;

        _ApplyingSplitDrag = false;
        _SplitDragHasBackup = false;
        _SplitDragSplitIndex = -1;

        _AutoHideResizeDragging = false;
        _AutoHideResizeKey = null;
      }

      base.Dispose(disposing);
    }

    // Visual Build =================================================================================

    private void EnsureVisuals(Rectangle bounds)
    {
      // (PATCH) 렌더러가 늦게 Metrics를 채우는 경우가 있어서, 빌드 직전에 한 번 더 시도
      if (!_MetricsAppliedFromRenderer && _Renderer is not null)
        TryApplyRendererMetricsToLayoutEngine(_Renderer);
      if (!_VisualDirty) return;

      _LayoutEngin.Build(_Root, bounds, _Tree);

      // 컨텐츠 재배치 중 깜빡임/레이아웃 흔들림 완화
      SuspendLayout();
      try
      {
        if (_Manager is not null) _Presenter.Present(_Tree);
        else _Presenter.Clear(true);

        // AutoHide 팝업(슬라이드) 컨텐츠를 Surface 위에 올린다.
        PresentAutoHidePopupHost(bounds);

        // (PATCH) Presenter가 뷰를 만진 뒤에도 팝업 View가 남아있도록 보정
        EnsureAutoHidePopupViewAttachedByManagerState();

        RequestPostPresentAutoHideRepair();
      }
      finally
      {
        ResumeLayout(false);
      }

      _VisualDirty = false;
    }

    private void MarkVisualDirtyAndRender()
    {
      _VisualDirty = true;
      RequestRender();
    }

    // Renderer Metrics Fallback ===================================================================

    private void TryApplyRendererMetricsToLayoutEngine(VsDockRenderer renderer)
    {
      if (renderer is null) return;
      if (_MetricsAppliedFromRenderer) return;

      try
      {
        var metrics = renderer.Metrics;

        // 검증 불가/실패면 절대 적용하지 않는다(0값 적용 방지)
        if (!MetricsLooksValid(metrics))
          return;

        _LayoutEngin.Metrics = metrics;
        _MetricsAppliedFromRenderer = true;
      }
      catch
      {
        // ignore: LayoutEngine의 기존 Metrics(CreateVsLike)를 유지
      }
    }

    private static bool MetricsLooksValid(object metricsObj)
    {
      if (metricsObj is null) return false;

      var mustBePositive = new string[]
      {
    "CaptionHeight", "CaptionBarHeight",
    "TabStripHeight", "TabHeight",
    "AutoHideStripThickness", "AutoHideStripSize", "AutoHideStripWidth", "AutoHideStripHeight",
    "AutoHideTabThickness", "AutoHideTabSize", "AutoHideTabHeight",
      };

      var foundCount = 0;

      for (int i = 0; i < mustBePositive.Length; i++)
      {
        if (!TryGetNumericMember(metricsObj, mustBePositive[i], out var v)) continue;

        foundCount++;
        if (v <= 0.0) return false;
      }

      // (PATCH) 아무 멤버도 못 찾았으면 "검증 불가" → 적용 금지
      if (foundCount == 0) return false;

      return true;
    }

    private static bool TryGetNumericMember(object instance, string memberName, out double value)
    {
      value = 0.0;

      var t = instance.GetType();

      try
      {
        var p = t.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p is not null && p.CanRead)
        {
          var v = p.GetValue(instance);
          if (TryConvertToPositiveMetricScalar(v, out value)) return true;
          return false;
        }

        var f = t.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f is not null)
        {
          var v = f.GetValue(instance);
          if (TryConvertToPositiveMetricScalar(v, out value)) return true;
          return false;
        }
      }
      catch
      {
        return false;
      }

      return false;
    }

    private static bool TryConvertToPositiveMetricScalar(object? v, out double value)
    {
      value = 0.0;
      if (v is null) return false;

      if (v is int i) { value = i; return true; }
      if (v is float f) { value = f; return true; }
      if (v is double d) { value = d; return true; }
      if (v is decimal m) { value = (double)m; return true; }
      if (v is short s) { value = s; return true; }
      if (v is long l) { value = l; return true; }
      if (v is byte b) { value = b; return true; }
      if (v is uint ui) { value = ui; return true; }
      if (v is ulong ul) { value = ul; return true; }
      if (v is ushort us) { value = us; return true; }
      if (v is sbyte sb) { value = sb; return true; }

      if (v is Size sz)
      {
        value = Math.Min(sz.Width, sz.Height);
        return true;
      }

      return false;
    }

    // AutoHide Popup Host =========================================================================

    private static string? NormalizeAutoHideKey(string? key)
    {
      if (string.IsNullOrWhiteSpace(key)) return null;

      key = key.Trim();
      if (key.Length == 0) return null;

      // DockManager가 "없음"을 "-" 같은 값으로 줄 수 있음
      if (string.Equals(key, "-", StringComparison.Ordinal)) return null;

      return key;
    }

    private DockAutoHideSide GetCachedAutoHideSideOrDefault(string key)
    {
      if (!string.IsNullOrWhiteSpace(_AutoHidePopupSideCacheKey)
        && string.Equals(_AutoHidePopupSideCacheKey, key, StringComparison.Ordinal))
        return _AutoHidePopupSideCache;

      return DockAutoHideSide.Left;
    }

    private void EnsureAutoHidePopupHost()
    {
      if (_AutoHidePopupHost is null || _AutoHidePopupHost.IsDisposed)
      {
        _AutoHidePopupHost = new Panel
        {
          Visible = false,
          TabStop = false,
          BackColor = Color.Transparent,
        };
      }

      // Presenter가 Controls를 재배치/정리하는 경우를 대비해 항상 Parent를 보장한다.
      if (!ReferenceEquals(_AutoHidePopupHost.Parent, this))
        Controls.Add(_AutoHidePopupHost);

      EnsureAutoHideResizeGrip();
    }

    private void EnsureAutoHideResizeGrip()
    {
      if (_AutoHidePopupHost is null) return;

      if (_AutoHideResizeGrip is null || _AutoHideResizeGrip.IsDisposed)
      {
        _AutoHideResizeGrip = new Panel
        {
          Visible = false,
          TabStop = false,
          BackColor = Color.Transparent,
        };

        _AutoHideResizeGrip.MouseDown += OnAutoHideResizeGripMouseDown;
        _AutoHideResizeGrip.MouseMove += OnAutoHideResizeGripMouseMove;
        _AutoHideResizeGrip.MouseUp += OnAutoHideResizeGripMouseUp;
        _AutoHideResizeGrip.MouseCaptureChanged += OnAutoHideResizeGripCaptureChanged;
      }

      if (!ReferenceEquals(_AutoHideResizeGrip.Parent, _AutoHidePopupHost))
        _AutoHidePopupHost.Controls.Add(_AutoHideResizeGrip);
    }

    private void DestroyAutoHidePopupHost()
    {
      if (_AutoHidePopupHost is null) return;

      try { _AutoHidePopupHost.Controls.Clear(); } catch { }

      try
      {
        if (!_AutoHidePopupHost.IsDisposed)
          _AutoHidePopupHost.Dispose();
      }
      catch { }

      _AutoHidePopupHost = null;
      _AutoHidePopupKey = null;
      _AutoHidePopupView = null;

      _AutoHideResizeGrip = null;
      _AutoHideResizeDragging = false;
      _AutoHideResizeKey = null;
    }

    private void PresentAutoHidePopupHost(Rectangle bounds)
    {
      if (_Manager is null || _Root is null)
      {
        HideAutoHidePopupHost(removeView: true);
        return;
      }

      if (!_Manager.IsAutoHidePopupVisible)
      {
        HideAutoHidePopupHost(removeView: false);
        return;
      }

      var key = NormalizeAutoHideKey(_Manager.ActiveAutoHideKey);
      if (key is null)
      {
        HideAutoHidePopupHost(removeView: false);
        return;
      }

      DockAutoHideSide side;
      Size? popupSize;

      // (PATCH) VisualTree 메타 실패해도 "캐시 side + 기본 size"로 무조건 띄운다
      if (!TryFindAutoHidePopupInfoByVisualTree(key, out side, out popupSize))
      {
        side = GetCachedAutoHideSideOrDefault(key);
        popupSize = null;
      }

      var content = _Manager.Registry.Ensure(key);
      if (content is null)
      {
        HideAutoHidePopupHost(removeView: true);
        return;
      }

      var view = content.View;
      if (view is null || view.IsDisposed)
      {
        HideAutoHidePopupHost(removeView: true);
        return;
      }

      EnsureAutoHidePopupHost();
      if (_AutoHidePopupHost is null) return;

      if (!string.Equals(_AutoHidePopupKey, key, StringComparison.Ordinal))
      {
        _AutoHidePopupKey = key;
        try { _AutoHidePopupHost.Controls.Clear(); } catch { }
        _AutoHidePopupView = null;
      }

      _AutoHidePopupView = view;
      EnsureAutoHidePopupViewAttached(view);

      UpdateAutoHidePopupHostLayoutCore(bounds, side, popupSize);

      try
      {
        _AutoHidePopupHost.Visible = true;
        _AutoHidePopupHost.BringToFront();

        view.BringToFront();
        view.Visible = true;
      }
      catch { }
    }

    private void ClearAutoHidePopupContentOnly()
    {
      if (_AutoHidePopupHost is null) return;

      try
      {
        for (int i = _AutoHidePopupHost.Controls.Count - 1; i >= 0; i--)
        {
          var c = _AutoHidePopupHost.Controls[i];
          if (c is null) continue;
          if (ReferenceEquals(c, _AutoHideResizeGrip)) continue;

          _AutoHidePopupHost.Controls.RemoveAt(i);
        }
      }
      catch { }

      EnsureAutoHideResizeGrip();
    }

    private void HideAutoHidePopupHost(bool removeView)
    {
      if (_AutoHidePopupHost is null) { _AutoHidePopupKey = null; _AutoHidePopupView = null; return; }

      try { _AutoHidePopupHost.Visible = false; } catch { }

      // (PATCH) 리사이즈 드래그 중이면 강제 종료
      CancelAutoHideResizeDrag();

      if (_AutoHideResizeGrip is not null && !_AutoHideResizeGrip.IsDisposed)
      {
        try { _AutoHideResizeGrip.Visible = false; } catch { }
      }

      if (removeView)
      {
        // (PATCH) 그립은 유지하고 컨텐츠만 제거
        ClearAutoHidePopupContentOnly();

        _AutoHidePopupKey = null;
        _AutoHidePopupView = null;
        _AutoHideResizeKey = null;
      }
    }

    private void UpdateAutoHidePopupHostLayout(Rectangle bounds)
    {
      if (_AutoHidePopupHost is null) return;
      if (_Manager is null || _Root is null) return;
      if (!_Manager.IsAutoHidePopupVisible) return;

      var key = NormalizeAutoHideKey(_Manager.ActiveAutoHideKey);
      if (key is null) return;

      DockAutoHideSide side;
      Size? popupSize;

      if (!TryFindAutoHidePopupInfoByVisualTree(key, out side, out popupSize))
      {
        side = GetCachedAutoHideSideOrDefault(key);
        popupSize = null;
      }

      UpdateAutoHidePopupHostLayoutCore(bounds, side, popupSize);
    }

    private void UpdateAutoHidePopupHostLayoutCore(Rectangle bounds, DockAutoHideSide side, Size? popupSize)
    {
      if (_AutoHidePopupHost is null) return;

      if (bounds.Width <= 0 || bounds.Height <= 0) return;

      var edge = MapAutoHideSideToEdge(side);
      var stripThickness = GetAutoHideStripThickness(edge);

      var rc = ComputeAutoHidePopupRect(bounds, side, stripThickness, popupSize);
      if (rc.Width <= 0 || rc.Height <= 0)
      {
        HideAutoHidePopupHost(removeView: false);
        return;
      }

      try { _AutoHidePopupHost.Bounds = rc; } catch { }

      // (PATCH) Host 크기 바뀐 직후 View가 0 사이즈로 남는 것 방지
      if (_AutoHidePopupView is not null && !_AutoHidePopupView.IsDisposed && ReferenceEquals(_AutoHidePopupView.Parent, _AutoHidePopupHost))
      {
        try { _AutoHidePopupView.Bounds = _AutoHidePopupHost.ClientRectangle; } catch { }
      }

      UpdateAutoHideResizeGripLayout(side);
    }

    private void UpdateAutoHideResizeGripLayout(DockAutoHideSide side)
    {
      if (_AutoHidePopupHost is null) return;

      EnsureAutoHideResizeGrip();
      if (_AutoHideResizeGrip is null || _AutoHideResizeGrip.IsDisposed) return;

      // 팝업이 떠 있을 때만 보이게
      if (!_AutoHidePopupHost.Visible)
      {
        try { _AutoHideResizeGrip.Visible = false; } catch { }
        return;
      }

      const int GripThickness = 6;

      var rc = _AutoHidePopupHost.ClientRectangle;
      if (rc.Width <= 0 || rc.Height <= 0)
      {
        try { _AutoHideResizeGrip.Visible = false; } catch { }
        return;
      }

      Rectangle gripRc;

      if (side == DockAutoHideSide.Left)
      {
        gripRc = new Rectangle(Math.Max(0, rc.Width - GripThickness), 0, GripThickness, rc.Height);
        _AutoHideResizeGrip.Cursor = Cursors.SizeWE;
      }
      else if (side == DockAutoHideSide.Right)
      {
        gripRc = new Rectangle(0, 0, GripThickness, rc.Height);
        _AutoHideResizeGrip.Cursor = Cursors.SizeWE;
      }
      else if (side == DockAutoHideSide.Top)
      {
        gripRc = new Rectangle(0, Math.Max(0, rc.Height - GripThickness), rc.Width, GripThickness);
        _AutoHideResizeGrip.Cursor = Cursors.SizeNS;
      }
      else
      {
        gripRc = new Rectangle(0, 0, rc.Width, GripThickness);
        _AutoHideResizeGrip.Cursor = Cursors.SizeNS;
      }

      try
      {
        _AutoHideResizeGrip.Bounds = gripRc;
        _AutoHideResizeGrip.Visible = true;
        _AutoHideResizeGrip.BringToFront();
      }
      catch { }
    }

    private void CancelAutoHideResizeDrag()
    {
      if (!_AutoHideResizeDragging) return;

      _AutoHideResizeDragging = false;

      if (_AutoHideResizeGrip is not null && !_AutoHideResizeGrip.IsDisposed)
      {
        try { _AutoHideResizeGrip.Capture = false; } catch { }
      }
    }

    private void OnAutoHideResizeGripMouseDown(object? sender, MouseEventArgs e)
    {
      if (e.Button != MouseButtons.Left) return;
      if (_AutoHidePopupHost is null || _AutoHidePopupHost.IsDisposed) return;
      if (_Manager is null) return;
      if (!_Manager.IsAutoHidePopupVisible) return;

      var key = _Manager.ActiveAutoHideKey;
      if (string.IsNullOrWhiteSpace(key)) return;

      key = key.Trim();
      if (key.Length == 0) return;

      if (!TryFindAutoHidePopupInfoByVisualTree(key, out var side, out var _)) return;

      _AutoHideResizeKey = key;
      _AutoHideResizeSide = side;

      var ptScreen = (_AutoHideResizeGrip is not null)
        ? _AutoHideResizeGrip.PointToScreen(e.Location)
        : PointToScreen(e.Location);

      _AutoHideResizeDragging = true;
      _AutoHideResizeDownScreen = ptScreen;
      _AutoHideResizeStartSize = _AutoHidePopupHost.Size;

      if (_AutoHideResizeGrip is not null && !_AutoHideResizeGrip.IsDisposed)
      {
        try { _AutoHideResizeGrip.Capture = true; } catch { }
      }
    }

    private void OnAutoHideResizeGripMouseMove(object? sender, MouseEventArgs e)
    {
      if (!_AutoHideResizeDragging) return;
      if (_AutoHidePopupHost is null || _AutoHidePopupHost.IsDisposed) { CancelAutoHideResizeDrag(); return; }
      if (_Manager is null || !_Manager.IsAutoHidePopupVisible) { CancelAutoHideResizeDrag(); return; }

      var key = _AutoHideResizeKey;
      if (string.IsNullOrWhiteSpace(key)) return;

      var ptScreen = (_AutoHideResizeGrip is not null)
        ? _AutoHideResizeGrip.PointToScreen(e.Location)
        : PointToScreen(e.Location);

      var dx = ptScreen.X - _AutoHideResizeDownScreen.X;
      var dy = ptScreen.Y - _AutoHideResizeDownScreen.Y;

      var reqW = _AutoHideResizeStartSize.Width;
      var reqH = _AutoHideResizeStartSize.Height;

      switch (_AutoHideResizeSide)
      {
        case DockAutoHideSide.Left:
          reqW = _AutoHideResizeStartSize.Width + dx;
          break;

        case DockAutoHideSide.Right:
          reqW = _AutoHideResizeStartSize.Width - dx;
          break;

        case DockAutoHideSide.Top:
          reqH = _AutoHideResizeStartSize.Height + dy;
          break;

        default:
          reqH = _AutoHideResizeStartSize.Height - dy;
          break;
      }

      var bounds = ClientRectangle;

      var edge = MapAutoHideSideToEdge(_AutoHideResizeSide);
      var stripThickness = GetAutoHideStripThickness(edge);

      var clamped = ClampAutoHidePopupSize(bounds, _AutoHideResizeSide, stripThickness, new Size(reqW, reqH));

      _AutoHidePopupSizeCache[key.Trim()] = clamped;

      UpdateAutoHidePopupHostLayoutCore(bounds, _AutoHideResizeSide, clamped);

      RequestRender();
    }

    private void OnAutoHideResizeGripMouseUp(object? sender, MouseEventArgs e)
    {
      if (e.Button != MouseButtons.Left) return;
      CancelAutoHideResizeDrag();
    }

    private void OnAutoHideResizeGripCaptureChanged(object? sender, EventArgs e)
    {
      CancelAutoHideResizeDrag();
    }

    private static Size ClampAutoHidePopupSize(Rectangle bounds, DockAutoHideSide side, int stripThickness, Size desired)
    {
      const int MinW = 180;
      const int MinH = 140;

      var w = desired.Width <= 0 ? MinW : desired.Width;
      var h = desired.Height <= 0 ? MinH : desired.Height;

      if (side == DockAutoHideSide.Left || side == DockAutoHideSide.Right)
      {
        var maxW = Math.Max(1, bounds.Width - stripThickness);
        w = MathEx.Clamp(w, MinW, maxW);

        var maxH = Math.Max(1, bounds.Height);
        h = MathEx.Clamp(h, MinH, maxH);

        return new Size(w, h);
      }
      else
      {
        var maxH = Math.Max(1, bounds.Height - stripThickness);
        h = MathEx.Clamp(h, MinH, maxH);

        var maxW = Math.Max(1, bounds.Width);
        w = MathEx.Clamp(w, MinW, maxW);

        return new Size(w, h);
      }
    }

    // AutoHide Popup Host - View Ownership Guard ==================================================

    private void EnsureAutoHidePopupViewAttached(Control view)
    {
      if (_AutoHidePopupHost is null) return;
      if (view is null || view.IsDisposed) return;

      try
      {
        var parent = view.Parent;
        if (parent is not null && !ReferenceEquals(parent, _AutoHidePopupHost))
          parent.Controls.Remove(view);
      }
      catch { }

      try
      {
        if (!ReferenceEquals(view.Parent, _AutoHidePopupHost))
        {
          view.Dock = DockStyle.Fill;
          _AutoHidePopupHost.Controls.Add(view);
        }

        view.Visible = true;
        view.Enabled = true;

        if (_AutoHidePopupHost.ClientSize.Width > 0 && _AutoHidePopupHost.ClientSize.Height > 0)
          view.Bounds = _AutoHidePopupHost.ClientRectangle;

        // 그립이 항상 위
        if (_AutoHideResizeGrip is not null && !_AutoHideResizeGrip.IsDisposed)
          _AutoHideResizeGrip.BringToFront();

        _AutoHidePopupHost.PerformLayout();
      }
      catch { }
    }

    private void EnsureAutoHidePopupViewAttachedByManagerState()
    {
      if (_Manager is null) return;
      if (!_Manager.IsAutoHidePopupVisible) return;

      var key = NormalizeAutoHideKey(_Manager.ActiveAutoHideKey);
      if (key is null) return;

      EnsureAutoHidePopupHost();
      if (_AutoHidePopupHost is null) return;

      object? content = null;
      try { content = _Manager.Registry.Get(key); } catch { content = null; }
      if (content is null)
      {
        try { content = _Manager.Registry.Ensure(key); } catch { content = null; }
      }
      if (content is null) return;

      Control? view = null;
      try
      {
        if (content is IDockContent dc) view = dc.View;
        else view = TryGetPropertyValueByReflection(content, "View") as Control;
      }
      catch { view = null; }

      if (view is null || view.IsDisposed) return;

      _AutoHidePopupView = view;
      EnsureAutoHidePopupViewAttached(view);
    }

    private static DockVisualTree.DockEdge MapAutoHideSideToEdge(DockAutoHideSide side)
    {
      return side switch
      {
        DockAutoHideSide.Left => DockVisualTree.DockEdge.Left,
        DockAutoHideSide.Right => DockVisualTree.DockEdge.Right,
        DockAutoHideSide.Top => DockVisualTree.DockEdge.Top,
        _ => DockVisualTree.DockEdge.Bottom,
      };
    }

    private static DockAutoHideSide MapAutoHideEdgeToSide(DockVisualTree.DockEdge edge)
    {
      return edge switch
      {
        DockVisualTree.DockEdge.Left => DockAutoHideSide.Left,
        DockVisualTree.DockEdge.Right => DockAutoHideSide.Right,
        DockVisualTree.DockEdge.Top => DockAutoHideSide.Top,
        _ => DockAutoHideSide.Bottom,
      };
    }

    private int GetAutoHideStripThickness(DockVisualTree.DockEdge edge)
    {
      var strips = _Tree.AutoHideStrips;
      for (int i = 0; i < strips.Count; i++)
      {
        var sv = strips[i];
        if (sv.Edge != edge) continue;
        if (sv.Bounds.IsEmpty) continue;

        var t = (edge == DockVisualTree.DockEdge.Left || edge == DockVisualTree.DockEdge.Right)
          ? Math.Max(0, sv.Bounds.Width)
          : Math.Max(0, sv.Bounds.Height);

        if (t > 0) return t;
      }

      // (PATCH) VisualTree에 strip이 없거나 두께가 0이면 기본값으로 팝업 레이아웃을 살린다.
      return GetDefaultAutoHideStripThickness();
    }

    private int GetDefaultAutoHideStripThickness()
    {
      // LayoutEngine.Metrics에서 읽히면 그 값 사용, 아니면 안전 기본값
      try
      {
        var m = _LayoutEngin.Metrics;

        if (TryGetNumericMember(m, "AutoHideStripThickness", out var v) && v > 0.0)
          return Math.Max(1, (int)Math.Round(v));

        if (TryGetNumericMember(m, "AutoHideStripSize", out v) && v > 0.0)
          return Math.Max(1, (int)Math.Round(v));
      }
      catch { }

      return 24; // 안전 기본값(VS 느낌)
    }

    private static Rectangle ComputeAutoHidePopupRect(Rectangle bounds, DockAutoHideSide side, int stripThickness, Size? popupSize)
    {
      // 기본 정책(픽셀): 너무 작으면 못 쓰므로 최소치 둔다.
      const int MinW = 180;
      const int MinH = 140;

      var wDefault = Math.Max(MinW, (int)Math.Round(bounds.Width * 0.30));
      var hDefault = Math.Max(MinH, (int)Math.Round(bounds.Height * 0.30));

      var w = wDefault;
      var h = hDefault;

      if (popupSize.HasValue)
      {
        var ps = popupSize.Value;

        if (ps.Width > 0) w = Math.Max(MinW, ps.Width);
        if (ps.Height > 0) h = Math.Max(MinH, ps.Height);
      }

      if (side == DockAutoHideSide.Left || side == DockAutoHideSide.Right)
      {
        // VS 느낌: 세로는 기본적으로 전체 높이. PopupSize.Height가 있으면 그 값도 허용(클램프).
        var maxW = Math.Max(1, bounds.Width - stripThickness);
        w = MathEx.Clamp(w, MinW, maxW);

        var maxH = bounds.Height;
        h = MathEx.Clamp((popupSize.HasValue && popupSize.Value.Height > 0) ? h : maxH, MinH, maxH);

        var x = (side == DockAutoHideSide.Left)
          ? bounds.X + stripThickness
          : bounds.Right - stripThickness - w;

        var y = bounds.Y;

        return new Rectangle(x, y, w, h);
      }
      else
      {
        var maxH = Math.Max(1, bounds.Height - stripThickness);
        h = MathEx.Clamp(h, MinH, maxH);

        var maxW = bounds.Width;
        w = MathEx.Clamp((popupSize.HasValue && popupSize.Value.Width > 0) ? w : maxW, MinW, maxW);

        var x = bounds.X;

        var y = (side == DockAutoHideSide.Top)
          ? bounds.Y + stripThickness
          : bounds.Bottom - stripThickness - h;

        return new Rectangle(x, y, w, h);
      }
    }

    // VisualTree 기반 AutoHide 메타 탐색 ===========================================================

    private bool TryFindAutoHidePopupInfoByVisualTree(string persistKey, out DockAutoHideSide side, out Size? popupSize)
    {
      side = DockAutoHideSide.Left;
      popupSize = null;

      if (string.IsNullOrWhiteSpace(persistKey)) return false;

      persistKey = persistKey.Trim();
      if (persistKey.Length == 0) return false;

      // 1) 정상 경로: VisualTree 메타
      if (_Tree.TryGetAutoHidePopupMeta(persistKey, out var meta))
      {
        side = MapAutoHideEdgeToSide(meta.Edge);
        popupSize = meta.PopupSize;

        // (PATCH) UI 캐시 우선
        if (_AutoHidePopupSizeCache.TryGetValue(persistKey, out var cached))
          popupSize = cached;

        return true;
      }

      // 2) fallback: AutoHideTabs에서 키를 찾고, 포함된 스트립 Edge로 side 추론
      var tabs = _Tree.AutoHideTabs;
      var hitTabIndex = -1;

      for (int i = 0; i < tabs.Count; i++)
      {
        var tv = tabs[i];
        var k = GetAutoHideTabPersistKeySafe(tv.ContentKey, tv);
        if (string.Equals(k, persistKey, StringComparison.Ordinal))
        {
          hitTabIndex = i;
          break;
        }
      }

      if (hitTabIndex < 0) return false;

      var strips = _Tree.AutoHideStrips;

      for (int si = 0; si < strips.Count; si++)
      {
        var sv = strips[si];
        var start = sv.TabStart;
        var end = sv.TabStart + sv.TabCount;

        if (hitTabIndex >= start && hitTabIndex < end)
        {
          side = MapAutoHideEdgeToSide(sv.Edge);
          popupSize = null;

          // (PATCH) UI 캐시 우선
          if (_AutoHidePopupSizeCache.TryGetValue(persistKey, out var cached))
            popupSize = cached;

          return true;
        }
      }

      return false;
    }

    // Internals ===================================================================================

    private void RequestPostPresentAutoHideRepair()
    {
      if (IsDisposed) return;
      if (_PostPresentRepairQueued) return;

      _PostPresentRepairQueued = true;

      TryBeginInvoke(() =>
      {
        _PostPresentRepairQueued = false;

        if (IsDisposed) return;

        // Presenter가 z-order/parent를 뒤늦게 바꿔도 한 번 더 복구
        try
        {
          if (_AutoHidePopupHost is not null && !_AutoHidePopupHost.IsDisposed && _AutoHidePopupHost.Visible)
            _AutoHidePopupHost.BringToFront();
        }
        catch { }

        EnsureAutoHidePopupViewAttachedByManagerState();

        try
        {
          if (_AutoHidePopupHost is not null && !_AutoHidePopupHost.IsDisposed && _AutoHidePopupHost.Visible)
          {
            _AutoHidePopupHost.BringToFront();

            if (_AutoHideResizeGrip is not null && !_AutoHideResizeGrip.IsDisposed)
              _AutoHideResizeGrip.BringToFront();
          }
        }
        catch { }

        RequestRender();
      }, false);
    }

    private void FlushInvalidate()
    {
      if (IsDisposed) return;

      _InvalidateQueued = false;
      Invalidate();
    }

    private void AttachManagerEvents(DockManager? manager)
    {
      if (manager is null) return;

      manager.Events.LayoutChanged += OnManagerLayoutChanged;

      manager.Events.ActiveContentChanged += OnActiveContentChanged;
      manager.Events.ContentAdded += OnContentChanged;
      manager.Events.ContentRemoved += OnContentChanged;
      manager.Events.ContentClosed += OnContentChanged;
    }

    private void DetachManagerEvents(DockManager? manager)
    {
      if (manager is null) return;

      manager.Events.LayoutChanged -= OnManagerLayoutChanged;

      manager.Events.ActiveContentChanged -= OnActiveContentChanged;
      manager.Events.ContentAdded -= OnContentChanged;
      manager.Events.ContentRemoved -= OnContentChanged;
      manager.Events.ContentClosed -= OnContentChanged;
    }

    private void OnManagerLayoutChanged(object? sender, DockLayoutChangedEventArgs e)
    {
      if (IsDisposed) return;

      if (IsHandleCreated && InvokeRequired)
      {
        TryBeginInvoke(() => OnManagerLayoutChanged(sender, e), true);
        return;
      }

      _Root = e.NewRoot;
      MarkVisualDirtyAndRender();
    }

    private void OnActiveContentChanged(object? sender, DockActiveContentChangedEventArgs e)
    {
      if (IsDisposed) return;

      if (InvokeRequired)
      {
        TryBeginInvoke(() => OnActiveContentChanged(sender, e), true);
        return;
      }

      // 레이아웃/탭 배치가 변하지 않는 “활성 전환”은 재빌드 없이 Present + Render만 수행
      if (!_VisualDirty && _Tree.Groups.Count > 0 && _Manager is not null)
      {
        SuspendLayout();
        try
        {
          _Presenter.Present(_Tree);

          // AutoHide 팝업은 Active 변경으로도 표시 상태가 바뀔 수 있으므로 보정
          PresentAutoHidePopupHost(ClientRectangle);

          // (PATCH) Presenter가 뷰를 뺏는 케이스 보정
          EnsureAutoHidePopupViewAttachedByManagerState();
          RequestPostPresentAutoHideRepair();
        }
        catch { }
        finally { ResumeLayout(false); }

        RequestRender();
        return;
      }

      MarkVisualDirtyAndRender();
    }

    private void OnContentChanged(object? sender, DockContentEventArgs e)
    {
      if (IsDisposed) return;

      if (IsHandleCreated && InvokeRequired)
      {
        TryBeginInvoke(() => OnContentChanged(sender, e), true);
        return;
      }

      MarkVisualDirtyAndRender();
    }

    private void OnVisualStateChanged()
    {
      RequestRender();
    }

    private void OnInputRequest(DockInputRouter.DockInputRequest req)
    {
      if (_Manager is null) return;

      if (_TabDragDrop.State == DockDragDropService.DragState.Dragging)
      {
        if (req.Kind is DockInputRouter.DockInputRequestKind.ActivateTab
          or DockInputRouter.DockInputRequestKind.ActivateAutoHideTab
          or DockInputRouter.DockInputRequestKind.CloseTab
          or DockInputRouter.DockInputRequestKind.CloseGroup)
          return;
      }

      switch (req.Kind)
      {
        case DockInputRouter.DockInputRequestKind.ActivateTab:
          HandleActivateTab(req.GroupIndex, req.TabIndex);
          return;

        case DockInputRouter.DockInputRequestKind.ActivateAutoHideTab:
          HandleActivateAutoHideTab(req.AutoHideStripIndex, req.AutoHideTabIndex);
          return;

        case DockInputRouter.DockInputRequestKind.DismissAutoHidePopup:
          HandleDismissAutoHidePopup();
          return;

        case DockInputRouter.DockInputRequestKind.CloseTab:
          HandleCloseTab(req.TabIndex);
          return;

        case DockInputRouter.DockInputRequestKind.CloseGroup:
          HandleCloseGroup(req.GroupIndex);
          return;

        case DockInputRouter.DockInputRequestKind.SplitterDrag:
          HandleSplitterDrag(req.SplitIndex, req.Ratio, req.Phase);
          return;

        default:
          return;
      }
    }

    private void TryBeginInvoke(Action action, bool fallbackQueue)
    {
      if (IsDisposed) return;
      if (!IsHandleCreated)
      {
        if (fallbackQueue)
        {
          _VisualDirty = true;
          _InvalidateQueued = true;
        }
        return;
      }

      try { BeginInvoke(action); }
      catch
      {
        if (fallbackQueue)
        {
          _VisualDirty = true;
          _InvalidateQueued = true;
        }
        else
        {
          _InvalidateQueued = false;
        }
      }
    }

    // Content MouseDown Forwarding (AutoHide Dismiss) =============================================

    private void OnSurfaceControlAdded(object? sender, ControlEventArgs e)
    {
      var c = e.Control;
      if (c is null || c.IsDisposed) return;

      HookForwardedMouseDownTree(c);
    }

    private void OnSurfaceControlRemoved(object? sender, ControlEventArgs e)
    {
      var c = e.Control;
      if (c is null) return;

      UnhookForwardedMouseDownTree(c);
    }

    private void OnForwardedMouseDown(object? sender, MouseEventArgs e)
    {
      if (e.Button != MouseButtons.Left) return;
      if (_Manager is null) return;

      // AutoHide 팝업이 떠 있을 때만 "바깥 클릭"을 의미있게 처리한다.
      if (!_Manager.IsAutoHidePopupVisible) return;

      if (sender is not Control c) return;

      // (PATCH) 팝업 호스트(그립 포함) 내부 클릭은 바깥 클릭이 아니다.
      if (IsFromAutoHidePopupHost(c)) return;

      // 팝업 컨텐츠 내부 클릭은 바깥 클릭이 아니다.
      if (IsFromActiveAutoHidePopupView(c)) return;

      _InputRouter.NotifyExternalMouseDown();
    }

    private void OnForwardedKeyDown(object? sender, KeyEventArgs e)
    {
      if (e.KeyCode != Keys.Escape) return;
      if (_Manager is null) return;

      // AutoHide 팝업이 떠 있을 때만 ESC를 소비한다.
      if (!_Manager.IsAutoHidePopupVisible) return;

      _InputRouter.NotifyExternalKeyDown(e.KeyData);

      e.Handled = true;
      e.SuppressKeyPress = true;
    }

    private void OnForwardedDisposed(object? sender, EventArgs e)
    {
      if (sender is not Control c) return;
      UnhookForwardedMouseDownOne(c);
    }

    private void OnForwardedControlAdded(object? sender, ControlEventArgs e)
    {
      var c = e.Control;
      if (c is null || c.IsDisposed) return;

      HookForwardedMouseDownTree(c);
    }

    private void OnForwardedControlRemoved(object? sender, ControlEventArgs e)
    {
      var c = e.Control;
      if (c is null) return;

      UnhookForwardedMouseDownTree(c);
    }

    private void HookForwardedMouseDownTree(Control root)
    {
      if (root is null || root.IsDisposed) return;
      if (ReferenceEquals(root, this)) return;

      HookForwardedMouseDownOne(root);

      var children = root.Controls;
      for (int i = 0; i < children.Count; i++)
        HookForwardedMouseDownTree(children[i]);
    }

    private void HookForwardedMouseDownOne(Control c)
    {
      if (c is null || c.IsDisposed) return;
      if (ReferenceEquals(c, this)) return;

      if (!_ForwardedMouseDownHooks.Add(c)) return;

      c.MouseDown += OnForwardedMouseDown;
      c.KeyDown += OnForwardedKeyDown;
      c.Disposed += OnForwardedDisposed;

      // 뷰 내부에 동적으로 자식이 추가되는 케이스까지 커버
      c.ControlAdded += OnForwardedControlAdded;
      c.ControlRemoved += OnForwardedControlRemoved;
    }

    private void UnhookForwardedMouseDownTree(Control root)
    {
      if (root is null) return;
      if (ReferenceEquals(root, this)) return;

      UnhookForwardedMouseDownOne(root);

      Control.ControlCollection? children = null;
      try { children = root.Controls; } catch { }

      if (children is null) return;

      for (int i = 0; i < children.Count; i++)
        UnhookForwardedMouseDownTree(children[i]);
    }

    private void UnhookForwardedMouseDownOne(Control c)
    {
      if (c is null) return;

      if (!_ForwardedMouseDownHooks.Remove(c)) return;

      try { c.MouseDown -= OnForwardedMouseDown; } catch { }
      try { c.KeyDown -= OnForwardedKeyDown; } catch { }
      try { c.Disposed -= OnForwardedDisposed; } catch { }

      try { c.ControlAdded -= OnForwardedControlAdded; } catch { }
      try { c.ControlRemoved -= OnForwardedControlRemoved; } catch { }
    }

    private void UnhookForwardedMouseDownAll()
    {
      if (_ForwardedMouseDownHooks.Count == 0) return;

      var arr = new Control[_ForwardedMouseDownHooks.Count];
      _ForwardedMouseDownHooks.CopyTo(arr);

      for (int i = 0; i < arr.Length; i++)
      {
        var c = arr[i];
        if (c is null) continue;

        try { c.MouseDown -= OnForwardedMouseDown; } catch { }
        try { c.KeyDown -= OnForwardedKeyDown; } catch { }
        try { c.Disposed -= OnForwardedDisposed; } catch { }
        try { c.ControlAdded -= OnForwardedControlAdded; } catch { }
        try { c.ControlRemoved -= OnForwardedControlRemoved; } catch { }
      }

      _ForwardedMouseDownHooks.Clear();
    }

    private bool IsFromAutoHidePopupHost(Control source)
    {
      if (_AutoHidePopupHost is null) return false;

      var cur = source;
      while (cur is not null)
      {
        if (ReferenceEquals(cur, _AutoHidePopupHost)) return true;
        cur = cur.Parent;
      }

      return false;
    }

    private bool IsFromActiveAutoHidePopupView(Control source)
    {
      if (source is null) return false;

      // (PATCH) Host 내부면 항상 "팝업 내부 클릭"
      if (_AutoHidePopupHost is not null && !_AutoHidePopupHost.IsDisposed && _AutoHidePopupHost.Visible)
      {
        var cur = source;
        while (cur is not null)
        {
          if (ReferenceEquals(cur, _AutoHidePopupHost)) return true;
          cur = cur.Parent;
        }
      }

      // (PATCH) 캐시된 View 기준
      if (_AutoHidePopupView is not null && !_AutoHidePopupView.IsDisposed)
      {
        var cur = source;
        while (cur is not null)
        {
          if (ReferenceEquals(cur, _AutoHidePopupView)) return true;
          cur = cur.Parent;
        }
      }

      // fallback: 기존 Manager 기준(정규화 적용)
      if (_Manager is null) return false;

      var key = NormalizeAutoHideKey(_Manager.ActiveAutoHideKey);
      if (key is null) return false;

      var content = _Manager.Registry.Get(key);
      if (content is null) return false;

      var view = content.View;
      if (view is null || view.IsDisposed) return false;

      var cur2 = source;
      while (cur2 is not null)
      {
        if (ReferenceEquals(cur2, view)) return true;
        cur2 = cur2.Parent;
      }

      return false;
    }

    // Tab Drag&Drop / Overlay / Splitter Drag / Drawing / Helpers / Overlay Form ====================

    private void OnTabDragBegun(DockDragDropService.DragInfo info)
    {
      _HasTabDropPreview = false;
      _TabDropPreview = DockDragDropService.DropInfo.None();
      ClearOverlayPreview();
      RequestRender();
    }

    private void OnTabDragUpdated(DockDragDropService.DragInfo info, DockDragDropService.DropInfo drop)
    {
      drop = FilterDropByGroupKind(info, drop);

      _TabDropPreview = drop;
      _HasTabDropPreview = drop.IsValid;

      UpdateOverlayPreview(drop);

      RequestRender();
    }

    private void OnTabDragCommitted(DockDragDropService.DragInfo info, DockDragDropService.DropInfo drop)
    {
      drop = FilterDropByGroupKind(info, drop);

      var commitDrop = drop;
      if (!commitDrop.IsValid && _HasTabDropPreview && _TabDropPreview.IsValid)
        commitDrop = _TabDropPreview;

      _HasTabDropPreview = false;
      _TabDropPreview = DockDragDropService.DropInfo.None();

      ClearOverlayPreview();

      TryCommitTabDrop(info, commitDrop);

      MarkVisualDirtyAndRender();
    }

    private void OnTabDragCanceled(DockDragDropService.DragInfo info)
    {
      _HasTabDropPreview = false;
      _TabDropPreview = DockDragDropService.DropInfo.None();

      ClearOverlayPreview();

      RequestRender();
    }

    private void OnTabDragVisualStateChanged()
    {
      RequestRender();
    }

    private DockDragDropService.DropInfo FilterDropByGroupKind(DockDragDropService.DragInfo info, DockDragDropService.DropInfo drop)
    {
      if (!drop.IsValid) return drop;
      if (info.SourceGroupIndex < 0) return drop;
      if (drop.TargetGroupIndex < 0) return drop;

      if ((uint)info.SourceGroupIndex >= (uint)_Tree.Groups.Count) return drop;
      if ((uint)drop.TargetGroupIndex >= (uint)_Tree.Groups.Count) return drop;

      var src = _Tree.Groups[info.SourceGroupIndex].Node;
      var dst = _Tree.Groups[drop.TargetGroupIndex].Node;

      if (src.ContentKind == dst.ContentKind) return drop;

      // 서로 Kind가 다르면:
      // - DockToZone의 Left/Right/Top/Bottom(분할)만 허용
      // - Center(합치기) / InsertTab(탭삽입) 은 차단
      if (drop.Kind == DockDragDropService.DropKind.DockToZone
        && drop.Zone != DockDragDropService.DockZone.Center)
        return drop;

      return DockDragDropService.DropInfo.None();
    }

    private void TryCommitTabDrop(DockDragDropService.DragInfo info, DockDragDropService.DropInfo drop)
    {
      if (_Manager is null) return;
      if (!drop.IsValid) return;

      var keyObj = info.SourceContentKey;

      string? key = keyObj as string;
      if (key is null && keyObj is IDockContent dc) key = dc.PersistKey;

      if (string.IsNullOrWhiteSpace(key)) return;
      key = key.Trim();
      if (key.Length == 0) return;

      if ((uint)drop.TargetGroupIndex >= (uint)_Tree.Groups.Count) return;

      var dstGroup = _Tree.Groups[drop.TargetGroupIndex].Node;
      var dstGroupId = dstGroup.NodeId;

      DockGroupNode? srcGroup = null;
      if (info.SourceGroupIndex >= 0 && (uint)info.SourceGroupIndex < (uint)_Tree.Groups.Count)
        srcGroup = _Tree.Groups[info.SourceGroupIndex].Node;

      var crossKind = (srcGroup is not null && srcGroup.ContentKind != dstGroup.ContentKind);

      // cross-kind인 경우:
      // - DockToZone + (Left/Right/Top/Bottom)만 허용
      // - Center / InsertTab 은 커밋 단계에서도 차단
      if (crossKind)
      {
        if (drop.Kind != DockDragDropService.DropKind.DockToZone) return;
        if (drop.Zone == DockDragDropService.DockZone.Center) return;
      }

      // 같은 그룹 Center 도킹은 noop(활성만)
      if (srcGroup is not null
        && drop.Kind == DockDragDropService.DropKind.DockToZone
        && drop.Zone == DockDragDropService.DockZone.Center
        && string.Equals(srcGroup.NodeId, dstGroupId, StringComparison.Ordinal))
      {
        _Manager.SetGroupActive(dstGroupId, key, "UI:TabDrag:CenterNoop");
        _Manager.SetActiveContent(key);
        Root = _Manager.Root;
        return;
      }

      // InsertTab은 cross-kind면 여기 오기 전에 return됨(위 방어)
      if (drop.Kind == DockDragDropService.DropKind.InsertTab)
      {
        _Manager.MoveTab(key, dstGroupId, drop.InsertIndex, makeActive: true, reason: "UI:TabDrag:InsertTab");
        Root = _Manager.Root;
        return;
      }

      if (drop.Kind == DockDragDropService.DropKind.DockToZone)
      {
        var side = MapDockZoneToDropSide(drop.Zone);

        var dstIsEmpty = dstGroup.Items.Count == 0;

        // Side 도킹에서 “빈 대상 그룹” 처리:
        // - same-kind: 기존처럼 Center로 강등 가능
        // - cross-kind: Center 합치기가 금지이므로 아예 금지(드랍 실패)
        if (side != DockDropSide.Center && dstIsEmpty)
        {
          if (!crossKind) side = DockDropSide.Center;
          else return;
        }

        // newPaneRatio=0.0 : DockManager 정책(기본 ratio) 사용
        _Manager.Dock(key, dstGroupId, side, newPaneRatio: 0.0, makeActive: true, reason: "UI:TabDrag:DockToZone");

        // 커밋 후 키가 레이아웃에서 빠진 경우 복구
        if (!LayoutContainsKey(_Manager.Root, key))
        {
          if (!crossKind)
          {
            _Manager.Dock(key, dstGroupId, DockDropSide.Center, newPaneRatio: 0.0, makeActive: true, reason: "UI:TabDrag:RecoverMissingKey");
          }
          else
          {
            // cross-kind는 Center 금지. 같은 side로 1회 재시도.
            _Manager.Dock(key, dstGroupId, side, newPaneRatio: 0.0, makeActive: true, reason: "UI:TabDrag:RecoverMissingKey:Side");
          }
        }

        Root = _Manager.Root;
        return;
      }
    }

    private static bool LayoutContainsKey(DockNode? node, string persistKey)
    {
      if (node is null) return false;

      if (node is DockGroupNode g)
      {
        for (int i = 0; i < g.Items.Count; i++)
        {
          if (string.Equals(g.Items[i].PersistKey, persistKey, StringComparison.Ordinal))
            return true;
        }
        return false;
      }

      if (node is DockAutoHideNode ah)
      {
        for (int i = 0; i < ah.Items.Count; i++)
        {
          if (string.Equals(ah.Items[i].PersistKey, persistKey, StringComparison.Ordinal))
            return true;
        }
        return false;
      }

      if (node is DockSplitNode s)
        return LayoutContainsKey(s.First, persistKey) || LayoutContainsKey(s.Second, persistKey);

      if (node is DockFloatingNode f)
        return LayoutContainsKey(f.Root, persistKey);

      return false;
    }

    private static DockDropSide MapDockZoneToDropSide(DockDragDropService.DockZone zone)
    {
      return zone switch
      {
        DockDragDropService.DockZone.Left => DockDropSide.Left,
        DockDragDropService.DockZone.Right => DockDropSide.Right,
        DockDragDropService.DockZone.Top => DockDropSide.Top,
        DockDragDropService.DockZone.Bottom => DockDropSide.Bottom,
        _ => DockDropSide.Center,
      };
    }

    private void UpdatePreviewResources()
    {
      if (_Overlay is null) return;

      var border = _Renderer?.Palette[ColorPalette.Role.DockPreviewBorder] ?? SystemColors.Highlight;
      var fill = _Renderer?.Palette[ColorPalette.Role.DockPreviewFill] ?? Color.FromArgb(70, border);

      _Overlay.BorderColor = border;
      _Overlay.FillColor = fill;
      _Overlay.Invalidate();
    }

    // Overlay Preview (Multi-Monitor Safe) =========================================================

    private enum OverlayPreviewMode : byte
    {
      None = 0,
      ZoneRect = 1,
      InsertLine = 2,
    }

    private void EnsureOverlay()
    {
      if (_Overlay is not null) return;
      if (!IsHandleCreated) return;

      var owner = FindForm();
      if (owner is null || owner.IsDisposed) return;

      var border = _Renderer?.Palette[ColorPalette.Role.DockPreviewBorder] ?? SystemColors.Highlight;
      var fill = _Renderer?.Palette[ColorPalette.Role.DockPreviewFill] ?? Color.FromArgb(70, border);

      _Overlay = new DockPreviewOverlayForm(owner)
      {
        BorderColor = border,
        FillColor = fill,
      };
    }

    private void DestroyOverlay()
    {
      if (_Overlay is null) return;

      try { _Overlay.Hide(); } catch { }

      try
      {
        if (!_Overlay.IsDisposed)
          _Overlay.Close();
      }
      catch { }

      try { _Overlay.Dispose(); } catch { }

      _Overlay = null;
      _OvMode = OverlayPreviewMode.None;
      _OvVisible = false;
      _OvBoundsScreen = Rectangle.Empty;
      _OvLineP0 = Point.Empty;
      _OvLineP1 = Point.Empty;
    }

    private void UpdateOverlayPreview(DockDragDropService.DropInfo drop)
    {
      if (!IsHandleCreated) return;

      if (!drop.IsValid)
      {
        ClearOverlayPreview();
        return;
      }

      EnsureOverlay();
      if (_Overlay is null) return;

      if (drop.Kind == DockDragDropService.DropKind.InsertTab)
      {
        if (!TryComputeInsertLineClient(drop, out var p0, out var p1))
        {
          ClearOverlayPreview();
          return;
        }

        var minY = Math.Min(p0.Y, p1.Y);
        var maxY = Math.Max(p0.Y, p1.Y);
        var rcClient = new Rectangle(p0.X - 2, minY - 1, 4, (maxY - minY) + 2);
        if (rcClient.Width <= 0 || rcClient.Height <= 0)
        {
          ClearOverlayPreview();
          return;
        }

        var rcScreen = RectangleToScreen(rcClient);
        var lp0 = new Point(p0.X - rcClient.X, p0.Y - rcClient.Y);
        var lp1 = new Point(p1.X - rcClient.X, p1.Y - rcClient.Y);

        if (_OvVisible && _OvMode == OverlayPreviewMode.InsertLine
          && _OvBoundsScreen == rcScreen
          && _OvLineP0 == lp0 && _OvLineP1 == lp1)
          return;

        _OvMode = OverlayPreviewMode.InsertLine;
        _OvVisible = true;
        _OvBoundsScreen = rcScreen;
        _OvLineP0 = lp0;
        _OvLineP1 = lp1;

        _Overlay.Mode = DockPreviewOverlayForm.PreviewMode.InsertLine;
        _Overlay.LineP0 = lp0;
        _Overlay.LineP1 = lp1;

        _Overlay.SetBoundsNoActivate(rcScreen);
        _Overlay.ShowNoActivate();
        _Overlay.Invalidate();
        return;
      }

      if (drop.Kind == DockDragDropService.DropKind.DockToZone)
      {
        if (!TryComputeDockZoneRectClient(drop, out var rcClient))
        {
          ClearOverlayPreview();
          return;
        }

        var rcScreen = RectangleToScreen(rcClient);

        if (_OvVisible && _OvMode == OverlayPreviewMode.ZoneRect && _OvBoundsScreen == rcScreen)
          return;

        _OvMode = OverlayPreviewMode.ZoneRect;
        _OvVisible = true;
        _OvBoundsScreen = rcScreen;
        _OvLineP0 = Point.Empty;
        _OvLineP1 = Point.Empty;

        _Overlay.Mode = DockPreviewOverlayForm.PreviewMode.ZoneRect;

        _Overlay.SetBoundsNoActivate(rcScreen);
        _Overlay.ShowNoActivate();
        _Overlay.Invalidate();
        return;
      }

      ClearOverlayPreview();
    }

    private void ClearOverlayPreview()
    {
      if (_Overlay is null) { _OvVisible = false; _OvMode = OverlayPreviewMode.None; return; }
      if (!_OvVisible) return;

      try { _Overlay.Hide(); } catch { }

      _OvVisible = false;
      _OvMode = OverlayPreviewMode.None;
      _OvBoundsScreen = Rectangle.Empty;
      _OvLineP0 = Point.Empty;
      _OvLineP1 = Point.Empty;
    }

    private bool TryComputeInsertLineClient(DockDragDropService.DropInfo drop, out Point p0, out Point p1)
    {
      p0 = Point.Empty;
      p1 = Point.Empty;

      if ((uint)drop.TargetGroupIndex >= (uint)_Tree.Groups.Count) return false;

      var gv = _Tree.Groups[drop.TargetGroupIndex];
      var strip = gv.TabStripBounds;
      if (strip.IsEmpty) return false;

      var x = strip.X;

      if (gv.TabCount <= 0)
      {
        x = strip.X + 2;
      }
      else
      {
        var start = gv.TabStart;
        var end = gv.TabStart + gv.TabCount;

        var ins = drop.InsertIndex;
        if (ins <= 0) x = _Tree.Tabs[start].Bounds.X;
        else if (ins >= gv.TabCount) x = _Tree.Tabs[end - 1].Bounds.Right;
        else x = _Tree.Tabs[start + ins].Bounds.X;
      }

      if (x < strip.X + 1) x = strip.X + 1;
      if (x > strip.Right - 1) x = strip.Right - 1;

      var y = strip.Y + 2;
      var h = Math.Max(2, strip.Height - 4);

      p0 = new Point(x, y);
      p1 = new Point(x, y + h);
      return true;
    }

    private bool TryComputeDockZoneRectClient(DockDragDropService.DropInfo drop, out Rectangle r)
    {
      r = Rectangle.Empty;

      var content = drop.TargetBounds;
      if (content.IsEmpty) return false;

      var zr = _TabDragDrop.ZoneRatio;
      if (zr < 0.05f) zr = 0.05f;
      if (zr > 0.45f) zr = 0.45f;

      var leftW = (int)Math.Floor(content.Width * zr);
      var topH = (int)Math.Floor(content.Height * zr);

      switch (drop.Zone)
      {
        case DockDragDropService.DockZone.Left:
          r = new Rectangle(content.X, content.Y, leftW, content.Height);
          return true;
        case DockDragDropService.DockZone.Right:
          r = new Rectangle(content.Right - leftW, content.Y, leftW, content.Height);
          return true;
        case DockDragDropService.DockZone.Top:
          r = new Rectangle(content.X, content.Y, content.Width, topH);
          return true;
        case DockDragDropService.DockZone.Bottom:
          r = new Rectangle(content.X, content.Bottom - topH, content.Width, topH);
          return true;
        default:
          r = content;
          return true;
      }
    }

    // Splitter Drag =================================================================================

    private static bool IsAutoHideStripSplit(DockSplitNode splitNode)
      => splitNode.First is DockAutoHideNode || splitNode.Second is DockAutoHideNode;

    private void HandleSplitterDrag(int splitIndex, float ratio, DockInputRouter.DockSplitterDragPhase phase)
    {
      if (_Manager is null) return;
      if ((uint)splitIndex >= (uint)_Tree.Splits.Count) return;

      var split = _Tree.Splits[splitIndex];

      // (PATCH) AutoHide 스트립 확보용 Split은 평소 고정. 팝업 크기 조절은 팝업 그립으로만 한다.
      if (IsAutoHideStripSplit(split.Node))
        return;

      switch (phase)
      {
        case DockInputRouter.DockSplitterDragPhase.Begin:
          _SplitDragSplitIndex = splitIndex;
          _SplitDragOriginalRatio = (float)split.Node.Ratio;
          _SplitDragHasBackup = true;

          _ApplyingSplitDrag = true;
          split.Node.Ratio = ratio;
          MarkVisualDirtyAndRender();
          return;

        case DockInputRouter.DockSplitterDragPhase.Update:
          split.Node.Ratio = ratio;
          _ApplyingSplitDrag = true;
          MarkVisualDirtyAndRender();
          return;

        case DockInputRouter.DockSplitterDragPhase.End:
          split.Node.Ratio = ratio;

          if (_ApplyingSplitDrag)
          {
            _ApplyingSplitDrag = false;

            _Manager.ApplyLayout(_Manager.Root, $"UI:SplitDrag:{split.Node.NodeId}", false);
            Root = _Manager.Root;
          }

          _SplitDragHasBackup = false;
          _SplitDragSplitIndex = -1;

          MarkVisualDirtyAndRender();
          return;

        case DockInputRouter.DockSplitterDragPhase.Cancel:
          if (_SplitDragHasBackup && _SplitDragSplitIndex == splitIndex)
            split.Node.Ratio = _SplitDragOriginalRatio;

          _SplitDragHasBackup = false;
          _SplitDragSplitIndex = -1;

          _ApplyingSplitDrag = false;
          MarkVisualDirtyAndRender();
          return;

        default:
          return;
      }
    }

    // Drawing =====================================================================================

    private void DrawSplits(Graphics g)
    {
      if (_Renderer is null) return;

      var hover = _InputRouter.Hover;
      var pressed = _InputRouter.Pressed;
      var splits = _Tree.Splits;

      for (int i = 0; i < splits.Count; i++)
      {
        var s = splits[i];

        // (PATCH) AutoHide 스트립 확보용 Split은 일반 Splitter처럼 보이면 안 된다.
        if (IsAutoHideStripSplit(s.Node))
          continue;

        var hot = hover.Kind == DockVisualTree.RegionKind.Splitter && hover.SplitIndex == i;
        var dragging = _InputRouter.IsSplitterDragging && pressed.Kind == DockVisualTree.RegionKind.Splitter && pressed.SplitIndex == i;

        _Renderer.DrawSplitter(g, s.SplitterBounds, hot, dragging);
      }
    }

    private void DrawGroups(Graphics g)
    {
      if (_Renderer is null) return;

      var groups = _Tree.Groups;
      if (groups.Count == 0) return;

      var hover = _InputRouter.Hover;
      var pressed = _InputRouter.Pressed;

      for (int gi = 0; gi < groups.Count; gi++)
      {
        var gv = groups[gi];
        var node = gv.Node;

        if (!gv.CaptionBounds.IsEmpty)
        {
          var title = GetActiveTitleForGroup(node);
          var isActive = IsGroupActiveByManager(node);

          var showClose = CanCloseAnyInGroup(node);

          var closeHot = showClose && hover.Kind == DockVisualTree.RegionKind.CaptionClose && hover.GroupIndex == gi;
          var closePressed = showClose && pressed.Kind == DockVisualTree.RegionKind.CaptionClose && pressed.GroupIndex == gi;

          _Renderer.DrawCaption(g, gv.CaptionBounds, title,
            (isActive ? DockRenderer.CaptionVisualState.Active : DockRenderer.CaptionVisualState.Inactive),
            showClose, closeHot, closePressed);
        }

        if (!gv.TabStripBounds.IsEmpty)
        {
          _Renderer.DrawTabStripBackground(g, gv.TabStripBounds);

          var activeKey = node.ActiveKey;
          if (string.IsNullOrWhiteSpace(activeKey) && node.Items.Count > 0)
            activeKey = node.Items[0].PersistKey;

          var tabStart = gv.TabStart;
          var tabEnd = gv.TabStart + gv.TabCount;

          for (int ti = tabStart; ti < tabEnd; ti++)
          {
            if ((uint)ti >= (uint)_Tree.Tabs.Count) break;

            var tv = _Tree.Tabs[ti];

            var key = tv.ContentKey as string ?? string.Empty;
            var text = GetTitleOrKey(key);

            var isActiveTab = (!string.IsNullOrWhiteSpace(activeKey) && string.Equals(activeKey, key, StringComparison.Ordinal));

            var state = DockRenderer.TabVisualState.Normal;
            if (isActiveTab) state = DockRenderer.TabVisualState.Active;
            else if (hover.Kind is DockVisualTree.RegionKind.Tab or DockVisualTree.RegionKind.TabClose)
            {
              if (hover.TabIndex == ti) state = DockRenderer.TabVisualState.Hot;
            }

            var canClose = CanClose(key);
            var showClose = canClose && !tv.CloseBounds.IsEmpty;

            var closeHot = showClose && hover.Kind == DockVisualTree.RegionKind.TabClose && hover.TabIndex == ti;
            var closePressed = showClose && pressed.Kind == DockVisualTree.RegionKind.TabClose && pressed.TabIndex == ti;

            _Renderer.DrawTab(g, tv.Bounds, text, state, showClose, closeHot, closePressed);
          }
        }
      }
    }

    private void DrawAutoHide(Graphics g)
    {
      if (_Renderer is null) return;

      var strips = _Tree.AutoHideStrips;
      if (strips.Count == 0) return;

      var hover = _InputRouter.Hover;
      var pressed = _InputRouter.Pressed;

      var popupKey = _Manager?.ActiveAutoHideKey;
      if (!string.IsNullOrWhiteSpace( popupKey )) popupKey = popupKey!.Trim( );

      for (int si = 0 ; si < strips.Count ; si++)
      {
        var sv = strips [ si ];
        if (sv.Bounds.IsEmpty) continue;

        var dir = MapAutoHideEdgeToTextDirection( sv.Edge );

        var start = sv.TabStart;
        var end = sv.TabStart + sv.TabCount;

        _Renderer.DrawAutoHideStripBackground( g, sv.Bounds );

        for (int ti = start ; ti < end ; ti++)
        {
          if ((uint) ti >= (uint) _Tree.AutoHideTabs.Count) break;

          var tv = _Tree.AutoHideTabs [ ti ];

          var key = GetAutoHideTabPersistKeySafe( tv.ContentKey, tv );
          var text = string.IsNullOrWhiteSpace( key ) ? string.Empty : GetTitleOrKey( key );

          var isActive = tv.IsActive;

          // Manager가 표시 중인 AutoHide 키를 우선 반영(레이아웃 트리 동기화가 늦는 프레임 방지)
          if (!string.IsNullOrWhiteSpace( popupKey ) && !string.IsNullOrWhiteSpace( key ))
            isActive |= string.Equals( popupKey, key, StringComparison.Ordinal );

          var localIndex = ti - start;

          var isHoverHot =
            hover.Kind == DockVisualTree.RegionKind.AutoHideTab
            && (hover.AutoHideTabIndex == ti || (hover.AutoHideStripIndex == si && hover.AutoHideTabIndex == localIndex));

          var isPressedHot =
            pressed.Kind == DockVisualTree.RegionKind.AutoHideTab
            && (pressed.AutoHideTabIndex == ti || (pressed.AutoHideStripIndex == si && pressed.AutoHideTabIndex == localIndex));

          var state = DockRenderer.TabVisualState.Normal;

          if (isActive) state = DockRenderer.TabVisualState.Active;
          else if (isHoverHot || isPressedHot) state = DockRenderer.TabVisualState.Hot;

          _Renderer.DrawAutoHideTab( g, tv.Bounds, text, state, dir );
        }
      }
    }

    private static VsDockRenderer.AutoHideTextDirection MapAutoHideEdgeToTextDirection(DockVisualTree.DockEdge edge)
    {
      return edge switch
      {
        DockVisualTree.DockEdge.Left => VsDockRenderer.AutoHideTextDirection.Rotate270,
        DockVisualTree.DockEdge.Right => VsDockRenderer.AutoHideTextDirection.Rotate90,
        _ => VsDockRenderer.AutoHideTextDirection.Horizontal,
      };
    }

    private void OnSurfaceMouseUp(object? sender, MouseEventArgs e)
    {
      if (IsDisposed) return;
      if (e.Button != MouseButtons.Right) return;
      if (_Manager is null) return;

      var hit = DockHitTest.HitTest(_Tree, e.Location);

      if (hit.Kind == DockVisualTree.RegionKind.Tab)
      {
        if ((uint)hit.TabIndex >= (uint)_Tree.Tabs.Count) return;
        var tv = _Tree.Tabs[hit.TabIndex];
        var key = GetPersistKeySafe(tv.ContentKey).Trim();
        if (key.Length == 0) return;

        IDockContent? content = null;
        try { content = _Manager.Registry.Get(key); } catch { content = null; }
        if (content is null || content.Kind != DockContentKind.ToolWindow) return;

        ShowTabContextMenu(key, e.Location);
        return;
      }

      if (hit.Kind == DockVisualTree.RegionKind.AutoHideTab)
      {
        if (!TryResolveAutoHideTabIndices(hit.AutoHideStripIndex, hit.AutoHideTabIndex, out _, out var globalIndex)) return;
        if ((uint)globalIndex >= (uint)_Tree.AutoHideTabs.Count) return;

        var tv = _Tree.AutoHideTabs[globalIndex];
        var key = GetAutoHideTabPersistKeySafe(tv.ContentKey, tv).Trim();
        if (key.Length == 0) return;

        ShowAutoHideTabContextMenu(key, e.Location);
      }
    }

    private void ShowTabContextMenu(string key, Point clientPoint)
    {
      if (IsDisposed) return;
      var menu = new ContextMenuStrip();
      menu.Items.Add("AutoHide Left", null, (s, e) => PinToolToAutoHideFromMenu(key, DockAutoHideSide.Left));
      menu.Items.Add("AutoHide Right", null, (s, e) => PinToolToAutoHideFromMenu(key, DockAutoHideSide.Right));
      menu.Items.Add("AutoHide Top", null, (s, e) => PinToolToAutoHideFromMenu(key, DockAutoHideSide.Top));
      menu.Items.Add("AutoHide Bottom", null, (s, e) => PinToolToAutoHideFromMenu(key, DockAutoHideSide.Bottom));
      SafeShowContextMenu(menu, clientPoint);
    }

    private void ShowAutoHideTabContextMenu(string key, Point clientPoint)
    {
      if (IsDisposed) return;
      if (_Manager is null) return;

      var menu = new ContextMenuStrip();
      menu.Items.Add("Unpin AutoHide", null, (s, e) =>
      {
        _Manager.UnpinFromAutoHide(key, targetGroupNodeId: null, makeActive: true, reason: "UI:ContextMenu:UnpinAutoHide");
        MarkVisualDirtyAndRender();
      });
      SafeShowContextMenu(menu, clientPoint);
    }

    private void SafeShowContextMenu(ContextMenuStrip menu, Point clientPoint)
    {
      if (menu is null) return;

      if (IsDisposed)
      {
        try { menu.Dispose(); } catch { }
        return;
      }

      try { menu.Show(this, clientPoint); }
      catch
      {
        try { menu.Dispose(); } catch { }
      }
    }

    private void PinToolToAutoHideFromMenu(string key, DockAutoHideSide side)
    {
      if (_Manager is null) return;

      if (_Manager.PinToAutoHide(key, side, popupSize: null, showPopup: true, reason: $"UI:ContextMenu:Pin:{side}:{key}"))
        MarkVisualDirtyAndRender();
    }

    private static string GetPersistKeySafe(object? keyObj)
    {
      if (keyObj is string s) return s;
      if (keyObj is IDockContent dc) return dc.PersistKey ?? string.Empty;
      if (keyObj is null) return string.Empty;

      // (PATCH) 래퍼/모델 타입(ContentRef 등) 대응
      return TryGetStringByReflection(keyObj, "PersistKey", "Key", "Id", "Name") ?? string.Empty;
    }

    private string GetAutoHideTabPersistKeySafe(object? contentKey, object tvObj)
    {
      var key = GetPersistKeySafe(contentKey);
      if (!string.IsNullOrWhiteSpace(key)) return key.Trim();

      // tvObj 자체가 PersistKey/Key를 들고 있는 케이스(구조체/모델 래퍼 등)
      key = (TryGetStringByReflection(tvObj, "PersistKey", "Key", "Id", "Name") ?? string.Empty).Trim();
      if (key.Length != 0) return key;

      // tvObj 내부에 ContentKey/Content 등으로 한 번 더 감싸져 있을 수 있다.
      object? nested =
        TryGetPropertyValueByReflection(tvObj, "ContentKey")
        ?? TryGetPropertyValueByReflection(tvObj, "Content")
        ?? TryGetPropertyValueByReflection(tvObj, "Item");

      if (nested is null) return string.Empty;

      key = GetPersistKeySafe(nested);
      if (!string.IsNullOrWhiteSpace(key)) return key.Trim();

      key = (TryGetStringByReflection(nested, "PersistKey", "Key", "Id", "Name") ?? string.Empty).Trim();
      return key;
    }

    // Command handlers / missing helpers =============================================================

    private void HandleActivateTab(int groupIndex, int tabIndex)
    {
      if (_Manager is null) return;
      if ((uint)groupIndex >= (uint)_Tree.Groups.Count) return;
      if ((uint)tabIndex >= (uint)_Tree.Tabs.Count) return;

      var tv = _Tree.Tabs[tabIndex];
      var key = GetPersistKeySafe(tv.ContentKey);
      if (string.IsNullOrWhiteSpace(key)) return;

      key = key.Trim();
      if (key.Length == 0) return;

      var g = _Tree.Groups[groupIndex].Node;
      _Manager.SetGroupActive(g.NodeId, key, "UI:ActivateTab");
      _Manager.SetActiveContent(key);

      RequestRender();
    }

    private void HandleActivateAutoHideTab(int stripIndex, int tabIndex)
    {
      if (_Manager is null) return;
      if (_AutoHideActivating) return;

      if (!TryResolveAutoHideTabIndices( stripIndex, tabIndex, out _, out var globalIndex ))
        return;

      if ((uint) globalIndex >= (uint) _Tree.AutoHideTabs.Count) return;

      var tv = _Tree.AutoHideTabs [ globalIndex ];
      var key = GetAutoHideTabPersistKeySafe( tv.ContentKey, tv );
      if (string.IsNullOrWhiteSpace( key )) return;

      key = key.Trim( );
      if (key.Length == 0) return;

      if (_Manager.IsAutoHidePopupVisible
        && string.Equals(_Manager.ActiveAutoHideKey, key, StringComparison.Ordinal))
      {
        RequestRender();
        return;
      }

      _AutoHideActivating = true;
      _AutoHideActivationHoldUntilUtc = DateTime.UtcNow.AddMilliseconds(250);
      try
      {
        // "Show" 우선(토글은 상태 불일치 시 반대로 동작 가능)
        var shown =
          TryInvokeByReflection( _Manager, "ShowAutoHidePopup", key, "UI:AutoHideTab" )
          || TryInvokeByReflection( _Manager, "ShowAutoHidePopup", key )
          || TrySetManagerAutoHidePopup( key, visible: true );

        // ShowAutoHidePopup 내부에서 ActiveContent까지 맞추므로 여기서 다시 SetActiveContent를 호출하면
        // 동일 키 재진입으로 토글-off가 발생할 수 있다.
        if (shown) MarkVisualDirtyAndRender( );
        else RequestRender( );
      }
      finally
      {
        _AutoHideActivating = false;
      }
    }

    private void HandleDismissAutoHidePopup()
    {
      if (_Manager is null) return;
      if (_AutoHideActivating) return;

      if (_Manager.IsAutoHidePopupVisible && DateTime.UtcNow < _AutoHideActivationHoldUntilUtc)
        return;

      TrySetManagerAutoHidePopup(_Manager.ActiveAutoHideKey ?? string.Empty, visible: false);

      // UI 즉시 숨김(Manager 이벤트 지연/누락 대비)
      HideAutoHidePopupHost(removeView: false);

      RequestRender();
    }

    private void HandleCloseTab(int tabIndex)
    {
      if (_Manager is null) return;
      if ((uint)tabIndex >= (uint)_Tree.Tabs.Count) return;

      var tv = _Tree.Tabs[tabIndex];
      var key = GetPersistKeySafe(tv.ContentKey);
      if (string.IsNullOrWhiteSpace(key)) return;

      key = key.Trim();
      if (key.Length == 0) return;

      if (!CanClose(key)) return;

      // AutoHide 팝업이 이 키면 먼저 닫는다.
      var ahKey = _Manager.ActiveAutoHideKey;
      if (!string.IsNullOrWhiteSpace(ahKey))
      {
        ahKey = ahKey.Trim();
        if (string.Equals(ahKey, key, StringComparison.Ordinal) && _Manager.IsAutoHidePopupVisible)
          HandleDismissAutoHidePopup();
      }

      TryClosePersistKey(key, "UI:CloseTab");

      MarkVisualDirtyAndRender();
    }

    private void HandleCloseGroup(int groupIndex)
    {
      if (_Manager is null) return;
      if ((uint)groupIndex >= (uint)_Tree.Groups.Count) return;

      var g = _Tree.Groups[groupIndex].Node;
      if (g.Items.Count <= 0) return;

      var any = false;

      for (int i = 0; i < g.Items.Count; i++)
      {
        var key = g.Items[i].PersistKey;
        if (string.IsNullOrWhiteSpace(key)) continue;

        key = key.Trim();
        if (key.Length == 0) continue;

        if (!CanClose(key)) continue;

        any |= TryClosePersistKey(key, "UI:CloseGroup");
      }

      if (any) MarkVisualDirtyAndRender();
      else RequestRender();
    }

    private string GetActiveTitleForGroup(DockGroupNode node)
    {
      if (node is null) return string.Empty;

      var key = node.ActiveKey;
      if (string.IsNullOrWhiteSpace(key) && node.Items.Count > 0)
        key = node.Items[0].PersistKey;

      if (string.IsNullOrWhiteSpace(key)) return string.Empty;

      key = key.Trim();
      if (key.Length == 0) return string.Empty;

      return GetTitleOrKey(key);
    }

    private bool IsGroupActiveByManager(DockGroupNode node)
    {
      if (_Manager is null) return false;
      if (node is null) return false;

      var groupKey = node.ActiveKey;
      if (string.IsNullOrWhiteSpace(groupKey) && node.Items.Count > 0)
        groupKey = node.Items[0].PersistKey;

      if (string.IsNullOrWhiteSpace(groupKey)) return false;

      groupKey = groupKey.Trim();
      if (groupKey.Length == 0) return false;

      var activeKey = GetManagerActiveKeySafe();
      if (string.IsNullOrWhiteSpace(activeKey)) return false;

      activeKey = activeKey.Trim();
      if (activeKey.Length == 0) return false;

      return string.Equals(activeKey, groupKey, StringComparison.Ordinal);
    }

    private bool CanCloseAnyInGroup(DockGroupNode node)
    {
      if (node is null) return false;
      if (node.Items.Count <= 0) return false;

      for (int i = 0; i < node.Items.Count; i++)
      {
        var key = node.Items[i].PersistKey;
        if (string.IsNullOrWhiteSpace(key)) continue;

        key = key.Trim();
        if (key.Length == 0) continue;

        if (CanClose(key)) return true;
      }

      return false;
    }

    private string GetTitleOrKey(string persistKey)
    {
      if (string.IsNullOrWhiteSpace(persistKey)) return string.Empty;

      var key = persistKey.Trim();
      if (key.Length == 0) return string.Empty;

      if (_Manager is null) return key;

      object? content = null;
      try { content = _Manager.Registry.Get(key); } catch { content = null; }

      if (content is null) return key;

      var title =
        TryGetStringByReflection(content, "Title", "Text", "DisplayName", "Caption", "Name")
        ?? string.Empty;

      title = title.Trim();
      return title.Length == 0 ? key : title;
    }

    private bool CanClose(string persistKey)
    {
      if (string.IsNullOrWhiteSpace(persistKey)) return false;

      var key = persistKey.Trim();
      if (key.Length == 0) return false;

      if (_Manager is null) return true;

      object? content = null;
      try { content = _Manager.Registry.Get(key); } catch { content = null; }

      if (content is null) return true;

      var b =
        TryGetBoolByReflection(content, "CanClose", "IsCloseable", "IsClosable", "CanUserClose", "AllowClose");

      return b ?? true;
    }

    private string GetManagerActiveKeySafe()
    {
      if (_Manager is null) return string.Empty;

      var s =
        TryGetStringByReflection(_Manager, "ActiveKey", "ActivePersistKey", "ActiveContentKey")
        ?? string.Empty;

      if (!string.IsNullOrWhiteSpace(s)) return s;

      object? ac = null;
      try
      {
        ac =
          TryGetPropertyValueByReflection(_Manager, "ActiveContent")
          ?? TryGetPropertyValueByReflection(_Manager, "Active");
      }
      catch { ac = null; }

      if (ac is IDockContent dc) return dc.PersistKey ?? string.Empty;

      if (ac is not null)
      {
        var k = TryGetStringByReflection(ac, "PersistKey", "Key", "Id", "Name");
        if (!string.IsNullOrWhiteSpace(k)) return k!;
      }

      return string.Empty;
    }

    private bool TryResolveAutoHideTabIndices ( int stripIndex, int tabIndex, out int resolvedStripIndex, out int globalTabIndex )
    {
      resolvedStripIndex = -1;
      globalTabIndex = -1;

      if (tabIndex < 0) return false;

      // stripIndex가 정상인 경우: local/global 둘 다 허용
      if ((uint) stripIndex < (uint) _Tree.AutoHideStrips.Count)
      {
        var strip = _Tree.AutoHideStrips [ stripIndex ];
        resolvedStripIndex = stripIndex;

        // Accept both "strip-local" and "global" tabIndex.
        if (tabIndex < strip.TabCount) globalTabIndex = strip.TabStart + tabIndex;
        else if (tabIndex >= strip.TabStart && tabIndex < strip.TabStart + strip.TabCount) globalTabIndex = tabIndex;
        else return false;

        return (uint) globalTabIndex < (uint) _Tree.AutoHideTabs.Count;
      }

      // stripIndex가 유실된 케이스: tabIndex를 global로 보고 스트립을 역추적
      if ((uint) tabIndex >= (uint) _Tree.AutoHideTabs.Count) return false;

      for (int si = 0 ; si < _Tree.AutoHideStrips.Count ; si++)
      {
        var s = _Tree.AutoHideStrips [ si ];
        var start = s.TabStart;
        var end = start + s.TabCount;

        if (tabIndex >= start && tabIndex < end)
        {
          resolvedStripIndex = si;
          globalTabIndex = tabIndex;
          return true;
        }
      }

      return false;
    }

    // Reflection bridges (DockManager/Registry API 차이를 흡수) =====================================

    private bool TrySetManagerAutoHidePopup(string key, bool visible)
    {
      if (_Manager is null) return false;

      // 1) 메서드 우선(있는 구현을 최대한 존중)
      if (visible)
      {
        if (TryInvokeByReflection(_Manager, "ShowAutoHidePopup", key, "UI:AutoHideTab")) return true;
        if (TryInvokeByReflection(_Manager, "ShowAutoHidePopup", key)) return true;
        if (TryInvokeByReflection(_Manager, "ActivateAutoHide", key)) return true;
        if (TryInvokeByReflection(_Manager, "OpenAutoHidePopup", key)) return true;
        if (TryInvokeByReflection(_Manager, "SetAutoHidePopup", key, true)) return true;
      }
      else
      {
        // HideAutoHidePopup(String reason) 시그니처 대응
        if (TryInvokeByReflection(_Manager, "HideAutoHidePopup", "UI:AutoHideDismiss")) return true;
        if (TryInvokeByReflection(_Manager, "HideAutoHidePopup")) return true;

        if (TryInvokeByReflection(_Manager, "DismissAutoHidePopup")) return true;
        if (TryInvokeByReflection(_Manager, "CloseAutoHidePopup")) return true;
        if (TryInvokeByReflection(_Manager, "SetAutoHidePopupVisible", false)) return true;
      }

      // 2) 프로퍼티 세팅 폴백
      var ok = false;

      if (visible)
      {
        ok |= TrySetPropertyByReflection(_Manager, "ActiveAutoHideKey", key);
        ok |= TrySetPropertyByReflection(_Manager, "IsAutoHidePopupVisible", true);
      }
      else
      {
        ok |= TrySetPropertyByReflection(_Manager, "IsAutoHidePopupVisible", false);
      }

      return ok;
    }

    private bool TryClosePersistKey(string key, string reason)
    {
      if (_Manager is null) return false;
      if (string.IsNullOrWhiteSpace(key)) return false;

      key = key.Trim();
      if (key.Length == 0) return false;

      // 1) DockManager에 close 계열이 있으면 우선 사용
      if (TryInvokeByReflection(_Manager, "CloseContent", key, reason)) return true;
      if (TryInvokeByReflection(_Manager, "CloseContent", key)) return true;

      if (TryInvokeByReflection(_Manager, "CloseTab", key, reason)) return true;
      if (TryInvokeByReflection(_Manager, "CloseTab", key)) return true;

      if (TryInvokeByReflection(_Manager, "RequestClose", key, reason)) return true;
      if (TryInvokeByReflection(_Manager, "RequestClose", key)) return true;

      if (TryInvokeByReflection(_Manager, "Close", key, reason)) return true;
      if (TryInvokeByReflection(_Manager, "Close", key)) return true;

      // 2) Registry에 close 계열이 있으면 사용
      object? registry = null;
      try { registry = _Manager.Registry; } catch { registry = null; }

      if (registry is not null)
      {
        if (TryInvokeByReflection(registry, "Close", key, reason)) return true;
        if (TryInvokeByReflection(registry, "Close", key)) return true;
        if (TryInvokeByReflection(registry, "Remove", key)) return true;
      }

      // 3) Content 인스턴스에 Close()가 있으면 호출
      object? content = null;
      try { content = _Manager.Registry.Get(key); } catch { content = null; }

      if (content is not null)
      {
        if (TryInvokeByReflection(content, "Close")) return true;
        if (TryInvokeByReflection(content, "Dispose")) return true;
      }

      return false;
    }

    private static object? TryGetPropertyValueByReflection(object instance, string propertyName)
    {
      var t = instance.GetType();
      var p = t.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      if (p is null || !p.CanRead) return null;

      try { return p.GetValue(instance); }
      catch { return null; }
    }

    private static string? TryGetStringByReflection(object instance, params string[] propertyNames)
    {
      for (int i = 0; i < propertyNames.Length; i++)
      {
        var name = propertyNames[i];
        var v = TryGetPropertyValueByReflection(instance, name);
        if (v is string s && !string.IsNullOrWhiteSpace(s)) return s;
      }

      return null;
    }

    private static bool? TryGetBoolByReflection(object instance, params string[] propertyNames)
    {
      for (int i = 0; i < propertyNames.Length; i++)
      {
        var name = propertyNames[i];
        var v = TryGetPropertyValueByReflection(instance, name);
        if (v is bool b) return b;
      }

      return null;
    }

    private static bool TrySetPropertyByReflection(object instance, string propertyName, object? value)
    {
      var t = instance.GetType();
      var p = t.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      if (p is null || !p.CanWrite) return false;

      try
      {
        p.SetValue(instance, value);
        return true;
      }
      catch
      {
        return false;
      }
    }

    private static bool TryInvokeByReflection(object instance, string methodName, params object?[] args)
    {
      var t = instance.GetType();
      var ms = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

      for (int i = 0; i < ms.Length; i++)
      {
        var m = ms[i];
        if (!string.Equals(m.Name, methodName, StringComparison.Ordinal)) continue;

        var ps = m.GetParameters();
        if (ps.Length != args.Length) continue;

        var ok = true;

        for (int ai = 0; ai < args.Length; ai++)
        {
          var a = args[ai];
          var pt = ps[ai].ParameterType;

          if (a is null)
          {
            if (pt.IsValueType && Nullable.GetUnderlyingType(pt) is null) { ok = false; break; }
            continue;
          }

          var at = a.GetType();
          if (!pt.IsAssignableFrom(at))
          {
            try
            {
              // 숫자/기본형 변환 허용(가능한 경우만)
              var converted = Convert.ChangeType(a, pt);
              args[ai] = converted;
            }
            catch
            {
              ok = false;
              break;
            }
          }
        }

        if (!ok) continue;

        try
        {
          var r = m.Invoke(instance, args);
          if (r is bool rb) return rb;
          return true; // void 포함
        }
        catch
        {
          return false;
        }
      }

      return false;
    }

    // Overlay Form =================================================================================

    private sealed class DockPreviewOverlayForm : Form
    {
      // Types ==================================================================

      public enum PreviewMode : byte { None = 0, ZoneRect = 1, InsertLine = 2 }

      // Fields =================================================================

      private readonly Form _Owner;
      private PreviewMode _Mode;
      private Point _LineP0;
      private Point _LineP1;
      private Color _BorderColor;
      private Color _FillColor;

      // Properties =============================================================

      public PreviewMode Mode
      {
        get { return _Mode; }
        set { _Mode = value; }
      }

      public Point LineP0
      {
        get { return _LineP0; }
        set { _LineP0 = value; }
      }

      public Point LineP1
      {
        get { return _LineP1; }
        set { _LineP1 = value; }
      }

      public Color BorderColor
      {
        get { return _BorderColor; }
        set { _BorderColor = value; }
      }

      public Color FillColor
      {
        get { return _FillColor; }
        set { _FillColor = value; }
      }

      // Ctor ===================================================================

      public DockPreviewOverlayForm(Form owner)
      {
        _Owner = owner;
        _Mode = PreviewMode.None;
        _LineP0 = Point.Empty;
        _LineP1 = Point.Empty;
        _BorderColor = SystemColors.Highlight;
        _FillColor = Color.FromArgb(70, SystemColors.Highlight);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = false;

        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;

        DoubleBuffered = true;

        Owner = _Owner;
      }

      // Public =================================================================

      public void SetBoundsNoActivate(Rectangle screenBounds)
      {
        Bounds = screenBounds;
      }

      public void ShowNoActivate()
      {
        if (IsDisposed) return;

        if (!Visible)
        {
          if (Owner is null || Owner.IsDisposed) Owner = _Owner;

          Show();
        }
      }

      // Overrides ==============================================================

      protected override bool ShowWithoutActivation
        => true;

      protected override CreateParams CreateParams
      {
        get
        {
          const int WS_EX_NOACTIVATE = 0x08000000;
          const int WS_EX_TOOLWINDOW = 0x00000080;
          const int WS_EX_TRANSPARENT = 0x00000020;

          var cp = base.CreateParams;
          cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT;
          return cp;
        }
      }

      protected override void OnPaintBackground(PaintEventArgs e)
      {
        // 투명키 배경이므로 배경 페인트 불필요
      }

      protected override void OnPaint(PaintEventArgs e)
      {
        base.OnPaint(e);

        if (_Mode == PreviewMode.None) return;

        if (_Mode == PreviewMode.ZoneRect)
        {
          var rc = ClientRectangle;
          if (rc.Width <= 0 || rc.Height <= 0) return;

          if (_FillColor.A != 0)
          {
            using var b = new SolidBrush(_FillColor);
            e.Graphics.FillRectangle(b, rc);
          }

          rc.Width -= 1;
          rc.Height -= 1;

          if (rc.Width > 0 && rc.Height > 0)
          {
            using var pen = new Pen(_BorderColor, 2.0f);
            e.Graphics.DrawRectangle(pen, rc);
          }

          return;
        }

        if (_Mode == PreviewMode.InsertLine)
        {
          using var pen = new Pen(_BorderColor, 2.0f);
          e.Graphics.DrawLine(pen, _LineP0, _LineP1);
          return;
        }
      }

      protected override void WndProc(ref Message m)
      {
        const int WM_NCHITTEST = 0x0084;
        const int HTTRANSPARENT = -1;

        if (m.Msg == WM_NCHITTEST)
        {
          m.Result = (IntPtr)HTTRANSPARENT;
          return;
        }

        base.WndProc(ref m);
      }
    }
  }
}

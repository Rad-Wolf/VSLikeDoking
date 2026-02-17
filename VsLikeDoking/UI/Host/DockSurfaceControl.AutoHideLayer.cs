using System;
using System.Drawing;
using System.Windows.Forms;

using VsLikeDoking.Abstractions;
using VsLikeDoking.Layout.Model;
using VsLikeDoking.Rendering.Theme;
using VsLikeDoking.Utils;

namespace VsLikeDoking.UI.Host
{
  public sealed partial class DockSurfaceControl
  {
    // AutoHide Popup Layer =========================================================================

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

    private IDockContent? TryGetOrEnsureContent(string key)
    {
      if (_Manager is null) return null;
      try { return _Manager.Registry.Get(key) ?? _Manager.Registry.Ensure(key); }
      catch { return null; }
    }

    private void EnsureAutoHidePopupChrome()
    {
      if (_AutoHidePopupChrome is null || _AutoHidePopupChrome.IsDisposed)
      {
        _AutoHidePopupChrome = new AutoHidePopupChromePanel
        {
          Visible = false,
          TabStop = false,
        };
      }

      if (!ReferenceEquals(_AutoHidePopupChrome.Parent, this))
        Controls.Add(_AutoHidePopupChrome);

      UpdateAutoHideChromeTheme();
    }

    private void EnsureAutoHideResizeGrip()
    {
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

      if (!ReferenceEquals(_AutoHideResizeGrip.Parent, this))
        Controls.Add(_AutoHideResizeGrip);
    }

    private void DestroyAutoHidePopupLayer()
    {
      CancelAutoHideResizeDrag();

      if (_AutoHideResizeGrip is not null)
      {
        try
        {
          _AutoHideResizeGrip.MouseDown -= OnAutoHideResizeGripMouseDown;
          _AutoHideResizeGrip.MouseMove -= OnAutoHideResizeGripMouseMove;
          _AutoHideResizeGrip.MouseUp -= OnAutoHideResizeGripMouseUp;
          _AutoHideResizeGrip.MouseCaptureChanged -= OnAutoHideResizeGripCaptureChanged;
        }
        catch { }

        try { if (!_AutoHideResizeGrip.IsDisposed) _AutoHideResizeGrip.Dispose(); } catch { }
      }

      if (_AutoHidePopupChrome is not null)
      {
        try { _AutoHidePopupChrome.Theme = null; } catch { }
        try { if (!_AutoHidePopupChrome.IsDisposed) _AutoHidePopupChrome.Dispose(); } catch { }
      }

      // 뷰는 Registry 소유이므로 Dispose 금지. Parent만 정리.
      if (_AutoHidePopupView is not null && !_AutoHidePopupView.IsDisposed)
      {
        try
        {
          if (ReferenceEquals(_AutoHidePopupView.Parent, this))
            Controls.Remove(_AutoHidePopupView);
        }
        catch { }
      }

      _AutoHidePopupChrome = null;
      _AutoHideResizeGrip = null;

      _AutoHidePopupView = null;
      _AutoHidePopupKey = null;

      _AutoHidePopupOuterBounds = Rectangle.Empty;
      _AutoHidePopupInnerBounds = Rectangle.Empty;

      _AutoHideResizeDragging = false;
      _AutoHideResizeKey = null;
    }

    private void PresentAutoHidePopupLayer(Rectangle bounds)
    {
      TraceAutoHide("PresentAutoHidePopupLayer", "enter");

      if (_Manager is null || _Root is null)
      {
        HideAutoHidePopupLayer(removeView: true);
        return;
      }

      if (!_Manager.IsAutoHidePopupVisible)
      {
        _ConsumeFirstDismissAfterAutoHideActivate = false;
        HideAutoHidePopupLayer(removeView: false);
        return;
      }

      var key = NormalizeAutoHideKey(_Manager.ActiveAutoHideKey);
      if (key is null)
      {
        HideAutoHidePopupLayer(removeView: false);
        return;
      }

      DockAutoHideSide side;
      Size? popupSize;

      // VisualTree 메타 실패해도 캐시 side + 기본 size로 계속 띄운다.
      if (!TryFindAutoHidePopupInfoByVisualTree(key, out side, out popupSize))
      {
        side = GetCachedAutoHideSideOrDefault(key);
        popupSize = null;
      }

      // UI 캐시 우선
      if (_AutoHidePopupSizeCache.TryGetValue(key, out var cached))
        popupSize = cached;

      var content = TryGetOrEnsureContent(key);
      if (content is null)
      {
        HideAutoHidePopupLayer(removeView: true);
        return;
      }

      var view = content.View;
      if (view is null || view.IsDisposed)
      {
        HideAutoHidePopupLayer(removeView: true);
        return;
      }

      EnsureAutoHidePopupChrome();
      EnsureAutoHideResizeGrip();

      _AutoHidePopupKey = key;
      _AutoHidePopupView = view;

      if (!EnsureAutoHidePopupViewAttachedToSurface(view))
      {
        // 폭주 감지로 popup 강제 종료된 케이스
        return;
      }

      UpdateAutoHidePopupLayerLayoutCore(bounds, side, popupSize);

      try
      {
        if (_AutoHidePopupChrome is not null)
        {
          _AutoHidePopupChrome.Visible = true;
          _AutoHidePopupChrome.BringToFront();
        }

        view.Visible = true;
        view.BringToFront();

        if (_AutoHideResizeGrip is not null)
        {
          _AutoHideResizeGrip.Visible = true;
          _AutoHideResizeGrip.BringToFront();
        }
      }
      catch { }
    }

    private void HideAutoHidePopupLayer(bool removeView)
    {
      TraceAutoHide("HideAutoHidePopupLayer", $"removeView={removeView}");

      CancelAutoHideResizeDrag();

      if (_AutoHideResizeGrip is not null && !_AutoHideResizeGrip.IsDisposed)
      {
        try { _AutoHideResizeGrip.Visible = false; } catch { }
      }

      if (_AutoHidePopupChrome is not null && !_AutoHidePopupChrome.IsDisposed)
      {
        try { _AutoHidePopupChrome.Visible = false; } catch { }
      }

      if (_AutoHidePopupView is not null && !_AutoHidePopupView.IsDisposed)
      {
        try { _AutoHidePopupView.Visible = false; } catch { }

        if (removeView)
        {
          try
          {
            if (ReferenceEquals(_AutoHidePopupView.Parent, this))
              Controls.Remove(_AutoHidePopupView);
          }
          catch { }

          _AutoHidePopupView = null;
          _AutoHidePopupKey = null;
          _AutoHideResizeKey = null;
        }
      }

      _AutoHidePopupOuterBounds = Rectangle.Empty;
      _AutoHidePopupInnerBounds = Rectangle.Empty;
    }

    private void UpdateAutoHidePopupLayerLayout(Rectangle bounds)
    {
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

      if (_AutoHidePopupSizeCache.TryGetValue(key, out var cached))
        popupSize = cached;

      UpdateAutoHidePopupLayerLayoutCore(bounds, side, popupSize);
    }

    private void UpdateAutoHidePopupLayerLayoutCore(Rectangle bounds, DockAutoHideSide side, Size? popupSize)
    {
      if (_AutoHidePopupView is null || _AutoHidePopupView.IsDisposed) return;
      if (bounds.Width <= 0 || bounds.Height <= 0) return;

      var edge = MapAutoHideSideToEdge(side);
      var stripThickness = GetAutoHideStripThickness(edge);

      var rcOuter = ComputeAutoHidePopupRect(bounds, side, stripThickness, popupSize);
      if (rcOuter.Width <= 0 || rcOuter.Height <= 0)
      {
        HideAutoHidePopupLayer(removeView: false);
        return;
      }

      _AutoHidePopupOuterBounds = rcOuter;
      _AutoHidePopupInnerBounds = ComputeAutoHideInnerRect(rcOuter, side);

      try
      {
        if (_AutoHidePopupChrome is not null && !_AutoHidePopupChrome.IsDisposed)
        {
          if (_AutoHidePopupChrome.Bounds != rcOuter)
            _AutoHidePopupChrome.Bounds = rcOuter;
        }
      }
      catch { }

      try
      {
        if (_AutoHidePopupView.Bounds != _AutoHidePopupInnerBounds)
          _AutoHidePopupView.Bounds = _AutoHidePopupInnerBounds;
      }
      catch { }

      UpdateAutoHideResizeGripLayout(side);
    }

    private static Rectangle ComputeAutoHideInnerRect(Rectangle outer, DockAutoHideSide side)
    {
      var left = outer.X + AutoHidePopupContentPadding;
      var top = outer.Y + AutoHidePopupContentPadding;
      var right = outer.Right - AutoHidePopupContentPadding;
      var bottom = outer.Bottom - AutoHidePopupContentPadding;

      if (side == DockAutoHideSide.Left) right -= AutoHideResizeGripThickness;
      else if (side == DockAutoHideSide.Right) left += AutoHideResizeGripThickness;
      else if (side == DockAutoHideSide.Top) bottom -= AutoHideResizeGripThickness;
      else top += AutoHideResizeGripThickness;

      var w = Math.Max(0, right - left);
      var h = Math.Max(0, bottom - top);

      return new Rectangle(left, top, w, h);
    }

    private void UpdateAutoHideResizeGripLayout(DockAutoHideSide side)
    {
      EnsureAutoHideResizeGrip();
      if (_AutoHideResizeGrip is null || _AutoHideResizeGrip.IsDisposed) return;

      if (_AutoHidePopupOuterBounds.IsEmpty || (_AutoHidePopupView is null || _AutoHidePopupView.IsDisposed))
      {
        try { _AutoHideResizeGrip.Visible = false; } catch { }
        return;
      }

      Rectangle gripRc;

      if (side == DockAutoHideSide.Left)
      {
        gripRc = new Rectangle(_AutoHidePopupOuterBounds.Right - AutoHideResizeGripThickness, _AutoHidePopupOuterBounds.Y, AutoHideResizeGripThickness, _AutoHidePopupOuterBounds.Height);
        _AutoHideResizeGrip.Cursor = Cursors.SizeWE;
      }
      else if (side == DockAutoHideSide.Right)
      {
        gripRc = new Rectangle(_AutoHidePopupOuterBounds.X, _AutoHidePopupOuterBounds.Y, AutoHideResizeGripThickness, _AutoHidePopupOuterBounds.Height);
        _AutoHideResizeGrip.Cursor = Cursors.SizeWE;
      }
      else if (side == DockAutoHideSide.Top)
      {
        gripRc = new Rectangle(_AutoHidePopupOuterBounds.X, _AutoHidePopupOuterBounds.Bottom - AutoHideResizeGripThickness, _AutoHidePopupOuterBounds.Width, AutoHideResizeGripThickness);
        _AutoHideResizeGrip.Cursor = Cursors.SizeNS;
      }
      else
      {
        gripRc = new Rectangle(_AutoHidePopupOuterBounds.X, _AutoHidePopupOuterBounds.Y, _AutoHidePopupOuterBounds.Width, AutoHideResizeGripThickness);
        _AutoHideResizeGrip.Cursor = Cursors.SizeNS;
      }

      try
      {
        if (_AutoHideResizeGrip.Bounds != gripRc)
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
      if (_Manager is null) return;
      if (!_Manager.IsAutoHidePopupVisible) return;

      var key = NormalizeAutoHideKey(_Manager.ActiveAutoHideKey);
      if (key is null) return;

      if (!TryFindAutoHidePopupInfoByVisualTree(key, out var side, out var _))
        side = GetCachedAutoHideSideOrDefault(key);

      _AutoHideResizeKey = key;
      _AutoHideResizeSide = side;

      var ptScreen = (_AutoHideResizeGrip is not null)
        ? _AutoHideResizeGrip.PointToScreen(e.Location)
        : PointToScreen(e.Location);

      _AutoHideResizeDragging = true;
      _AutoHideResizeDownScreen = ptScreen;
      _AutoHideResizeStartSize = _AutoHidePopupOuterBounds.IsEmpty ? Size.Empty : _AutoHidePopupOuterBounds.Size;

      if (_AutoHideResizeGrip is not null && !_AutoHideResizeGrip.IsDisposed)
      {
        try { _AutoHideResizeGrip.Capture = true; } catch { }
      }
    }

    private void OnAutoHideResizeGripMouseMove(object? sender, MouseEventArgs e)
    {
      if (!_AutoHideResizeDragging) return;
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
        case DockAutoHideSide.Left: reqW = _AutoHideResizeStartSize.Width + dx; break;
        case DockAutoHideSide.Right: reqW = _AutoHideResizeStartSize.Width - dx; break;
        case DockAutoHideSide.Top: reqH = _AutoHideResizeStartSize.Height + dy; break;
        default: reqH = _AutoHideResizeStartSize.Height - dy; break;
      }

      var bounds = ClientRectangle;

      var edge = MapAutoHideSideToEdge(_AutoHideResizeSide);
      var stripThickness = GetAutoHideStripThickness(edge);

      var clamped = ClampAutoHidePopupSize(bounds, _AutoHideResizeSide, stripThickness, new Size(reqW, reqH));

      _AutoHidePopupSizeCache[key] = clamped;

      UpdateAutoHidePopupLayerLayoutCore(bounds, _AutoHideResizeSide, clamped);

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

    private bool EnsureAutoHidePopupViewAttachedToSurface(Control view)
    {
      if (view is null || view.IsDisposed) return false;

      // 이미 붙어 있으면 OK
      if (ReferenceEquals(view.Parent, this)) return true;

      // 재부착 폭주 감지
      if (RegisterAutoHideRepairBurstAndShouldAbort())
      {
        TraceAutoHide("EnsureAutoHidePopupViewAttachedToSurface", "repair burst -> force close popup");

        // 무한 루프 방지: popup 강제 종료 + 잠깐 재활성화 막기
        _SuppressAutoHideActivateUntilUtc = DateTime.UtcNow.AddMilliseconds(600);

        if (_Manager is not null && _Manager.IsAutoHidePopupVisible)
          _Manager.HideAutoHidePopup("UI:AutoHide:RepairBurst");

        HideAutoHidePopupLayer(removeView: false);
        RequestRender();
        return false;
      }

      try
      {
        try { view.Parent?.Controls.Remove(view); } catch { }
        Controls.Add(view);
        view.Visible = true;
        view.BringToFront();
      }
      catch { }

      return true;
    }

    private bool RegisterAutoHideRepairBurstAndShouldAbort()
    {
      var now = Environment.TickCount64;

      if (_AutoHideRepairBurstStartMs == 0)
      {
        _AutoHideRepairBurstStartMs = now;
        _AutoHideRepairBurstCount = 0;
      }

      // 250ms 윈도우
      if (now - _AutoHideRepairBurstStartMs > 250)
      {
        _AutoHideRepairBurstStartMs = now;
        _AutoHideRepairBurstCount = 0;
      }

      _AutoHideRepairBurstCount++;

      // 짧은 시간에 12회 이상 Parent 재부착이면 "steal 루프"로 판단
      return _AutoHideRepairBurstCount > 12;
    }

    private void UpdateAutoHideChromeTheme()
    {
      if (_AutoHidePopupChrome is null || _AutoHidePopupChrome.IsDisposed) return;

      // Renderer 팔레트가 있으면 그 색을 쓰고, 없으면 안전 기본값
      var border = _Renderer?.Palette[ColorPalette.Role.DockPreviewBorder] ?? SystemColors.Highlight;
      var fill = _Renderer?.Palette[ColorPalette.Role.AppBack] ?? SystemColors.Control;

      try
      {
        _AutoHidePopupChrome.Theme = new AutoHidePopupChromePanel.ChromeTheme(border, fill);
        _AutoHidePopupChrome.Invalidate();
      }
      catch { }
    }


  }
}

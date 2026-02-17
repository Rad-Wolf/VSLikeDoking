using System;
using System.Windows.Forms;

namespace VsLikeDoking.UI.Host
{
  public sealed partial class DockSurfaceControl
  {
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

      // Sender 체인이 어긋난 경우(동적 재부모/Handle 재생성)에도
      // 현재 포인터가 팝업 호스트 영역 안이면 내부 클릭으로 본다.
      if (!_AutoHidePopupOuterBounds.IsEmpty)
      {
        try
        {
          var client = PointToClient(Control.MousePosition);
          if (_AutoHidePopupOuterBounds.Contains(client)) return;
        }
        catch { }
      }

      // 팝업 컨텐츠 내부 클릭은 바깥 클릭이 아니다.
      if (IsFromActiveAutoHidePopupView(c)) return;

      // 바깥 클릭 dismiss는 MouseDown 즉시 처리하지 않고 MouseUp 확정 시점으로 미룬다.
      // (탭 전환/포인터 이동 중 stale dismiss가 끼어드는 경로 차단)
      _PendingExternalOutsideClickDismiss = true;
      TraceAutoHide("OnForwardedMouseDown", "pending external outside-dismiss");
    }

    private void OnForwardedMouseUp(object? sender, MouseEventArgs e)
    {
      if (e.Button != MouseButtons.Left) return;

      if (_PendingExternalOutsideClickDismiss)
      {
        _PendingExternalOutsideClickDismiss = false;
        TraceAutoHide("OnForwardedMouseUp", "consume pending external outside-dismiss");
        HandleDismissAutoHidePopup();
      }

      TryFlushPendingAutoHideDismiss();
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
      c.MouseUp += OnForwardedMouseUp;
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
      try { c.MouseUp -= OnForwardedMouseUp; } catch { }
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
        try { c.MouseUp -= OnForwardedMouseUp; } catch { }
        try { c.KeyDown -= OnForwardedKeyDown; } catch { }
        try { c.Disposed -= OnForwardedDisposed; } catch { }
        try { c.ControlAdded -= OnForwardedControlAdded; } catch { }
        try { c.ControlRemoved -= OnForwardedControlRemoved; } catch { }
      }

      _ForwardedMouseDownHooks.Clear();
    }

    private bool IsFromAutoHidePopupHost(Control source)
    {
      if (_AutoHidePopupChrome is not null)
      {
        var cur = source;
        while (cur is not null)
        {
          if (ReferenceEquals(cur, _AutoHidePopupChrome)) return true;
          cur = cur.Parent;
        }
      }

      if (_AutoHideResizeGrip is not null)
      {
        var cur = source;
        while (cur is not null)
        {
          if (ReferenceEquals(cur, _AutoHideResizeGrip)) return true;
          cur = cur.Parent;
        }
      }

      return false;
    }

    private bool IsFromActiveAutoHidePopupView(Control source)
    {
      if (source is null) return false;

      // (PATCH) Host 내부면 항상 "팝업 내부 클릭"
      if (IsFromAutoHidePopupHost(source)) return true;

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


  }
}

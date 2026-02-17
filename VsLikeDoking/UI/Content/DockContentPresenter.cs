// VsLikeDocking - VsLikeDoking - UI/Content/DockContentPresenter.cs - DockContentPresenter - (File)

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

using VsLikeDoking.Core;
using VsLikeDoking.Layout.Nodes;
using VsLikeDoking.UI.Visual;
using VsLikeDoking.Utils;

namespace VsLikeDoking.UI.Content
{
  /// <summary>ActiveKey 기반으로 IDockContent.View를 배치/재사용하는 최소 Presenter</summary>
  /// <remarks>
  /// DockVisualTree 계산 결과(ContentBounds)를 그대로 사용하며, 레이아웃 변경은 수행하지 않는다.
  /// AutoHide Popup은 DockSurfaceControl이 전담한다(여기서 끄거나 숨기면 “안 보이는데 클릭만 먹는” 문제가 재발한다).
  /// </remarks>
  public sealed class DockContentPresenter : IDisposable
  {
    // Fields =====================================================================================================

    private Control? _Surface;
    private DockManager? _Manager;

    // (Surface 직속으로 붙인 컨텐츠만 관리한다.
    // AutoHide PopupHost(Panel) 아래로 간 뷰는 여기서 건드리지 않는다.)
    private readonly Dictionary<string, Control> _ByKey = new(StringComparer.Ordinal);
    private readonly HashSet<string> _VisibleKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _AutoHideKeys = new(StringComparer.Ordinal);
    private readonly List<string> _RemoveKeys = new();

    private bool _Disposed;

    // Bind/Unbind =================================================================================================

    /// <summary>Surface와 Manager를 바인딩한다.</summary>
    public void Bind(Control surface, DockManager manager)
    {
      Guard.NotNull(surface);
      Guard.NotNull(manager);

      if (ReferenceEquals(_Surface, surface) && ReferenceEquals(_Manager, manager))
        return;

      Unbind();

      _Surface = surface;
      _Manager = manager;
    }

    /// <summary>바인딩을 해제하고, 현재 표시/캐시를 정리한다.</summary>
    public void Unbind()
    {
      Clear(true);

      _Surface = null;
      _Manager = null;
    }

    // Present =====================================================================================================

    /// <summary>VisualTree 결과를 기반으로 컨텐츠 배치를 갱신한다.</summary>
    public void Present(DockVisualTree tree)
    {
      if (_Disposed) return;
      if (_Surface is null) return;
      if (_Manager is null) return;

      Guard.NotNull(tree);

      _VisibleKeys.Clear();
      CollectAutoHideKeys(tree);

      _Surface.SuspendLayout();
      try
      {
        // Dock 그룹(문서/툴)만 배치한다.
        PresentDockGroups(tree);

        // AutoHide Popup은 DockSurfaceControl이 전담한다.
        HideNonVisible(detach: false);
      }
      finally
      {
        _Surface.ResumeLayout(false);
      }
    }

    /// <summary>모든 컨텐츠 표시를 제거한다.</summary>
    public void Clear(bool detach = true)
    {
      if (_Disposed) return;

      _AutoHideKeys.Clear();

      if (_Surface is null)
      {
        _ByKey.Clear();
        _VisibleKeys.Clear();
        return;
      }

      _Surface.SuspendLayout();
      try
      {
        foreach (var kv in _ByKey)
        {
          var c = kv.Value;
          if (c is null || c.IsDisposed) continue;

          // Clear는 “전체 정리”이므로 부모가 어디든 숨김 처리한다.
          if (c.Visible)
            c.Visible = false;

          if (detach && ReferenceEquals(c.Parent, _Surface))
            _Surface.Controls.Remove(c);
        }

        if (detach)
          _ByKey.Clear();

        _VisibleKeys.Clear();
      }
      finally
      {
        _Surface.ResumeLayout(false);
      }
    }

    // Dispose =====================================================================================================

    /// <summary>Presenter 리소스를 정리한다.</summary>
    public void Dispose()
    {
      if (_Disposed) return;

      _Disposed = true;

      try { Unbind(); } catch { }
    }

    // Internals ===================================================================================================

    private void CollectAutoHideKeys(DockVisualTree tree)
    {
      _AutoHideKeys.Clear();

      var tabs = tree.AutoHideTabs;
      for (int i = 0; i < tabs.Count; i++)
      {
        if (tabs[i].ContentKey is not string key)
          continue;

        key = key.Trim();
        if (key.Length == 0)
          continue;

        _AutoHideKeys.Add(key);
      }
    }

    private void PresentDockGroups(DockVisualTree tree)
    {
      if (_Surface is null) return;
      if (_Manager is null) return;

      var groups = tree.Groups;

      for (int gi = 0; gi < groups.Count; gi++)
      {
        var gv = groups[gi];
        var bounds = gv.ContentBounds;
        if (bounds.IsEmpty) continue;

        var key = GetActiveKey(gv.Node);
        if (string.IsNullOrWhiteSpace(key)) continue;

        key = key.Trim();
        if (key.Length == 0) continue;

        // AutoHide 팝업으로 활성화된 키는 Surface 직속 배치 대상이 아니다.
        // (DockSurfaceControl의 PopupHost가 소유)
        if (IsActiveAutoHidePopupKey(key)) continue;

        // Ensure는 "없을 때만" (매 프레임 팩토리 호출 방지)
        var content = _Manager.Registry.Get(key) ?? _Manager.Registry.Ensure(key);
        if (content is null) continue;

        var view = content.View;
        if (view is null || view.IsDisposed) continue;

        AttachIfNeeded(key, view);
        LayoutView(view, bounds);
        _VisibleKeys.Add(key);
      }
    }

    private static string? GetActiveKey(DockGroupNode group)
    {
      var key = group.ActiveKey;
      if (!string.IsNullOrWhiteSpace(key))
        return key;

      var items = group.Items;
      if (items.Count <= 0)
        return null;

      return items[0].PersistKey;
    }

    private void AttachIfNeeded(string key, Control view)
    {
      if (_Surface is null) return;

      if (_ByKey.TryGetValue(key, out var existing))
      {
        if (!ReferenceEquals(existing, view))
        {
          // 기존 캐시가 다른 컨트롤이면: old를 표면에서 제거/숨김 (겹침/유령 컨트롤 방지)
          if (existing is not null && !existing.IsDisposed)
          {
            try { if (existing.Visible) existing.Visible = false; } catch { }
            try { if (ReferenceEquals(existing.Parent, _Surface)) _Surface.Controls.Remove(existing); } catch { }
          }

          _ByKey[key] = view;
        }
      }
      else
      {
        _ByKey.Add(key, view);
      }

      if (!ReferenceEquals(view.Parent, _Surface))
      {
        try
        {
          // 외부에서 다른 부모에 붙어있던 경우 재부모화
          view.Parent?.Controls.Remove(view);
        }
        catch { }

        if (view.Dock != DockStyle.None)
          view.Dock = DockStyle.None;

        _Surface.Controls.Add(view);
      }
    }


    private bool IsActiveAutoHidePopupKey(string key)
    {
      if (_Manager is null) return false;
      if (!_Manager.IsAutoHidePopupVisible) return false;
      if (string.IsNullOrWhiteSpace(_Manager.ActiveAutoHideKey)) return false;

      return string.Equals(_Manager.ActiveAutoHideKey, key, StringComparison.Ordinal);
    }
    private static void LayoutView(Control view, Rectangle bounds)
    {
      // 빈번 호출: 불필요한 SetBounds/Visible 토글 최소화
      if (view.Left != bounds.X || view.Top != bounds.Y || view.Width != bounds.Width || view.Height != bounds.Height)
        view.SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);

      if (!view.Visible)
        view.Visible = true;
    }

    private void HideNonVisible(bool detach)
    {
      if (_Surface is null) return;

      _RemoveKeys.Clear();

      foreach (var kv in _ByKey)
      {
        var key = kv.Key;
        var view = kv.Value;

        if (_VisibleKeys.Contains(key))
        {
          if (view is not null && !view.IsDisposed && ReferenceEquals(view.Parent, _Surface) && !view.Visible)
            view.Visible = true;

          continue;
        }

        // 현재 활성 AutoHide 팝업 키는 Surface 직계 자식으로 유지될 수 있으므로
        // Presenter의 비가시 정리에서 숨기지 않는다.
        if (IsActiveAutoHidePopupKey(key))
        {
          if (view is not null && !view.IsDisposed && ReferenceEquals(view.Parent, _Surface) && !view.Visible)
            view.Visible = true;
          continue;
        }

        // 현재 활성 AutoHide 팝업 키는 Surface 직계 자식으로 유지될 수 있으므로
        // Presenter의 비가시 정리에서 숨기지 않는다.
        if (IsActiveAutoHidePopupKey(key))
        {
          if (view is not null && !view.IsDisposed && ReferenceEquals(view.Parent, _Surface) && !view.Visible)
            view.Visible = true;
          continue;
        }

        // 현재 활성 AutoHide 팝업 키는 Surface 직계 자식으로 유지될 수 있으므로
        // Presenter의 비가시 정리에서 숨기지 않는다.
        if (IsActiveAutoHidePopupKey(key))
        {
          if (view is not null && !view.IsDisposed && ReferenceEquals(view.Parent, _Surface) && !view.Visible)
            view.Visible = true;
          continue;
        }

        if (view is null || view.IsDisposed)
        {
          if (view is not null && !view.IsDisposed && ReferenceEquals(view.Parent, _Surface) && !view.Visible)
            view.Visible = true;
          continue;
        }

        if (view is null || view.IsDisposed)
        {
          try { if (view is not null && ReferenceEquals(view.Parent, _Surface)) _Surface.Controls.Remove(view); } catch { }
          _RemoveKeys.Add(key);
          continue;
        }

        // 핵심 FIX:
        // AutoHide PopupHost(Panel) 아래로 간 뷰는 여기서 숨기지 않는다.
        // (DockSurfaceControl이 전담)
        if (!ReferenceEquals(view.Parent, _Surface))
          continue;

        if (view.Visible)
          view.Visible = false;

        if (detach)
        {
          _Surface.Controls.Remove(view);
          _RemoveKeys.Add(key);
        }
      }

      for (int i = 0; i < _RemoveKeys.Count; i++)
        _ByKey.Remove(_RemoveKeys[i]);
    }
  }
}

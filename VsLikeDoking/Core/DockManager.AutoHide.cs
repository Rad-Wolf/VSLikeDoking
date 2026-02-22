using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;

using VsLikeDoking.Abstractions;
using VsLikeDoking.Layout.Model;
using VsLikeDoking.Layout.Nodes;
using VsLikeDoking.Utils;

namespace VsLikeDoking.Core
{
  public partial class DockManager
  {
    // AutoHide Pin/Unpin ===========================================================================================

    /// <summary>PersistKey 컨텐츠를 AutoHide(핀)로 보낸다.</summary>
    /// <remarks>
    /// - 그룹에 있는 컨텐츠만 Pin 가능.
    /// - Pin 후 showPopup=true면 즉시 팝업 표시를 시도한다.
    /// </remarks>
    public bool PinToAutoHide(string persistKey, DockAutoHideSide side, Size? popupSize = null, bool showPopup = false, string? reason = null)
    {
      Guard.NotNullOrWhiteSpace(persistKey);
      ThrowIfDisposed();

      var key = persistKey.Trim();

      if (IsDocumentKey(key)) return false;

      var content = Registry.Get(key);
      if (content is IDockToolOptions opt && !opt.CanHide) return false;

      var wasActive = string.Equals(_ActiveContent?.PersistKey, key, StringComparison.Ordinal);

      TraceAutoHide("PinToAutoHide", $"key={key}, side={side}, showPopup={showPopup}, reason={reason}");

      var next = DockMutator.PinToAutoHide(_Root, key, side, out var didChange, popupSize);
      if (!didChange) return false;

      ApplyLayout(next, reason ?? $"AutoHide:Pin:{key}:{side}");

      if (showPopup)
      {
        _ = ShowAutoHidePopup(key, reason ?? $"AutoHide:ShowAfterPin:{key}");
      }
      else
      {
        // (PATCH) VS 느낌: 핀으로 접히면 focus는 마지막 그룹 활성로 복귀(절대 null로 떨어뜨리지 않음)
        if (wasActive)
        {
          var fallback = SelectFallbackGroupActiveKey(preferDocument: true);
          SetActiveContentDirect(fallback, updateLastNonAutoHide: true);
        }
      }

      return true;
    }

    /// <summary>PersistKey 컨텐츠를 AutoHide에서 다시 그룹으로 되돌린다(Unpin).</summary>
    /// <remarks>
    /// - targetGroupNodeId가 null이면 같은 Kind의 첫 그룹으로 들어간다.
    /// - ToolWindow인데 대상 그룹이 없으면 ToolArea를 생성하고 그 그룹으로 복귀한다.
    /// </remarks>
    public bool UnpinFromAutoHide(string persistKey, string? targetGroupNodeId = null, bool makeActive = true, string? reason = null)
    {
      Guard.NotNullOrWhiteSpace(persistKey);
      ThrowIfDisposed();

      var key = persistKey.Trim();

      var workingRoot = _Root;
      var targetId = targetGroupNodeId;

      // ToolWindow Unpin 시 대상 그룹이 없으면 기본 Right Tool 그룹을 선보장한다.
      if (IsToolKey(key) && string.IsNullOrWhiteSpace(targetId))
      {
        workingRoot = DockMutator.EnsureToolArea(workingRoot, out var toolGroup, DockToolAreaPlacement.Right, DockDefaults.DefaultToolOntoDocumentNewPaneRatio);
        targetId = toolGroup.NodeId;
      }

      var next = DockMutator.UnpinFromAutoHide(workingRoot, key, out var didChange, targetId, makeActive);
      if (!didChange) return false;

      ApplyLayout(next, reason ?? $"AutoHide:Unpin:{key}");

      if (makeActive)
        SetActiveContent(key);

      return true;
    }

    /// <summary>현재 상태에 따라 Pin/Unpin을 토글한다.</summary>
    /// <remarks>그룹에 있으면 Pin, AutoHide에 있으면 Unpin 시도.</remarks>
    public bool TogglePinAutoHide(string persistKey, DockAutoHideSide side, string? targetGroupNodeId = null, bool showPopupWhenPinned = false, string? reason = null)
    {
      Guard.NotNullOrWhiteSpace(persistKey);
      ThrowIfDisposed();

      var key = persistKey.Trim();

      if (TryFindGroupContainingKey(_Root, key, out _))
        return PinToAutoHide(key, side, popupSize: null, showPopup: showPopupWhenPinned, reason: reason);

      if (TryFindAutoHideContainingKey(_Root, key, out _))
        return UnpinFromAutoHide(key, targetGroupNodeId, makeActive: true, reason: reason);

      return false;
    }


    // Active =======================================================================================================

    /// <summary>UI가 활성 컨텐츠가 바뀌었음을 DockManager에 알린다</summary>
    /// <remarks>ActiveContentChanged 이벤트 발생</remarks>
    public void SetActiveContent(string? persistKey)
    {
      ThrowIfDisposed();

      var key = NormalizeKey(persistKey);

      // (PATCH) null/빈 입력은 "null 확정"이 아니라 "합리적 활성 재선정"으로 처리한다.
      if (key is null)
      {
        if (!string.IsNullOrWhiteSpace(_ActiveAutoHideKey))
          SetAutoHidePopupKeyCore(null, "AutoHide:HideOnActiveContent(null)");

        var fallback = SelectFallbackGroupActiveKey(preferDocument: true);
        SetActiveContentDirect(fallback, updateLastNonAutoHide: true);
        return;
      }

      // AutoHide 키면: "표시(팝업) 토글" 정책을 이곳에서 처리한다.
      // - 그룹에 존재하면 AutoHide 토글 대상이 아니다(일반 활성 전환)
      var isInGroup = TryFindGroupContainingKey(_Root, key, out _);
      if (!isInGroup && TryFindAutoHideContainingKey(_Root, key, out _))
      {
        // 같은 AutoHide 탭을 다시 클릭하면 숨김(토글 off) + 마지막 그룹 활성로 복귀(절대 null 금지)
        if (!string.IsNullOrWhiteSpace(_ActiveAutoHideKey)
          && string.Equals(_ActiveAutoHideKey, key, StringComparison.Ordinal))
        {
          SetAutoHidePopupKeyCore(null, $"AutoHide:Hide:{key}");

          var fallback = SelectFallbackGroupActiveKey(preferDocument: true);
          SetActiveContentDirect(fallback, updateLastNonAutoHide: true);
          return;
        }

        // AutoHide 표시(토글 on) + ActiveContent는 해당 키로 맞춘다(단, NonAutoHide 캐시는 갱신하지 않음)
        SetAutoHidePopupKeyCore(key, $"AutoHide:Show:{key}");
        SetActiveContentDirect(key, updateLastNonAutoHide: false);
        return;
      }

      // 일반 컨텐츠를 활성화하면 AutoHide 팝업은 숨긴다.
      if (!string.IsNullOrWhiteSpace(_ActiveAutoHideKey))
        SetAutoHidePopupKeyCore(null, "AutoHide:HideOnActiveContent");

      SetActiveContentDirect(key, updateLastNonAutoHide: true);
    }

    /// <summary>AutoHide 팝업(슬라이드) 표시를 강제한다. 성공하면 true.</summary>
    public bool ShowAutoHidePopup(string persistKey, string? reason = null)
    {
      Guard.NotNullOrWhiteSpace(persistKey);
      ThrowIfDisposed();

      TraceAutoHide("ShowAutoHidePopup", $"request key={persistKey}, reason={reason}");

      var key = NormalizeKey(persistKey);
      if (key is null) return false;

      // 그룹에 있는 키는 AutoHide 팝업 대상이 아님
      if (TryFindGroupContainingKey(_Root, key, out _)) return false;
      if (!TryFindAutoHideContainingKey(_Root, key, out _)) return false;

      SetAutoHidePopupKeyCore(key, reason ?? $"AutoHide:Show:{key}");
      SetActiveContentDirect(key, updateLastNonAutoHide: false);

      TraceAutoHide("ShowAutoHidePopup", $"applied key={key}");

      return true;
    }

    /// <summary>AutoHide 팝업(슬라이드)을 숨긴다.</summary>
    public void HideAutoHidePopup(string? reason = null)
    {
      ThrowIfDisposed();

      TraceAutoHide("HideAutoHidePopup", $"reason={reason}");

      var oldKey = _ActiveAutoHideKey;
      if (string.IsNullOrWhiteSpace(oldKey)) return;

      SetAutoHidePopupKeyCore(null, reason ?? $"AutoHide:Hide:{oldKey}");

      // (PATCH) 숨김 후 ActiveContent가 AutoHide 키였으면 마지막 그룹 활성로 복귀
      if (string.Equals(_ActiveContent?.PersistKey, oldKey, StringComparison.Ordinal))
      {
        var fallback = SelectFallbackGroupActiveKey(preferDocument: true);
        SetActiveContentDirect(fallback, updateLastNonAutoHide: true);
      }
    }

    private void TraceAutoHide(string stage, string detail)
    {
      if (!AutoHideTraceEnabled) return;
      var line = $"[AH][Manager][{DateTime.Now:HH:mm:ss.fff}] {stage} | {detail} | activeAh={_ActiveAutoHideKey ?? "(null)"}, activeContent={_ActiveContent?.PersistKey ?? "(null)"}";

      Debug.WriteLine(line);
      Trace.WriteLine(line);

      try { File.AppendAllText(AutoHideTraceFilePath, line + Environment.NewLine); }
      catch { }
    }

    /// <summary>AutoHide 팝업(슬라이드)을 토글한다. 성공하면 true.</summary>
    public bool ToggleAutoHidePopup(string persistKey, string? reason = null)
    {
      Guard.NotNullOrWhiteSpace(persistKey);
      ThrowIfDisposed();

      var key = NormalizeKey(persistKey);
      if (key is null) return false;

      if (!string.IsNullOrWhiteSpace(_ActiveAutoHideKey)
        && string.Equals(_ActiveAutoHideKey, key, StringComparison.Ordinal))
      {
        HideAutoHidePopup(reason ?? $"AutoHide:ToggleOff:{key}");
        return true;
      }

      return ShowAutoHidePopup(key, reason ?? $"AutoHide:ToggleOn:{key}");
    }


    private bool SetAutoHidePopupKeyCore(string? persistKey, string? reason)
    {
      var key = NormalizeKey(persistKey);

      TraceAutoHide("SetAutoHidePopupKeyCore", $"next={key ?? "(null)"}, reason={reason}");

      if (string.Equals(_ActiveAutoHideKey, key, StringComparison.Ordinal))
      {
        // 레이아웃이 교체/로드된 경우를 대비해 상태 동기화는 항상 수행
        SyncAutoHidePopupStateToLayout(ref _ActiveAutoHideKey, _Root);
        return false;
      }

      _ActiveAutoHideKey = key;

      SyncAutoHidePopupStateToLayout(ref _ActiveAutoHideKey, _Root);

      Events.RaiseLayoutChanged(_Root, _Root, reason ?? (key is null ? "AutoHide:Hide" : $"AutoHide:Show:{key}"));
      TraceAutoHide("SetAutoHidePopupKeyCore", $"applied={_ActiveAutoHideKey ?? "(null)"}");
      return true;
    }


  }
}

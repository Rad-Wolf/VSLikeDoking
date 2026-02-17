// VsLikeDocking - VsLikeDoking - Core/DockManager.cs - DockManager - (File)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;

using VsLikeDoking.Abstractions;
using VsLikeDoking.Layout.Model;
using VsLikeDoking.Layout.Nodes;
using VsLikeDoking.Layout.Persistence;
using VsLikeDoking.Utils;

namespace VsLikeDoking.Core
{
  /// <summary>현재 레이아웃 루트(DockNode)를 보유하고, DockMutator로 트리를 변경한뒤 DockValidator로 정리하고, 저장/복원(Serializer/Json/Versioning)과 컨텐츠 생성/상태 로드(IDockContentFatory/IDockPersistable)를 묶어서 운용한다.</summary>
  public partial class DockManager : IDisposable
  {
    private const bool AutoHideTraceEnabled = true;
    private static readonly string AutoHideTraceFilePath = Path.Combine(Path.GetTempPath(), "VsLikeDoking-autohide-trace.log");
    // Fields =====================================================================================================

    private DockNode _Root;
    private IDockContent? _ActiveContent;

    private string? _ActiveAutoHideKey;

    // (PATCH) ActiveContent가 null로 떨어지는 케이스 방지용 "마지막 활성" 캐시
    private string? _LastActiveKey;
    private string? _LastNonAutoHideKey;

    private bool _Disposed;

    // Properties =================================================================================================

    /// <summary>도킹 시스템 설정</summary>
    public DockSettings Settings { get; }

    /// <summary>도킹 시스템 이벤트 허브</summary>
    public DockEvents Events { get; }

    /// <summary>PersistKey ↔ IDockContent 레지스트리</summary>
    public DockRegistry Registry { get; }

    /// <summary>현재 레이아웃 루트 노드.</summary>
    public DockNode Root => _Root;

    /// <summary>현재 활성 컨텐츠(선택). UI가 활성 변경 시 SetActiveContent로 알려줘야한다.</summary>
    public IDockContent? ActiveContent => _ActiveContent;

    /// <summary>현재 활성 컨텐츠의 PersistKey(없으면 null)</summary>
    public string? ActiveKey => _ActiveContent?.PersistKey;

    /// <summary>현재 활성 컨텐츠의 PersistKey(없으면 null)</summary>
    public string? ActivePersistKey => _ActiveContent?.PersistKey;

    /// <summary>현재 표시(팝업/슬라이드) 중인 AutoHide 컨텐츠 키(없으면 null)</summary>
    public string? ActiveAutoHideKey => _ActiveAutoHideKey;

    /// <summary>AutoHide 팝업이 표시 중인지 여부</summary>
    public bool IsAutoHidePopupVisible => !string.IsNullOrWhiteSpace(_ActiveAutoHideKey);

    // Ctor =======================================================================================================

    /// <summary>DockManager 생성 (기본 설정/기본 레이아웃)</summary>
    public DockManager() : this(null, null) { }

    /// <summary>DockManager 생성</summary>
    public DockManager(IDockContentFactory? factory = null, DockSettings? settings = null)
    {
      Settings = (settings ?? DockSettings.Default).Normalize();
      Events = new DockEvents();
      Registry = new DockRegistry(factory, Events);

      _Root = DockDefaults.CreateDefaultLayout();
      if (Settings.ValidateLayoutOnApply) _Root = DockValidator.ValidateAndFix(_Root);
      else _Root.SetParentInternal(null);

      _ActiveAutoHideKey = null;

      _LastActiveKey = null;
      _LastNonAutoHideKey = null;
    }

    // Layout Apply =================================================================================================

    /// <summary>레이아웃 루트를 교체한다</summary>
    public void ApplyLayout(DockNode newRoot)
      => ApplyLayout(newRoot, null, null);

    /// <summary>레이아웃 루트를 교체한다</summary>
    public void ApplyLayout(DockNode newRoot, string? reason)
      => ApplyLayout(newRoot, reason, null);

    /// <summary>레이아웃 루트를 교체한다</summary>
    /// <remarks>옵션 : Validate/정리 후 LayoutChanged 이벤트 발생</remarks>
    public void ApplyLayout(DockNode newRoot, string? reason = null, bool? validateOverride = null)
    {
      Guard.NotNull(newRoot);
      ThrowIfDisposed();

      var old = _Root;
      _Root = newRoot;

      var validate = validateOverride ?? Settings.ValidateLayoutOnApply;
      if (validate) _Root = DockValidator.ValidateAndFix(_Root);
      else _Root.SetParentInternal(null);

      // AutoHide 표시 상태가 레이아웃에 남아있지 않도록 항상 동기화한다.
      // - 현재 표시 키가 더 이상 AutoHide에 없으면 자동 해제
      SyncAutoHidePopupStateToLayout(ref _ActiveAutoHideKey, _Root);

      // (PATCH) 레이아웃 변경 후 ActiveContent가 null로 떨어지는 케이스 복구
      EnsureActiveContentAfterLayout();

      Events.RaiseLayoutChanged(old, _Root, reason);
    }

    // Tool Area ====================================================================================================

    /// <summary>ToolWindow 영역(그룹)이 없으면 기본 배치로 생성한다.</summary>
    /// <remarks>이미 존재하면 아무 것도 하지 않는다.</remarks>
    public void EnsureToolArea(DockToolAreaPlacement placement = DockToolAreaPlacement.Right, double toolPaneRatio = DockDefaults.DefaultToolOntoDocumentNewPaneRatio, string? reason = null)
    {
      ThrowIfDisposed();

      toolPaneRatio = NormalizeToolPaneRatio(toolPaneRatio);

      var next = DockMutator.EnsureToolArea(_Root, out _, placement, toolPaneRatio);
      if (ReferenceEquals(next, _Root)) return;

      ApplyLayout(next, reason ?? $"EnsureToolArea:{placement}", validateOverride: null);
    }

    /// <summary>ToolWindow 영역으로 도킹한다. ToolWindow 영역이 없으면 자동으로 생성한다.</summary>
    /// <remarks>ToolArea 생성 비율은 기본 정책(DockDefaults.DefaultToolOntoDocumentNewPaneRatio)을 사용한다.</remarks>
    public void DockToToolArea(string persistKey, DockDropSide side = DockDropSide.Center, double newPaneRatio = 0.0, DockToolAreaPlacement placement = DockToolAreaPlacement.Right, bool makeActive = true, string? reason = null)
      => DockToToolArea(persistKey, side, newPaneRatio, placement, DockDefaults.DefaultToolOntoDocumentNewPaneRatio, makeActive, reason);

    /// <summary>ToolWindow 영역으로 도킹한다. ToolWindow 영역이 없으면 지정 비율로 자동 생성한다.</summary>
    public void DockToToolArea(string persistKey, DockDropSide side, double newPaneRatio, DockToolAreaPlacement placement, double toolPaneRatio, bool makeActive = true, string? reason = null)
    {
      Guard.NotNullOrWhiteSpace(persistKey);
      ThrowIfDisposed();

      var key = persistKey.Trim();

      toolPaneRatio = NormalizeToolPaneRatio(toolPaneRatio);

      var ensured = DockMutator.EnsureToolArea(_Root, out var toolGroup, placement, toolPaneRatio);
      var targetId = toolGroup.NodeId;

      var next = DockCore(ensured, key, targetId, side, newPaneRatio, makeActive, out var didChange, out var autoReason);
      if (!didChange) return;

      ApplyLayout(next, reason ?? autoReason ?? $"DockToToolArea:{key}");
    }

    // Dock Operations ==============================================================================================

    /// <summary>PersistKey 컨텐츠를 대상 그룹(NodeId)으로 도킹한다.</summary>
    /// <remarks>
    /// - Center면 탭으로 합친다(단, Document↔ToolWindow cross-kind는 금지).
    /// - L/R/T/B면 분할하여 새 그룹을 만든다(cross-kind여도 컨텐츠 Kind를 유지하며 대상 그룹 Kind는 변경하지 않는다).
    /// - newPaneRatio가 0 또는 NaN이면 DockDefaults 정책 비율을 사용한다.
    /// </remarks>
    public void Dock(string persistKey, string targetGroupNodeId, DockDropSide side = DockDropSide.Center, double newPaneRatio = 0.0, bool makeActive = true, string? reason = null)
    {
      Guard.NotNullOrWhiteSpace(persistKey);
      Guard.NotNullOrWhiteSpace(targetGroupNodeId);
      ThrowIfDisposed();

      var key = persistKey.Trim();
      var targetId = targetGroupNodeId.Trim();

      var next = DockCore(_Root, key, targetId, side, newPaneRatio, makeActive, out var didChange, out var autoReason);
      if (!didChange) return;

      ApplyLayout(next, reason ?? autoReason ?? $"Dock:{key}");
    }

    /// <summary>PersistKey 컨텐츠(탭)를 대상 그룹(NodeId)의 특정 위치로 이동한다.</summary>
    /// <remarks>
    /// - 같은 그룹이면 재정렬만 한다.
    /// - 다른 그룹이면 레이아웃에서 key를 제거한 뒤 Center로 다시 도킹하고, 마지막에 insertIndex로 재정렬한다.
    /// - 문서/툴윈도우 그룹 Kind가 다르면 false(정책: 서로 섞지 않음).
    /// </remarks>
    public bool MoveTab(string persistKey, string targetGroupNodeId, int insertIndex, bool makeActive = true, string? reason = null)
    {
      Guard.NotNullOrWhiteSpace(persistKey);
      Guard.NotNullOrWhiteSpace(targetGroupNodeId);
      ThrowIfDisposed();

      var key = persistKey.Trim();
      var targetId = targetGroupNodeId.Trim();

      if (!TryFindGroupContainingKey(_Root, key, out var sourceGroup)) return false;

      var targetGroup = DockMutator.FindByNodeId(_Root, targetId) as DockGroupNode;
      if (targetGroup is null) return false;

      if (sourceGroup.ContentKind != targetGroup.ContentKind) return false;

      // 같은 그룹이면 Close/Dock 없이 재정렬만 수행(가장 안전/가벼움)
      if (string.Equals(sourceGroup.NodeId, targetId, StringComparison.Ordinal))
      {
        if (!TryReorderTabInGroup(_Root, targetId, key, insertIndex)) return false;

        if (makeActive) sourceGroup.SetActive(key);
        ApplyLayout(_Root, reason ?? $"ReorderTab:{key}");
        return true;
      }

      var state = TryGetLayoutItemState(sourceGroup, key) ?? TryGetContentState(key);

      var r1 = DockMutator.CloseContent(_Root, key);
      var r2 = DockMutator.DockToGroup(r1, key, state, targetId, DockDropSide.Center, 0.5, makeActive);

      // 여기서 reorder가 실패해도 "끝에 붙는" 동작은 유지된다.
      TryReorderTabInGroup(r2, targetId, key, insertIndex);

      ApplyLayout(r2, reason ?? $"MoveTab:{key}");
      return true;
    }

    /// <summary>PersistKey 컨텐츠를 레이아웃에서 제거(닫기)한다.</summary>
    /// <remarks>
    /// - 레지스트리에 컨텐츠가 존재하고 CanClose=false면: 레이아웃/레지스트리 모두 변경하지 않는다.
    /// - 레지스트리에 컨텐츠가 없고 레이아웃에만 남아있다면: 레이아웃 정리만 수행한다(고아 탭 제거).
    /// </remarks>
    public bool CloseContent(string persistKey, string? reason = null)
    {
      Guard.NotNullOrWhiteSpace(persistKey);
      ThrowIfDisposed();

      var key = persistKey.Trim();

      var wasInLayout = IsKeyInLayout(_Root, key);

      // 1) CanClose 정책(등록된 컨텐츠 기준)
      var content = Registry.Get(key);
      if (content is not null && !content.CanClose)
        return false;

      // 2) 레지스트리 Close (등록된 컨텐츠가 있을 때만 시도)
      var hadRegistered = content is not null;
      var closedRegistry = !hadRegistered || Registry.Close(key);

      // 등록된 컨텐츠가 있었는데 Close 실패면 아무것도 하지 않는다.
      if (hadRegistered && !closedRegistry)
        return false;

      if (closedRegistry)
      {
        ClearActiveContentIfMatches(key);

        // AutoHide 팝업이 이 키를 보고 있으면 먼저 해제한다(레이아웃 변경이 없을 수도 있으므로).
        var clearedAutoHide = ClearAutoHidePopupIfMatches(key);
        if (clearedAutoHide && !wasInLayout)
          Events.RaiseLayoutChanged(_Root, _Root, reason ?? $"AutoHide:Close:{key}");
      }

      // 3) 레이아웃에서 제거 (있을 때만 ApplyLayout)
      if (wasInLayout)
      {
        var nextRoot = DockMutator.CloseContent(_Root, key);
        ApplyLayout(nextRoot, reason ?? $"Close:{key}");
        return true;
      }

      // 레이아웃에는 없었지만 레지스트리는 닫혔으면 성공
      return closedRegistry;
    }

    /// <summary>그룹(NodeId)의 컨텐츠들을 닫는다. CanClose=false는 건너뛴다.</summary>
    /// <remarks>여러 탭을 닫아도 ApplyLayout은 1회만 호출한다(배치).</remarks>
    /// <returns>실제로 닫힌 컨텐츠 개수</returns>
    public int CloseGroupContents(string groupNodeId, string? reason = null)
    {
      Guard.NotNullOrWhiteSpace(groupNodeId);
      ThrowIfDisposed();

      var id = groupNodeId.Trim();
      var group = DockMutator.FindByNodeId(_Root, id) as DockGroupNode;
      if (group is null) return 0;

      if (group.Items.Count == 0) return 0;

      var keys = new List<string>(group.Items.Count);
      for (int i = 0; i < group.Items.Count; i++)
      {
        var k = group.Items[i].PersistKey;
        if (string.IsNullOrWhiteSpace(k)) continue;

        k = k.Trim();
        if (k.Length == 0) continue;

        keys.Add(k);
      }

      if (keys.Count == 0) return 0;

      var closedKeys = new List<string>(keys.Count);
      var unique = new HashSet<string>(StringComparer.Ordinal);

      // 1) 레지스트리 Close(가능한 것만)
      for (int i = 0; i < keys.Count; i++)
      {
        var key = keys[i];
        if (!unique.Add(key)) continue;

        var c = Registry.Get(key);
        if (c is not null && !c.CanClose) continue;

        var hadRegistered = c is not null;
        var closedRegistry = !hadRegistered || Registry.Close(key);
        if (hadRegistered && !closedRegistry) continue;

        if (closedRegistry)
          ClearActiveContentIfMatches(key);

        // AutoHide 팝업이 이 키를 보고 있으면 해제(ApplyLayout 전에 상태 정리)
        ClearAutoHidePopupIfMatches(key);

        closedKeys.Add(key);
      }

      if (closedKeys.Count == 0) return 0;

      // 2) 레이아웃에서 제거(닫힌 것만) + ApplyLayout 1회
      var root = _Root;
      for (int i = 0; i < closedKeys.Count; i++)
        root = DockMutator.CloseContent(root, closedKeys[i]);

      ApplyLayout(root, reason ?? $"CloseGroup:{id}");
      return closedKeys.Count;
    }

    /// <summary>그룹(NodeID)의 활성 탭을 변경한다. 성공하면 true.</summary>
    public bool SetGroupActive(string groupNodeId, string persistKey, string? reason = null)
    {
      Guard.NotNullOrWhiteSpace(groupNodeId);
      Guard.NotNullOrWhiteSpace(persistKey);
      ThrowIfDisposed();

      var g = DockMutator.FindByNodeId(_Root, groupNodeId.Trim()) as DockGroupNode;
      if (g is null) return false;
      if (!g.SetActive(persistKey.Trim())) return false;

      Events.RaiseLayoutChanged(_Root, _Root, reason ?? $"ActiveTab:{persistKey}");
      return true;
    }

    // Save/Load =====================================================================================================

    /// <summary>현재 레이아웃을 JSON 파일로 저장한다. 저장 전에 컨텐츠 상태(SaveState)를 레이아웃 아이템에 반영한다.</summary>
    public void SaveLayoutToFile(string path, int version = 1)
    {
      Guard.NotNullOrWhiteSpace(path);
      ThrowIfDisposed();
      CaptureContentStatesIntoLayout(_Root);

      var dto = DockLayoutSerializer.ToDto(_Root, version);
      DockLayoutJson.SaveToFile(path.Trim(), dto, null);
    }

    /// <summary>JSON파일에서 레이아웃을 로드하여 적용한다.</summary>
    /// <remarks>버전 업그레이드 + Validate/정리 + 상태 LoadState</remarks>
    public void LoadLayoutFromFile(string path, bool applyStatesToContents = true, string? reason = null)
    {
      Guard.NotNullOrWhiteSpace(path);
      ThrowIfDisposed();

      var dto = DockLayoutJson.LoadFromFile(path.Trim(), null);
      dto = DockLayoutVersioning.UpgradeToLatest(dto);

      var root = DockLayoutSerializer.FromDto(dto, Settings.ValidateLayoutOnApply);
      ApplyLayout(root, reason ?? $"LoadLayout");

      if (applyStatesToContents)
        ApplyLayoutStatesToContents(_Root);
    }

    // Content Sync ==================================================================================================

    /// <summary>레이아웃에 등장하는 PersistKey 컨텐츠를 레지스트리에 보장하고(Factory), State가 있으면 LoadState를 호출한다.</summary>
    public void ApplyLayoutStatesToContents(DockNode root)
    {
      Guard.NotNull(root);
      ThrowIfDisposed();

      foreach (var item in EnumerateLayoutItems(root))
      {
        var c = Registry.Ensure(item.PersistKey);
        if (c is null) continue;

        try { c.LoadState(item.State); }
        catch { }
      }
    }

    /// <summary>레지스트리에 있는 컨텐츠들의 상태(SaveState)를 레이아웃 아이템 State에 반영한다.</summary>
    public void CaptureContentStatesIntoLayout(DockNode root)
    {
      Guard.NotNull(root);
      ThrowIfDisposed();

      foreach (var item in EnumerateLayoutItemsMutable(root))
      {
        var c = Registry.Get(item.PersistKey);
        if (c is null) continue;

        try { item.State = c.SaveState(); }
        catch { }
      }
    }

    // Dispose ======================================================================================================

    /// <summary>관리 중인 리소스를 정리한다.</summary>
    public void Dispose()
    {
      if (_Disposed) return;
      _Disposed = true;

      try { Registry.Clear(dispose: true); }
      catch { }

      _ActiveContent = null;
      _ActiveAutoHideKey = null;

      _LastActiveKey = null;
      _LastNonAutoHideKey = null;
    }

    // Dock Core ====================================================================================================

    private DockNode DockCore(DockNode root, string persistKey, string targetGroupNodeId, DockDropSide side, double newPaneRatio, bool makeActive, out bool didChange, out string? autoReason)
    {
      didChange = false;
      autoReason = null;

      var key = persistKey.Trim();
      var targetId = targetGroupNodeId.Trim();

      var targetGroup0 = DockMutator.FindByNodeId(root, targetId) as DockGroupNode;
      if (targetGroup0 is null) return root;

      DockGroupNode? sourceGroup0 = null;
      TryFindGroupContainingKey(root, key, out sourceGroup0);

      // 같은 그룹 Center 도킹은 "활성만" (키 제거/재도킹 금지)
      if (side == DockDropSide.Center && sourceGroup0 is not null
        && string.Equals(sourceGroup0.NodeId, targetId, StringComparison.Ordinal))
      {
        if (makeActive) sourceGroup0.SetActive(key);
        didChange = true;
        autoReason = $"Dock:CenterNoop:{key}";
        return root;
      }

      var state = (sourceGroup0 is not null ? TryGetLayoutItemState(sourceGroup0, key) : null) ?? TryGetContentState(key);

      var srcKind = GetEffectiveDockKind(key, sourceGroup0, targetGroup0);
      var dstKind = targetGroup0.ContentKind;

      // Center/탭 합치기는 cross-kind 금지(문서↔툴 상호 변환/혼합 방지)
      if (side == DockDropSide.Center && srcKind != dstKind)
        return root;

      // 자기 자신 그룹에 탭 1개뿐인데 Side 도킹하면 대상 그룹이 사라져 실패/유실이 날 수 있음 => noop
      if (side != DockDropSide.Center && sourceGroup0 is not null
        && string.Equals(sourceGroup0.NodeId, targetId, StringComparison.Ordinal)
        && sourceGroup0.Items.Count <= 1)
        return root;

      // 이미 존재하는 key는 먼저 레이아웃에서 제거 후 재도킹(중복 PersistKey 방지)
      var pruned = DockMutator.CloseContent(root, key);

      // 같은 Kind면 기존 경로 유지
      if (srcKind == dstKind || side == DockDropSide.Center)
      {
        var nextRoot = DockMutator.DockToGroup(pruned, key, state, targetId, side, newPaneRatio, makeActive);
        didChange = true;
        autoReason = $"Dock:{key}";
        return nextRoot;
      }

      // cross-kind + Side 도킹: 새 그룹 Kind는 srcKind로 만들고, 대상 그룹 Kind는 유지
      var targetGroup = DockMutator.FindByNodeId(pruned, targetId) as DockGroupNode;
      if (targetGroup is null) return root;

      var ratioNew = NormalizeNewPaneRatio(newPaneRatio, srcKind, dstKind);

      var newGroup = new DockGroupNode(srcKind);
      var newGroupId = newGroup.NodeId;

      var split = CreateSplitForSideDock(targetGroup, newGroup, side, ratioNew);

      var replacedRoot = ReplaceNodeById(pruned, targetId, split, out var replaced);
      if (!replaced) return root;

      // 새 그룹에 Center로 넣는다(새 그룹 내부는 동일 Kind이므로 안전)
      var finalRoot = DockMutator.DockToGroup(replacedRoot, key, state, newGroupId, DockDropSide.Center, 0.5, makeActive);

      try { DockValidator.RebuildParents(finalRoot); } catch { }

      didChange = true;
      autoReason = $"Dock:{key}";
      return finalRoot;
    }

    private static double NormalizeNewPaneRatio(double ratio, DockContentKind sourceKind, DockContentKind targetKind)
    {
      if (double.IsNaN(ratio) || ratio <= 0.0)
        ratio = DockDefaults.GetDefaultNewPaneRatioForSideDock(sourceKind, targetKind);

      return DockDefaults.ClampLayoutRatio(ratio);
    }

    private static double NormalizeToolPaneRatio(double ratio)
    {
      if (double.IsNaN(ratio) || ratio <= 0.0)
        ratio = DockDefaults.DefaultToolOntoDocumentNewPaneRatio;

      return DockDefaults.ClampLayoutRatio(ratio);
    }

    // Helpers ======================================================================================================

    private void ThrowIfDisposed()
    {
      if (_Disposed) throw new ObjectDisposedException(nameof(DockManager));
    }

    private static string? NormalizeKey(string? s)
    {
      if (string.IsNullOrWhiteSpace(s)) return null;

      var t = s.Trim();
      return t.Length == 0 ? null : t;
    }

    private void SetActiveContentDirect(string? persistKey, bool updateLastNonAutoHide)
    {
      var key = NormalizeKey(persistKey);

      IDockContent? next = null;
      if (key is not null) next = Registry.Ensure(key);

      if (ReferenceEquals(_ActiveContent, next)) return;

      var old = _ActiveContent;
      _ActiveContent = next;

      if (key is not null)
      {
        _LastActiveKey = key;
        if (updateLastNonAutoHide) _LastNonAutoHideKey = key;
      }

      Events.RaiseActiveContentChanged(old, next);
    }

    private void EnsureActiveContentAfterLayout()
    {
      // 1) 현재 ActiveContent가 레이아웃에 없으면 제거
      var curKey = _ActiveContent?.PersistKey;
      if (!string.IsNullOrWhiteSpace(curKey))
      {
        curKey = curKey!.Trim();
        if (curKey.Length != 0 && IsKeyInLayout(_Root, curKey))
        {
          _LastActiveKey = curKey;
          if (TryFindGroupContainingKey(_Root, curKey, out _))
            _LastNonAutoHideKey = curKey;

          return;
        }
      }

      // 2) ActiveContent가 null/무효면: 마지막 키/폴백으로 복구
      string? restore = null;

      if (!string.IsNullOrWhiteSpace(_LastActiveKey))
      {
        var k = _LastActiveKey!.Trim();
        if (k.Length != 0 && IsKeyInLayout(_Root, k))
          restore = k;
      }

      if (restore is null)
        restore = SelectFallbackGroupActiveKey(preferDocument: true);

      SetActiveContentDirect(restore, updateLastNonAutoHide: (restore is not null));
    }

    private string? SelectFallbackGroupActiveKey(bool preferDocument)
    {
      // 0) 마지막 NonAutoHide가 아직 그룹에 있으면 최우선
      if (!string.IsNullOrWhiteSpace(_LastNonAutoHideKey))
      {
        var k = _LastNonAutoHideKey!.Trim();
        if (k.Length != 0 && TryFindGroupContainingKey(_Root, k, out _))
          return k;
      }

      // 1) Document 그룹 우선(옵션)
      if (preferDocument)
      {
        var k = FindFirstGroupActiveKeyByKind(DockContentKind.Document);
        if (!string.IsNullOrWhiteSpace(k)) return k;
      }

      // 2) ToolWindow 그룹
      {
        var k = FindFirstGroupActiveKeyByKind(DockContentKind.ToolWindow);
        if (!string.IsNullOrWhiteSpace(k)) return k;
      }

      // 3) 아무 그룹
      return FindFirstGroupActiveKeyAny();
    }

    private string? FindFirstGroupActiveKeyByKind(DockContentKind kind)
    {
      foreach (var n in _Root.TraverseDepthFirst(true))
      {
        if (n is not DockGroupNode g) continue;
        if (g.ContentKind != kind) continue;
        if (g.Items.Count <= 0) continue;

        var k = NormalizeKey(g.ActiveKey) ?? NormalizeKey(g.Items[0].PersistKey);
        if (!string.IsNullOrWhiteSpace(k)) return k;
      }

      return null;
    }

    private string? FindFirstGroupActiveKeyAny()
    {
      foreach (var n in _Root.TraverseDepthFirst(true))
      {
        if (n is not DockGroupNode g) continue;
        if (g.Items.Count <= 0) continue;

        var k = NormalizeKey(g.ActiveKey) ?? NormalizeKey(g.Items[0].PersistKey);
        if (!string.IsNullOrWhiteSpace(k)) return k;
      }

      return null;
    }

    private void ClearActiveContentIfMatches(string persistKey)
    {
      if (_ActiveContent is null) return;

      try
      {
        if (!string.Equals(_ActiveContent.PersistKey, persistKey, StringComparison.Ordinal))
          return;

        var old = _ActiveContent;
        _ActiveContent = null;
        Events.RaiseActiveContentChanged(old, null);
      }
      catch
      {
        // ignore
      }
    }

    private bool ClearAutoHidePopupIfMatches(string persistKey)
    {
      if (string.IsNullOrWhiteSpace(_ActiveAutoHideKey)) return false;
      if (!string.Equals(_ActiveAutoHideKey, persistKey, StringComparison.Ordinal)) return false;

      _ActiveAutoHideKey = null;

      // 레이아웃 쪽 표시 키도 같이 정리
      SyncAutoHidePopupStateToLayout(ref _ActiveAutoHideKey, _Root);
      return true;
    }

    private static void SyncAutoHidePopupStateToLayout(ref string? activeAutoHideKey, DockNode root)
    {
      // 1) 현재 표시 키가 유효한 AutoHide 항목인지 확인
      DockAutoHideNode? activeNode = null;

      if (!string.IsNullOrWhiteSpace(activeAutoHideKey))
      {
        var key = activeAutoHideKey!.Trim();
        if (key.Length == 0)
        {
          activeAutoHideKey = null;
        }
        else
        {
          // 그룹에 들어간 키면 AutoHide 표시 대상이 아님
          if (TryFindGroupContainingKey(root, key, out _))
            activeAutoHideKey = null;
          else if (!TryFindAutoHideContainingKey(root, key, out activeNode))
            activeAutoHideKey = null;
          else
            activeAutoHideKey = key;
        }
      }

      // 2) 레이아웃의 DockAutoHideNode에 "현재 표시 키"를 기록(가능한 경우)
      foreach (var n in root.TraverseDepthFirst(true))
      {
        if (n is not DockAutoHideNode ah) continue;

        if (activeNode is not null && ReferenceEquals(ah, activeNode) && !string.IsNullOrWhiteSpace(activeAutoHideKey))
          TrySetAutoHideNodeActiveKey(ah, activeAutoHideKey);
        else
          TrySetAutoHideNodeActiveKey(ah, null);
      }
    }

    private static bool TrySetAutoHideNodeActiveKey(DockAutoHideNode node, string? persistKey)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(persistKey))
        {
          node.ClearActive();
          return true;
        }

        return node.SetActive(persistKey);
      }
      catch
      {
        return false;
      }
    }

    private static bool IsKeyInLayout(DockNode root, string persistKey)
    {
      foreach (var n in root.TraverseDepthFirst(true))
      {
        if (n is DockGroupNode g)
        {
          for (int i = 0; i < g.Items.Count; i++)
            if (string.Equals(g.Items[i].PersistKey, persistKey, StringComparison.Ordinal))
              return true;
        }
        else if (n is DockAutoHideNode a)
        {
          for (int i = 0; i < a.Items.Count; i++)
            if (string.Equals(a.Items[i].PersistKey, persistKey, StringComparison.Ordinal))
              return true;
        }
      }

      return false;
    }

    private string? TryGetContentState(string persistKey)
    {
      var c = Registry.Get(persistKey);
      if (c is null) return null;

      try { return c.SaveState(); }
      catch { return null; }
    }

    private static bool TryFindGroupContainingKey(DockNode root, string persistKey, out DockGroupNode group)
    {
      foreach (var n in root.TraverseDepthFirst(true))
      {
        if (n is not DockGroupNode g) continue;

        for (int i = 0; i < g.Items.Count; i++)
          if (string.Equals(g.Items[i].PersistKey, persistKey, StringComparison.Ordinal))
          {
            group = g;
            return true;
          }
      }

      group = null!;
      return false;
    }

    private static bool TryFindAutoHideContainingKey(DockNode root, string persistKey, out DockAutoHideNode autoHide)
    {
      foreach (var n in root.TraverseDepthFirst(true))
      {
        if (n is not DockAutoHideNode a) continue;

        for (int i = 0; i < a.Items.Count; i++)
          if (string.Equals(a.Items[i].PersistKey, persistKey, StringComparison.Ordinal))
          {
            autoHide = a;
            return true;
          }
      }

      autoHide = null!;
      return false;
    }

    private static string? TryGetLayoutItemState(DockGroupNode group, string persistKey)
    {
      for (int i = 0; i < group.Items.Count; i++)
        if (string.Equals(group.Items[i].PersistKey, persistKey, StringComparison.Ordinal))
          return group.Items[i].State;

      return null;
    }

    private static bool TryReorderTabInGroup(DockNode root, string targetGroupNodeId, string persistKey, int insertIndex)
    {
      var g = DockMutator.FindByNodeId(root, targetGroupNodeId) as DockGroupNode;
      if (g is null) return false;

      // IReadOnlyList로 노출되더라도 실제 구현이 List<T>면 IList<T> 캐스팅이 된다.
      var items = g.Items as IList<DockGroupItem>;
      if (items is null) return false;
      if (items.IsReadOnly) return false;

      // 1) 현재 인덱스 찾기
      var cur = -1;
      for (int i = 0; i < items.Count; i++)
      {
        if (string.Equals(items[i].PersistKey, persistKey, StringComparison.Ordinal))
        {
          cur = i;
          break;
        }
      }

      if (cur < 0) return false;

      // 2) insertIndex는 "삽입 위치(0..Count)" 기준
      var count = items.Count;
      if (insertIndex < 0) insertIndex = 0;
      if (insertIndex > count) insertIndex = count;

      // 제거 후 인덱스 보정
      var adjusted = insertIndex;
      if (adjusted > cur) adjusted--;

      var newCount = count - 1; // 제거 후 Count
      if (adjusted < 0) adjusted = 0;
      if (adjusted > newCount) adjusted = newCount;

      if (adjusted == cur) return true;

      try
      {
        var item = items[cur];
        items.RemoveAt(cur);
        items.Insert(adjusted, item);
        return true;
      }
      catch
      {
        return false;
      }
    }

    // Dock ContentKind Policy (cross-kind safe) =====================================================================

    private DockContentKind GetEffectiveDockKind(string persistKey, DockGroupNode? sourceGroup, DockGroupNode targetGroup)
    {
      if (sourceGroup is not null) return sourceGroup.ContentKind;

      var c = Registry.Get(persistKey);
      var k = TryGetContentKindFromRegistry(c);
      if (k.HasValue) return k.Value;

      return targetGroup.ContentKind;
    }

    private static DockContentKind? TryGetContentKindFromRegistry(IDockContent? content)
    {
      if (content is null) return null;

      try
      {
        var t = content.GetType();
        var p = t.GetProperty("ContentKind") ?? t.GetProperty("DockContentKind") ?? t.GetProperty("DockKind");
        if (p is null) return null;

        if (p.PropertyType == typeof(DockContentKind))
          return (DockContentKind)p.GetValue(content)!;

        var u = Nullable.GetUnderlyingType(p.PropertyType);
        if (u == typeof(DockContentKind))
        {
          var v = p.GetValue(content);
          if (v is null) return null;
          return (DockContentKind)v;
        }
      }
      catch
      {
        // ignore
      }

      return null;
    }

    private static DockSplitNode CreateSplitForSideDock(DockGroupNode targetGroup, DockGroupNode newGroup, DockDropSide side, double newPaneRatio)
    {
      var vertical = side == DockDropSide.Left || side == DockDropSide.Right;
      var orientation = vertical ? DockSplitOrientation.Vertical : DockSplitOrientation.Horizontal;

      // DockSplitNode.Ratio는 "First"의 비율
      if (side == DockDropSide.Left || side == DockDropSide.Top)
        return new DockSplitNode(orientation, newPaneRatio, newGroup, targetGroup);

      return new DockSplitNode(orientation, 1.0 - newPaneRatio, targetGroup, newGroup);
    }

    private static DockNode ReplaceNodeById(DockNode node, string targetNodeId, DockNode replacement, out bool replaced)
    {
      replaced = false;

      if (node is DockGroupNode g)
      {
        if (string.Equals(g.NodeId, targetNodeId, StringComparison.Ordinal))
        {
          replaced = true;
          try { g.SetParentInternal(null); } catch { }
          return replacement;
        }
        return g;
      }

      if (node is DockSplitNode s)
      {
        var first = ReplaceNodeById(s.First, targetNodeId, replacement, out var r0);
        var second = ReplaceNodeById(s.Second, targetNodeId, replacement, out var r1);

        replaced = r0 || r1;
        if (!replaced) return s;

        if (!ReferenceEquals(first, s.First)) s.ReplaceChild(s.First, first);
        if (!ReferenceEquals(second, s.Second)) s.ReplaceChild(s.Second, second);

        return s;
      }

      if (node is DockFloatingNode f)
      {
        var inner = ReplaceNodeById(f.Root, targetNodeId, replacement, out replaced);
        if (!replaced) return f;

        if (!ReferenceEquals(inner, f.Root)) f.ReplaceRoot(inner);
        return f;
      }

      return node;
    }

    private static IEnumerable<(string PersistKey, string? State)> EnumerateLayoutItems(DockNode root)
    {
      foreach (var n in root.TraverseDepthFirst(true))
      {
        if (n is DockGroupNode g)
          for (int i = 0; i < g.Items.Count; i++) yield return (g.Items[i].PersistKey, g.Items[i].State);
        else if (n is DockAutoHideNode a)
          for (int i = 0; i < a.Items.Count; i++) yield return (a.Items[i].PersistKey, a.Items[i].State);
      }
    }

    private static IEnumerable<LayoutItemRef> EnumerateLayoutItemsMutable(DockNode root)
    {
      foreach (var n in root.TraverseDepthFirst(true))
      {
        if (n is DockGroupNode g) for (int i = 0; i < g.Items.Count; i++)
        {
          int idx = i;
          yield return new LayoutItemRef(g.Items[idx].PersistKey, () => g.Items[idx].State, v => g.Items[idx].State = v);
        }
        else if (n is DockAutoHideNode a) for (int i = 0; i < a.Items.Count; i++)
        {
          int idx = i;
          yield return new LayoutItemRef(a.Items[idx].PersistKey, () => a.Items[idx].State, v => a.Items[idx].State = v);
        }
      }
    }

    private readonly struct LayoutItemRef
    {
      public string PersistKey { get; }

      public string? State
      {
        get { return _Getter(); }
        set { _Setter(value); }
      }

      private readonly Func<string?> _Getter;
      private readonly Action<string?> _Setter;

      public LayoutItemRef(string persistKey, Func<string?> getter, Action<string?> setter)
      {
        PersistKey = persistKey;
        _Getter = getter;
        _Setter = setter;
      }
    }
  }
}

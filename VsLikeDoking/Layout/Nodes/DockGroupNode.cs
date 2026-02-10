using System;
using System.Collections.Generic;

using VsLikeDoking.Abstractions;
using VsLikeDoking.Utils;

namespace VsLikeDoking.Layout.Nodes
{
  /// <summary>탭으로 묶이는 컨텐츠 그룹을 나타낸다. 문서 탭 그룹/도구창 탭 그룹이 전부 이 노드이다.</summary>
  /// <remarks>레이아웃 저장시에는 PersistKey 목록(문자열)만 들어있고, 실제 Control은 UI레이어에서 IDockContentFactory로 생성해 붙인다.</remarks>
  public sealed class DockGroupNode : DockNode
  {
    // Fields =====================================================================================

    private readonly List<DockGroupItem> _Items = new();

    // Properties =================================================================================

    /// <summary>그룹이 표현하는 컨텐츠 종류(문서/도구함)</summary>
    public DockContentKind ContentKind { get; }

    /// <summary>그룹 내 탭 항목들(저장용 모델). 순서가 탭 순서다.</summary>
    public IReadOnlyList<DockGroupItem> Items => _Items;

    /// <summary>활성 탭의 PersistKey. 없으면 null</summary>
    public string? ActiveKey { get; private set; }

    /// <summary>그룹이 비어있는지 여부</summary>
    public bool IsEmpty => _Items.Count == 0;

    // Ctor =======================================================================================

    /// <summary>탭 그룹 노드를 생성한다.</summary>
    public DockGroupNode(DockContentKind contentKind, string? nodeId = null) : base(DockNodeKind.Group, nodeId)
    {
      ContentKind = contentKind;
    }

    // Items ======================================================================================

    /// <summary>항목을 추가한다. 이미 존재하면 추가하지 않고 false를 반환한다.</summary>
    public bool Add(DockGroupItem item)
    {
      Guard.NotNull(item);

      if (IndexOf(item.PersistKey) >= 0) return false;

      _Items.Add(item);
      if (ActiveKey is null) ActiveKey = item.PersistKey;
      return true;
    }

    /// <summary>PersistKey로 항목을 추가한다. 이미 존재하면 추가하지 않고 false를 반환한다.</summary>
    public bool Add(string persistKey, string? state = null)
      => Add(new DockGroupItem(persistKey, state));

    /// <summary>지정 위치(삽입 위치 0..Count)에 항목을 삽입한다. 이미 존재하면 false.</summary>
    public bool InsertAt(DockGroupItem item, int insertIndex, bool makeActiveIfEmpty = true)
    {
      Guard.NotNull(item);

      if (IndexOf(item.PersistKey) >= 0) return false;

      if (insertIndex < 0) insertIndex = 0;
      if (insertIndex > _Items.Count) insertIndex = _Items.Count;

      var wasEmpty = _Items.Count == 0;

      _Items.Insert(insertIndex, item);

      if (makeActiveIfEmpty && wasEmpty) ActiveKey = item.PersistKey;
      return true;
    }

    /// <summary>PersistKey에 해당하는 항목을 제거한다. 제거되면 true</summary>
    public bool Remove(string persistKey)
      => Remove(persistKey, out _);

    /// <summary>PersistKey에 해당하는 항목을 제거하고 제거된 항목을 반환한다.</summary>
    public bool Remove(string persistKey, out DockGroupItem? removed)
    {
      Guard.NotNullOrWhiteSpace(persistKey);

      var key = persistKey.Trim();
      var idx = IndexOf(key);
      if (idx < 0)
      {
        removed = null;
        return false;
      }

      removed = _Items[idx];
      _Items.RemoveAt(idx);

      if (string.Equals(ActiveKey, key, StringComparison.Ordinal))
        ActiveKey = _Items.Count > 0 ? _Items[MathEx.Clamp(idx, 0, _Items.Count - 1)].PersistKey : null;

      return true;
    }

    /// <summary>삽입 위치(0..Count) 기준으로 탭(항목)의 순서를 이동한다. 존재하면 true.</summary>
    /// <remarks>
    /// - insertIndex는 "삽입 위치"이며, 이동 대상 제거 후 보정된다.
    /// - 이동 후 활성(ActiveKey)은 유지된다.
    /// </remarks>
    public bool Move(string persistKey, int insertIndex)
    {
      Guard.NotNullOrWhiteSpace(persistKey);

      var key = persistKey.Trim();
      var cur = IndexOf(key);
      if (cur < 0) return false;
      if (_Items.Count <= 1) return true;

      var count = _Items.Count;

      if (insertIndex < 0) insertIndex = 0;
      if (insertIndex > count) insertIndex = count;

      // insertIndex는 "삽입 위치(0..Count)" 이므로, 같은 아이템을 이동할 땐 보정 필요
      var target = insertIndex;
      if (target > cur) target--;

      if (target < 0) target = 0;
      if (target > count - 1) target = count - 1;

      if (target == cur) return true;

      var item = _Items[cur];
      _Items.RemoveAt(cur);
      _Items.Insert(target, item);
      return true;
    }

    /// <summary>활성 탭을 지정한다. 존재하면 true.</summary>
    public bool SetActive(string persistKey)
    {
      Guard.NotNullOrWhiteSpace(persistKey);

      var key = persistKey.Trim();
      if (IndexOf(key) < 0) return false;

      ActiveKey = key;
      return true;
    }

    /// <summary>현재 항목들을 모두 제거한다.</summary>
    public void Clear()
    {
      _Items.Clear();
      ActiveKey = null;
    }

    /// <summary>PersistKey의 인덱스를 반환한다. 없으면 -1.</summary>
    public int IndexOf(string persistKey)
    {
      if (string.IsNullOrWhiteSpace(persistKey)) return -1;

      var key = persistKey.Trim();

      for (int i = 0; i < _Items.Count; i++)
        if (string.Equals(_Items[i].PersistKey, key, StringComparison.Ordinal))
          return i;

      return -1;
    }

    // DockNode ===================================================================================

    public override IEnumerable<DockNode> EnumerateChildren()
    {
      yield break;
    }
  }

  /// <summary>그룹(탭) 항목의 저장용 모델</summary>
  public sealed class DockGroupItem
  {
    public string PersistKey { get; }
    public string? State { get; set; }

    public DockGroupItem(string persistKey, string? state = null)
    {
      PersistKey = Guard.NotNullOrWhiteSpace(persistKey).Trim();
      State = state;
    }

    public override string ToString()
      => PersistKey;
  }
}

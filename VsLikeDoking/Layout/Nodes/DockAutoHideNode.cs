// VsLikeDocking - VsLikeDoking - Layout/Nodes/DockAutoHideNode.cs - DockAutoHideNode - (File)

using System;
using System.Collections.Generic;
using System.Drawing;

using VsLikeDoking.Abstractions;
using VsLikeDoking.Utils;

namespace VsLikeDoking.Layout.Nodes
{
  /// <summary>오토하이드(핀) 스트립 1개(L/R/T/B)를 레이아웃 트리에서 표현한다.</summary>
  /// <remarks>내부에는 숨겨진 컨텐츠 목록(PersistKey + State)과 현재 활성(팝업 대상) 키를 저장한다.</remarks>
  public class DockAutoHideNode : DockNode
  {
    // Fields =====================================================================================

    private readonly List<DockAutoHideItem> _Items = new();

    // Properties =================================================================================

    /// <summary>오토하이드 스트립 위치</summary>
    public DockAutoHideSide Side { get; }

    /// <summary>스트립이 표현하는 컨텐츠 종류(일반적으로 ToolWindow)</summary>
    public DockContentKind ContentKind { get; }

    /// <summary>오토하이드 항목 목록(저장용 모델). 순서는 스트립 버튼 순서다.</summary>
    public IReadOnlyList<DockAutoHideItem> Items
      => _Items;

    /// <summary>현재 활성(팝업 대상) PersistKey. 없으면 null</summary>
    public string? ActiveKey { get; private set; }

    /// <summary>비어있는지 여부</summary>
    public bool IsEmpty
      => _Items.Count == 0;

    // Ctor =======================================================================================

    /// <summary>오토하이드 스트립 노드를 생성한다.</summary>
    public DockAutoHideNode(DockAutoHideSide side, DockContentKind kind = DockContentKind.ToolWindow, string? nodeId = null)
      : base(DockNodeKind.AutoHide, nodeId)
    {
      Side = side;
      ContentKind = kind;
    }

    // Items ======================================================================================

    /// <summary>PersistKey가 존재하면 true</summary>
    public bool Contains(string persistKey)
    {
      var key = NormalizeKey(persistKey);
      if (key is null) return false;

      return IndexOfKey(key) >= 0;
    }

    /// <summary>항목을 추가한다. 이미 존재하면 추가하지 않고 false를 반환한다.</summary>
    public bool Add(DockAutoHideItem item)
    {
      Guard.NotNull(item);

      var key = NormalizeKey(item.PersistKey);
      if (key is null) return false;

      if (IndexOfKey(key) >= 0) return false;

      _Items.Add(item);

      if (string.IsNullOrWhiteSpace(ActiveKey))
        ActiveKey = key;

      return true;
    }

    /// <summary>PersistKey로 항목을 추가한다. 이미 존재한다면 추가하지 않고 false를 반환한다.</summary>
    public bool Add(string persistKey, string? state = null, Size? popupSize = null)
    {
      var key = NormalizeKey(persistKey);
      if (key is null) return false;

      return Add(new DockAutoHideItem(key, state) { PopupSize = popupSize });
    }

    /// <summary>PersistKey에 해당하는 항목을 제거한다. 제거되면 true</summary>
    public bool Remove(string persistKey)
    {
      var key = NormalizeKey(persistKey);
      if (key is null) return false;

      var idx = IndexOfKey(key);
      if (idx < 0) return false;

      _Items.RemoveAt(idx);

      if (string.Equals(ActiveKey, key, StringComparison.Ordinal))
      {
        if (_Items.Count == 0) ActiveKey = null;
        else ActiveKey = _Items[MathEx.Clamp(idx, 0, _Items.Count - 1)].PersistKey;
      }

      return true;
    }

    /// <summary>현재 활성(팝업 대상) 키를 클리어한다.</summary>
    public void ClearActive()
      => ActiveKey = null;

    /// <summary>
    /// 활성 항목 지정. 존재하면 true.
    /// 빈 문자열/공백이면 ActiveKey를 null로 클리어하고 true를 반환한다(동기화/상태 클리어용).
    /// </summary>
    public bool SetActive(string persistKey)
    {
      var key = NormalizeKey(persistKey);

      // "클리어" 허용
      if (key is null)
      {
        ActiveKey = null;
        return true;
      }

      if (IndexOfKey(key) < 0) return false;

      ActiveKey = key;
      return true;
    }

    /// <summary>현재 항목들을 모두 제거한다.</summary>
    public void Clear()
    {
      _Items.Clear();
      ActiveKey = null;
    }

    // DockNode ===================================================================================

    public override IEnumerable<DockNode> EnumerateChildren()
    {
      yield break;
    }

    // Utils ======================================================================================

    private static string? NormalizeKey(string? s)
    {
      if (string.IsNullOrWhiteSpace(s)) return null;

      var t = s.Trim();
      return t.Length == 0 ? null : t;
    }

    private int IndexOfKey(string key)
    {
      for (int i = 0; i < _Items.Count; i++)
      {
        if (string.Equals(_Items[i].PersistKey, key, StringComparison.Ordinal))
          return i;
      }

      return -1;
    }
  }

  /// <summary>오토하이드 항목의 저장용 모델</summary>
  public sealed class DockAutoHideItem
  {
    public string PersistKey { get; }
    public string? State { get; set; }

    /// <summary>팝업 표시시 선호 크기(선택). 실제 크기는 UI정책으로 보정될 수 있다.</summary>
    public Size? PopupSize { get; set; }

    public DockAutoHideItem(string persistKey, string? state = null)
    {
      PersistKey = Guard.NotNullOrWhiteSpace(persistKey).Trim();
      State = state;
    }

    public override string ToString()
      => PersistKey;
  }

  /// <summary>오토하이드 스트립 위치</summary>
  public enum DockAutoHideSide { Left = 0, Right = 1, Top = 2, Bottom = 3 }
}

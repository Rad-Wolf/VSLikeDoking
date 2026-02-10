// VsLikeDocking - VsLikeDoking - UI/Visual/DockVisualTree.cs - DockVisualTree - (File)

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;

using VsLikeDoking.Layout.Nodes;

namespace VsLikeDoking.UI.Visual
{
  /// <summary>DockLayoutEngine이 계산한 그리기/히트테스트용 사각형 캐시를 담는 컨테이너</summary>
  /// <remarks>그룹/탭/스플리터/히트영역을 배열로 평탄화하여 렌더/히트테스트가 빠르게 순회할 수 있게 한다.</remarks>
  public sealed class DockVisualTree
  {
    // Types ======================================================================================

    public enum DockEdge : byte { Left = 0, Right = 1, Top = 2, Bottom = 3 }

    // 기존 값의 숫자 안정성을 위해 신규 Kind는 뒤에만 추가한다.
    public enum RegionKind : byte
    {
      None = 0,
      Tab,
      TabClose,
      TabStrip,
      Caption,
      CaptionClose,
      Splitter,
      Content,
      Empty,

      // AutoHide (추가)
      AutoHideStrip,
      AutoHideTab,
    }

    public enum SplitAxis : byte { Horizontal = 0, Vertical = 1 }

    public readonly struct HitRegion
    {
      public static HitRegion None => new(RegionKind.None, Rectangle.Empty, -1, -1);

      public RegionKind Kind { get; }
      public Rectangle Bounds { get; }

      public readonly int PrimaryIndex;   // : [Splitter => SplitIndex] [Group 기반 => GroupIndex] [AutoHide* => StripIndex] [그 외 -1]
      public readonly int SecondaryIndex; // : [Tab/TabClose => TabIndex] [AutoHideTab => AutoHideTabIndex] [그 외 -1]

      /// <summary>히트 영역을 생성한다.</summary>
      public HitRegion(RegionKind kind, Rectangle bounds, int primaryIndex, int secondaryIndex)
      {
        Kind = kind;
        Bounds = bounds;
        PrimaryIndex = primaryIndex;
        SecondaryIndex = secondaryIndex;
      }
    }

    public struct SplitVisual
    {
      public DockSplitNode Node;
      public Rectangle Bounds;
      public Rectangle SplitterBounds;
      public SplitAxis Axis;
      public float Ratio; // 참고: Ratio는 Build 시점 스냅샷이다. 실시간은 Node.Ratio가 기준.
    }

    public struct GroupVisual
    {
      public DockGroupNode Node;
      public Rectangle Bounds;
      public Rectangle TabStripBounds;
      public Rectangle CaptionBounds;
      public Rectangle ContentBounds;

      // Tabs는 _Tabs에 평탄화 저장. Group은 Range만 들고 있음.
      public int TabStart;
      public int TabCount;

      public int ActiveTabIndex; // Group 내 활성 탭 인덱스(없으면 -1) - "Tabs 리스트의 인덱스"
    }

    public struct TabVisual
    {
      public int GroupIndex;
      public object? ContentKey;
      public Rectangle Bounds;
      public Rectangle CloseBounds;
      public bool IsActive;
    }

    public struct AutoHideStripVisual
    {
      public DockEdge Edge;
      public Rectangle Bounds;

      // Tabs는 _AutoHideTabs에 평탄화 저장. Strip은 Range만 들고 있음.
      public int TabStart;
      public int TabCount;
    }

    public struct AutoHideTabVisual
    {
      public int StripIndex;
      public object? ContentKey;
      public Rectangle Bounds;
      public bool IsActive;
    }

    /// <summary>AutoHide 팝업 배치에 필요한 메타</summary>
    public readonly struct AutoHidePopupMeta
    {
      public DockEdge Edge { get; }
      public Size? PopupSize { get; }
      public int StripIndex { get; }
      public int TabIndex { get; }

      public AutoHidePopupMeta(DockEdge edge, Size? popupSize, int stripIndex, int tabIndex)
      {
        Edge = edge;
        PopupSize = popupSize;
        StripIndex = stripIndex;
        TabIndex = tabIndex;
      }
    }

    // Fields ======================================================================================

    private readonly List<SplitVisual> _Splits = new(16);
    private readonly List<GroupVisual> _Groups = new(16);
    private readonly List<TabVisual> _Tabs = new(64);

    private readonly List<AutoHideStripVisual> _AutoHideStrips = new(4);
    private readonly List<AutoHideTabVisual> _AutoHideTabs = new(32);

    private readonly List<HitRegion> _HitRegions = new(256);

    // AutoHide Popup Meta Cache (key -> edge/popupSize) ===========================================

    private readonly Dictionary<string, AutoHidePopupMeta> _AutoHidePopupMetaByKey = new(StringComparer.Ordinal);

    // Properties ==================================================================================

    /// <summary>이번 프레임의 전체 영역</summary>
    public Rectangle Bounds { get; private set; }

    /// <summary>스플릿(평탄화) 목록</summary>
    public IReadOnlyList<SplitVisual> Splits => _Splits;

    /// <summary>그룹(평탄화) 목록</summary>
    public IReadOnlyList<GroupVisual> Groups => _Groups;

    /// <summary>탭(평탄화) 목록</summary>
    public IReadOnlyList<TabVisual> Tabs => _Tabs;

    /// <summary>AutoHide Strip(평탄화) 목록</summary>
    public IReadOnlyList<AutoHideStripVisual> AutoHideStrips => _AutoHideStrips;

    /// <summary>AutoHide Tab(평탄화) 목록</summary>
    public IReadOnlyList<AutoHideTabVisual> AutoHideTabs => _AutoHideTabs;

    /// <summary>히트테스트용 평탄화 영역 목록</summary>
    public IReadOnlyList<HitRegion> HitRegions => _HitRegions;

    // Public ======================================================================================

    /// <summary>모든 캐시를 초기화한다</summary>
    public void Clear()
    {
      Bounds = Rectangle.Empty;

      _Splits.Clear();
      _Groups.Clear();
      _Tabs.Clear();

      _AutoHideStrips.Clear();
      _AutoHideTabs.Clear();

      _HitRegions.Clear();

      _AutoHidePopupMetaByKey.Clear();
    }

    /// <summary>새 계산을 시작한다.</summary>
    public void BeginFrame(Rectangle bounds)
    {
      Clear();
      Bounds = bounds;
    }

    /// <summary>좌표(클라이언트)로 히트테스트를 시도한다. (없으면 false)</summary>
    /// <remarks>DockHitTest의 우선순위/정규화 규칙과 동일한 결과를 반환한다.</remarks>
    public bool TryHitTest(Point clientPoint, out HitRegion hit)
    {
      var r = DockHitTest.HitTest(this, clientPoint);
      if (!r.IsHit)
      {
        hit = HitRegion.None;
        return false;
      }

      hit = new HitRegion(r.Kind, r.Bounds, r.PrimaryIndex, r.SecondaryIndex);
      return true;
    }

    /// <summary>PersistKey에 대한 AutoHide 팝업 메타를 조회한다.</summary>
    public bool TryGetAutoHidePopupMeta(string persistKey, out AutoHidePopupMeta meta)
    {
      meta = default;

      var key = NormalizeKeyForQuery(persistKey);
      if (key is null) return false;

      return _AutoHidePopupMetaByKey.TryGetValue(key, out meta);
    }

    // Builder API ( for DockLayoutEngine) =========================================================

    /// <summary>스플릿 추가</summary>
    internal int AddSplit(DockSplitNode node, Rectangle bounds, Rectangle splitterBounds, SplitAxis axis, float ratio)
    {
      var index = _Splits.Count;

      _Splits.Add(new SplitVisual { Node = node, Bounds = bounds, SplitterBounds = splitterBounds, Axis = axis, Ratio = ratio });

      AddHitRegion(RegionKind.Splitter, splitterBounds, primaryIndex: index, secondaryIndex: -1);
      return index;
    }

    /// <summary>그룹추가</summary>
    internal int AddGroup(DockGroupNode node, Rectangle bounds, Rectangle tabStripBounds, Rectangle captionBounds, Rectangle contentBounds)
    {
      var index = _Groups.Count;

      _Groups.Add(new GroupVisual
      {
        Node = node,
        Bounds = bounds,
        TabStripBounds = tabStripBounds,
        CaptionBounds = captionBounds,
        ContentBounds = contentBounds,
        TabStart = _Tabs.Count,
        TabCount = 0,
        ActiveTabIndex = -1
      });

      if (!tabStripBounds.IsEmpty) AddHitRegion(RegionKind.TabStrip, tabStripBounds, primaryIndex: index, secondaryIndex: -1);
      if (!captionBounds.IsEmpty) AddHitRegion(RegionKind.Caption, captionBounds, primaryIndex: index, secondaryIndex: -1);
      if (!contentBounds.IsEmpty) AddHitRegion(RegionKind.Content, contentBounds, primaryIndex: index, secondaryIndex: -1);

      return index;
    }

    /// <summary>그룹에 탭을 추가한다.</summary>
    /// <remarks>주의 : Engine은 그룹 처리 중에만 AddTab을 호출하는 것을 전제로 한다.(성능/단순화 목적)</remarks>
    internal int AddTab(int groupIndex, object? contentKey, Rectangle bounds, Rectangle closeBounds, bool isActive)
    {
      if ((uint)groupIndex >= (uint)_Groups.Count) throw new ArgumentOutOfRangeException(nameof(groupIndex));

      var tabIndex = _Tabs.Count;

      _Tabs.Add(new TabVisual { GroupIndex = groupIndex, ContentKey = contentKey, Bounds = bounds, CloseBounds = closeBounds, IsActive = isActive });

      // GroupVisual은 struct라서 read-modify-write가 필요함
      var g = _Groups[groupIndex];
      g.TabCount++;

      if (isActive) g.ActiveTabIndex = tabIndex;

      _Groups[groupIndex] = g;

      AddHitRegion(RegionKind.Tab, bounds, primaryIndex: groupIndex, secondaryIndex: tabIndex);

      if (!closeBounds.IsEmpty)
        AddHitRegion(RegionKind.TabClose, closeBounds, primaryIndex: groupIndex, secondaryIndex: tabIndex);

      return tabIndex;
    }

    /// <summary>AutoHide Strip 추가</summary>
    internal int AddAutoHideStrip(DockEdge edge, Rectangle bounds)
    {
      var index = _AutoHideStrips.Count;

      _AutoHideStrips.Add(new AutoHideStripVisual
      {
        Edge = edge,
        Bounds = bounds,
        TabStart = _AutoHideTabs.Count,
        TabCount = 0
      });

      if (!bounds.IsEmpty)
        AddHitRegion(RegionKind.AutoHideStrip, bounds, primaryIndex: index, secondaryIndex: -1);

      return index;
    }

    /// <summary>AutoHide Strip에 Tab 추가</summary>
    internal int AddAutoHideTab(int stripIndex, object? contentKey, Rectangle bounds, bool isActive)
      => AddAutoHideTab(stripIndex, contentKey, bounds, isActive, popupSize: null);

    /// <summary>AutoHide Strip에 Tab 추가 (PopupSize까지 포함)</summary>
    internal int AddAutoHideTab(int stripIndex, object? contentKey, Rectangle bounds, bool isActive, Size? popupSize)
    {
      if ((uint)stripIndex >= (uint)_AutoHideStrips.Count) throw new ArgumentOutOfRangeException(nameof(stripIndex));

      var tabIndex = _AutoHideTabs.Count;

      _AutoHideTabs.Add(new AutoHideTabVisual { StripIndex = stripIndex, ContentKey = contentKey, Bounds = bounds, IsActive = isActive });

      // AutoHideStripVisual은 struct라서 read-modify-write가 필요함
      var s = _AutoHideStrips[stripIndex];
      s.TabCount++;
      _AutoHideStrips[stripIndex] = s;

      AddHitRegion(RegionKind.AutoHideTab, bounds, primaryIndex: stripIndex, secondaryIndex: tabIndex);

      // 팝업 메타 캐시(키 -> edge/popupSize)
      if (TryGetAutoHideKeyAndPopupSize(contentKey, out var key, out var keyPopupSize))
      {
        var ps = popupSize ?? keyPopupSize;
        _AutoHidePopupMetaByKey[key] = new AutoHidePopupMeta(s.Edge, ps, stripIndex, tabIndex);
      }

      return tabIndex;
    }

    internal void AddCaptionClose(int groupIndex, Rectangle bounds)
    {
      if ((uint)groupIndex >= (uint)_Groups.Count) throw new ArgumentOutOfRangeException(nameof(groupIndex));
      if (bounds.IsEmpty) return;
      AddHitRegion(RegionKind.CaptionClose, bounds, primaryIndex: groupIndex, secondaryIndex: -1);
    }

    internal void AddEmptyRegion(Rectangle bounds)
    {
      if (bounds.IsEmpty) return;
      AddHitRegion(RegionKind.Empty, bounds, primaryIndex: -1, secondaryIndex: -1);
    }

    // Internals ==================================================================================

    private void AddHitRegion(RegionKind kind, Rectangle bounds, int primaryIndex, int secondaryIndex)
    {
      // bounds가 비어있으면 히트 대상이 아니므로 추가하지 않는다.
      if (bounds.IsEmpty) return;

      // "Tab류"만 secondary(인덱스)를 가진다. 그 외는 -1로 정규화한다.
      if (kind is not RegionKind.Tab and not RegionKind.TabClose and not RegionKind.AutoHideTab)
        secondaryIndex = -1;

      _HitRegions.Add(new HitRegion(kind, bounds, primaryIndex, secondaryIndex));
    }

    private static string? NormalizeKeyForQuery(string? s)
    {
      if (string.IsNullOrWhiteSpace(s)) return null;
      var t = s.Trim();
      return t.Length == 0 ? null : t;
    }

    private static bool TryGetAutoHideKeyAndPopupSize(object? contentKey, out string key, out Size? popupSize)
    {
      key = string.Empty;
      popupSize = null;

      if (contentKey is null) return false;

      if (contentKey is string s)
      {
        var nk = NormalizeKeyForQuery(s);
        if (nk is null) return false;

        key = nk;
        return true;
      }

      // 특정 타입(DockAutoHideItem 등)에 직접 의존하지 않고, PersistKey/PopupSize 패턴을 reflection으로 지원한다.
      var t = contentKey.GetType();

      var pk =
        TryGetStringMember(t, contentKey, "PersistKey")
        ?? TryGetStringMember(t, contentKey, "Key")
        ?? TryGetStringMember(t, contentKey, "Id")
        ?? TryGetStringMember(t, contentKey, "Name");

      var nk2 = NormalizeKeyForQuery(pk);
      if (nk2 is null) return false;

      key = nk2;

      popupSize =
        TryGetNullableSizeMember(t, contentKey, "PopupSize")
        ?? TryGetNullableSizeMember(t, contentKey, "Size");

      return true;
    }

    private static string? TryGetStringMember(Type t, object instance, string name)
    {
      const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

      try
      {
        var p = t.GetProperty(name, Flags);
        if (p is not null && p.CanRead)
          return p.GetValue(instance) as string;

        var f = t.GetField(name, Flags);
        if (f is not null)
          return f.GetValue(instance) as string;
      }
      catch { }

      return null;
    }

    private static Size? TryGetNullableSizeMember(Type t, object instance, string name)
    {
      const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

      try
      {
        var p = t.GetProperty(name, Flags);
        if (p is not null && p.CanRead)
          return NormalizeSizeValue(p.GetValue(instance));

        var f = t.GetField(name, Flags);
        if (f is not null)
          return NormalizeSizeValue(f.GetValue(instance));
      }
      catch { }

      return null;
    }

    private static Size? NormalizeSizeValue(object? v)
    {
      if (v is null) return null;
      if (v is Size s) return s;

      // Nullable<Size>가 boxing되면 대개 Size로 들어오지만, 안전망 유지
      if (v is Rectangle r) return r.Size;

      return null;
    }
  }
}

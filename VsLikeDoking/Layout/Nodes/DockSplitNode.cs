using System.Collections.Generic;

using VsLikeDoking.Utils;

namespace VsLikeDoking.Layout.Nodes
{
  /// <summary>레이아웃 트리에서 화면을 2분할 하는 노드다.</summary>
  /// <remarks>Fisrt/Second 두 자식을 가지고 Orientation(좌우/상하)와 Ratio(패널 비율)을 저장한다.</remarks>
  public sealed class DockSplitNode : DockNode
  {
    // Fields ====================================================================

    private DockNode _First;
    private DockNode _Second;
    private double _Ratio;

    // Properties ================================================================

    public DockSplitOrientation Orientation { get; set; }

    /// <summary>첫 번째 패널이 차지하는 비율(0~1). UI에서는 최소/최대 폭/높이 정책에 의해 보정될 수 있다.</summary>
    public double Ratio
    {
      get { return _Ratio; }
      set { _Ratio = MathEx.ClampPer(value); }
    }

    public DockNode First
      => _First;

    public DockNode Second
      => _Second;

    // Ctor ======================================================================

    public DockSplitNode(DockSplitOrientation orientation, double ratio, DockNode first, DockNode second, string? nodeId = null) : base(DockNodeKind.Split, nodeId)
    {
      Orientation = orientation;
      _Ratio = MathEx.ClampPer(ratio);
      _First = first;
      _Second = second;
      _First.SetParentInternal(this);
      _Second.SetParentInternal(this);
    }

    // Methods =================================================================

    /// <summary>지정한 자식을 다른 노드로 교체한다. 교체되면 new</summary>
    public bool ReplaceChild(DockNode oldChild, DockNode newChild)
    {
      Guard.NotNull(oldChild);
      Guard.NotNull(newChild);

      if (ReferenceEquals(_First, oldChild))
      {
        _First.SetParentInternal(null);
        _First = newChild;
        _First.SetParentInternal(this);
        return true;
      }
      if (ReferenceEquals(_Second, oldChild))
      {
        _Second.SetParentInternal(null);
        _Second = newChild;
        _Second.SetParentInternal(this);
        return true;
      }
      return false;
    }

    /// <summary>주어진 자식의 반대편 자식을 반환한다. 자식이 아니면 null</summary>
    public DockNode? GetOtherChild(DockNode child)
    {
      if (ReferenceEquals(_First, child)) return _Second;
      if (ReferenceEquals(_Second, child)) return _First;
      return null;
    }

    // DockNode ================================================================

    public override IEnumerable<DockNode> EnumerateChildren()
    {
      yield return _First;
      yield return _Second;
    }
  }

  /// <summary>Split Docking 방향</summary>
  public enum DockSplitOrientation { Vertical = 0, Horizontal = 1 }
}

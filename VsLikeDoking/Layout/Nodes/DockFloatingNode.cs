using System;
using System.Collections.Generic;
using System.Drawing;

using VsLikeDoking.Utils;

namespace VsLikeDoking.Layout.Nodes
{
  /// <summary>플로팅(분리된) 창 1개를 레이아웃 트리에서 표현하는 노드다.</summary>
  /// <remarks>내부에 Root(플로팅 창 안의 레이아웃 서브트리) 1개를 들고, 화면 위치/크기(Bounds)를 저장한다.</remarks>
  public sealed class DockFloatingNode : DockNode
  {
    // Fields ====================================================================

    private DockNode _Root;
    private Rectangle _Bounds;

    // Properties ================================================================

    /// <summary>플로팅 창 내부의 레이아웃 루트 노드</summary>
    public DockNode Root
      => _Root;

    /// <summary>플로팅 창의 화면 좌표/크기</summary>
    public Rectangle Bounds
    {
      get { return _Bounds; }
      set { _Bounds = NormalizeBounds(value); }
    }

    // Ctor ======================================================================

    /// <summary>플로팅 노드를 생성한다.</summary>
    public DockFloatingNode(DockNode root, Rectangle bounds, string? nodeId = null) : base(DockNodeKind.Floating, nodeId)
    {
      _Root = Guard.NotNull(root);
      _Root.SetParentInternal(this);
      _Bounds = NormalizeBounds(bounds);
    }

    // Methods =================================================================

    /// <summary>플로팅 창 내부 루트를 교체한다.</summary>
    public void ReplaceRoot(DockNode newRoot)
    {
      Guard.NotNull(newRoot);

      _Root.SetParentInternal(null);
      _Root = newRoot;
      _Root.SetParentInternal(this);
    }

    private static Rectangle NormalizeBounds(Rectangle rect)
    {
      // 최소 크기는 UI 정책적으로 더 크게 잡을 수 있다. 여기서는 1 이상만 보장.
      var w = Math.Max(1, rect.Width);
      int h = Math.Max(1, rect.Height);
      return new Rectangle(rect.X, rect.Y, w, h);
    }

    // DockNode ================================================================

    public override IEnumerable<DockNode> EnumerateChildren()
    {
      yield return _Root;
    }
  }
}

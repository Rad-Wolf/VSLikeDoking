using System;
using System.Collections.Generic;

using VsLikeDoking.Utils;

namespace VsLikeDoking.Layout.Nodes
{
  /// <summary>도킹 레이아숫 트리의 모든 노드 베이스</summary>
  public abstract class DockNode
  {
    // Fields ====================================================================

    private DockNode? _Parent;

    // Propertis =================================================================

    /// <summary>노드종류</summary>
    public DockNodeKind Kind { get; }

    /// <summary>노드 식별자</summary>
    /// <remarks>레이아웃 저장/디버그/추적 용도</remarks>
    public string NodeId { get; }

    /// <summary>부모 노드</summary>
    /// <remarks>트리 구성 시 내부에서 설정</remarks>
    public DockNode? Parent => _Parent;

    // Ctor ======================================================================

    /// <summary>노드를 생성한다.</summary>
    /// <param name="kind">노드의 종류</param>
    /// <param name="nodeId">식별자. null/공백이면 자동 생성</param>
    protected DockNode(DockNodeKind kind, string? nodeId = null)
    {
      Kind = kind;
      if (string.IsNullOrWhiteSpace(nodeId)) NodeId = CreateNodeId();
      else NodeId = Guard.NotNullOrWhiteSpace(nodeId).Trim();
    }

    // Parent helpers ============================================================

    /// <summary>내부에서만 부모를 설정한다. (Mutator/Validator/Ui 빌더에서 사용)</summary>
    internal void SetParentInternal(DockNode? parent)
      => _Parent = parent;

    // Children ==================================================================

    /// <summary>자식 노드를 열거한다.(없으면 빈 열거)</summary>
    public abstract IEnumerable<DockNode> EnumerateChildren();

    /// <summary>현재 노드를 루트로 하여 깊이 우선으로 순회한다.</summary>
    /// <param name="includeSelf">true면 현재 노드도 포함</param>
    public IEnumerable<DockNode> TraverseDepthFirst(bool includeSelf = true)
    {
      if (includeSelf) yield return this;

      foreach (var child in EnumerateChildren())
      {
        if (child is null) continue;

        foreach (var node in child.TraverseDepthFirst(true))
          yield return node;
      }
    }

    // Id ========================================================================

    /// <summary>노드 식별자를 생성한다.</summary>
    protected static string CreateNodeId()
      => Guid.NewGuid().ToString("N");

    public override string ToString()
      => $"{Kind}:{NodeId}";
  }

  /// <summary>도킹 레이아웃 노드 종류</summary>
  public enum DockNodeKind
  {
    Group = 0,
    Split = 1,
    Floating = 2,
    AutoHide = 3
  }
}

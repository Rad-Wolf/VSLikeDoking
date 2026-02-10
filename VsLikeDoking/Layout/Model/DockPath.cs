using System;
using System.Collections.Generic;
using System.Text;

using VsLikeDoking.Layout.Nodes;
using VsLikeDoking.Utils;

namespace VsLikeDoking.Layout.Model
{
  /// <summary>레이아웃 트리 안에서 특정 노드를 "경로(루트->타겟 NodeId 체인)"로 안정적으로 가리키는 값 객체다.</summary>
  /// <remarks>레이아웃 저장/복원, 트리 변형(Mutator), UI 히트테스트 결과를 "어느 노드에 적용할지" 연결할 때 기준이 된다.</remarks>
  public readonly struct DockPath : IEquatable<DockPath>
  {

    // Fields ====================================================================

    private readonly string[] _NodeIds;

    // Properties ================================================================

    /// <summary>경로가 비어있는지 여부.</summary>
    public bool IsEmpty
      => _NodeIds is null || _NodeIds.Length == 0;

    /// <summary>경로 길이(노드 개수)</summary>
    public int Length
      => _NodeIds?.Length ?? 0;

    /// <summary>루트 노드의 NodeId. 비어있으면 null</summary>
    public string? RootId
      => (IsEmpty ? null : _NodeIds[0]);

    /// <summary>타겟 노드의 NodeId. 비어있으면 null</summary>
    public string? NodeId
      => (IsEmpty ? null : _NodeIds[_NodeIds.Length - 1]);

    /// <summary>인덱서(0=루트, 마지막 = 타겟)</summary>
    public string this[int index]
      => _NodeIds[index];

    // Ctor ======================================================================

    /// <summary>루트부터 타겟까지 NodeId 체인으로 DockPath를 생성한다.</summary>
    public DockPath(IEnumerable<string> nodeIds)
    {
      Guard.NotNull(nodeIds);

      List<string> list = new();
      foreach (var id in nodeIds)
      {
        if (string.IsNullOrWhiteSpace(id)) continue;
        list.Add(id.Trim());
      }

      if (list.Count == 0) throw new ArgumentException("DockPath에는 최소한 하나의 NodeId가 필요합니다.", nameof(nodeIds));

      _NodeIds = list.ToArray();
    }

    private DockPath(string[] nodeIds)
    {
      _NodeIds = nodeIds;
    }

    // Factory ===================================================================

    /// <summary>노드의 Parent 체인을 따라 루트부터 타겟까지의 DockPath를 생성한다.</summary>
    public static DockPath FromNode(DockNode node)
    {
      Guard.NotNull(node);

      var stack = new Stack<string>();
      var cur = node;
      while (cur is not null)
      {
        stack.Push(cur.NodeId);
        cur = cur.Parent!;
      }

      return new DockPath(stack.ToArray());
    }

    /// <summary>"id1/id2/id3" 형태의 문자열을 DockPath로 파싱한다.</summary>
    public static DockPath Parse(string pathText)
    {
      Guard.NotNullOrWhiteSpace(pathText);

      var parts = pathText.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      if (parts.Length == 0) throw new FormatException("잘못된 DockPath 문자열입니다.");

      return new DockPath(parts);
    }

    /// <summary>"id1/id2/id3" 형태의 문자열을 DockPath로 파싱한다. 실패 시 false</summary>
    public static bool TryParse(string? pathText, out DockPath path)
    {
      path = default;

      if (string.IsNullOrWhiteSpace(pathText)) return false;

      try
      {
        path = Parse(pathText);
        return true;
      }
      catch
      {
        return false;
      }
    }

    // Resolve ==================================================================

    /// <summary>경로를 기준으로 루투에서 타겟 노드를 해석한다.(경로가 깨졌으면 null)</summary>
    public DockNode? Resolve(DockNode root)
    {
      Guard.NotNull(root);
      if (IsEmpty) return null;

      if (!string.Equals(root.NodeId, _NodeIds[0], StringComparison.Ordinal))
      {
        //루트 id가 다르면, 마지막 id로 전체 탐색(복구용)
        return FindById(root, _NodeIds[_NodeIds.Length - 1]);
      }

      DockNode current = root;

      for (int i = 1; i < _NodeIds.Length; i++)
      {
        var nextId = _NodeIds[i];
        var found = FindDirectChildById(current, nextId);
        if (found is null) return null;
        current = found;
      }

      return current;
    }

    /// <summary>루트에서 특정 NodeId를 깊이 우선으로 탐색한다.</summary>
    public static DockNode? FindById(DockNode root, string nodeId)
    {
      Guard.NotNull(root);
      Guard.NotNullOrWhiteSpace(nodeId);

      var id = nodeId.Trim();

      foreach (var node in root.TraverseDepthFirst(true))
      {
        if (string.Equals(node.NodeId, id, StringComparison.Ordinal)) return node;
      }

      return null;
    }

    private static DockNode? FindDirectChildById(DockNode parent, string nodeId)
    {
      foreach (var child in parent.EnumerateChildren())
      {
        if (string.Equals(child.NodeId, nodeId, StringComparison.Ordinal)) return child;
      }
      return null;
    }

    // String ====================================================================

    /// <summary>"id1/id2/id3" 형태로 문자열을 반환한다.</summary>
    public override string ToString()
    {
      if (IsEmpty) return string.Empty;

      var sb = new StringBuilder();
      for (int i = 0; i < _NodeIds.Length; i++)
      {
        if (i > 0) sb.Append('/');
        sb.Append(NodeId[i]);
      }

      return sb.ToString();
    }

    // Equality ==================================================================

    public bool Equals(DockPath other)
    {
      if (IsEmpty && other.IsEmpty) return true;
      if (Length != other.Length) return false;

      for (int i = 0; i < _NodeIds.Length; i++)
      {
        if (!string.Equals(_NodeIds[i], other._NodeIds[i], StringComparison.Ordinal)) return false;
      }

      return true;
    }

    public override bool Equals(object? obj)
      => obj is DockPath p && Equals(p);

    public override int GetHashCode()
    {
      if (IsEmpty) return 0;

      unchecked // 오버플로가 확실하거나 어쩌구. 내가 의도한거다. 에러 아님! 이라고 하는 키워드
      {
        int h = 17;
        for (int i = 0; i < _NodeIds.Length; i++)
        {
          h = h * 31 + _NodeIds[i].GetHashCode();
        }
        return h;
      }
    }

    public static bool operator ==(DockPath left, DockPath right)
      => left.Equals(right);
    public static bool operator !=(DockPath left, DockPath right)
      => !left.Equals(right);
  }
}

using System;
using System.Collections.Generic;

using VsLikeDoking.Abstractions;
using VsLikeDoking.Utils;

namespace VsLikeDoking.Core
{
  /// <summary>PersistKey↔IDockContent 인스턴스 매핑을 관리하는 컨텐츠 레시트스리다.</summary>
  /// <remarks>레이아웃 복원 시 IDockContentFactory로 컨텐츠를 생성하거나, 이미 생성된 컨텐츠를 재사용할 때 여기에서 찾는다. 또한 컨텐츠 추가/제거/닫힘 이벤트를 DockEvents로 올릴 수 있다.</remarks>
  public class DockRegistry
  {
    // Fields ====================================================================

    private readonly Dictionary<string, IDockContent> _ByKey = new Dictionary<string, IDockContent>(StringComparer.Ordinal);
    private readonly IDockContentFactory? _Factory;
    private readonly DockEvents? _Events;

    // Settings ==================================================================

    /// <summary>문서(Document) 컨텐츠를 Close 시 Dispose 할지 여부.</summary>
    public bool DisposeDocumentsOnClose { get; set; } = true;

    /// <summary>도구창(ToolWindow) 컨텐츠를 Close 시 Dispose 할지 여부.</summary>
    public bool DisposeToolWindowsOnClose { get; set; } = false;

    // Ctor ======================================================================

    /// <summary>레지스트리를 생성</summary>
    public DockRegistry(IDockContentFactory? factory = null, DockEvents? events = null)
    {
      _Factory = factory;
      _Events = events;
    }

    // Query ====================================================================

    /// <summary>등록된 컨텐츠 개수</summary>
    public int Count
      => _ByKey.Count;

    /// <summary>PersistKey로 등록된 컨텐츠를 가져온다. 없으면 null.</summary>
    public IDockContent? Get(string persistKey)
    {
      Guard.NotNullOrWhiteSpace(persistKey);
      _ByKey.TryGetValue(persistKey.Trim(), out var content);
      return content;
    }

    /// <summary>PersistKey로 등록된 컨텐츠를 가져온다. 없으면 false</summary>
    public bool TryGet(string persistKey, out IDockContent? content)
    {
      Guard.NotNullOrWhiteSpace(persistKey);
      return _ByKey.TryGetValue(persistKey.Trim(), out content);
    }

    /// <summary>현재 등록된 모든 컨텐츠를 열거한다.</summary>
    public IEnumerable<IDockContent> EnumerateAll()
    {
      foreach (var v in _ByKey)
        yield return v.Value;
    }

    // Register ==================================================================

    /// <summary>컨텐츠를 등록한다. 같은 PersistKey가 이미 있으면 false</summary>
    public bool Register(IDockContent content)
    {
      Guard.NotNull(content);

      var key = Guard.NotNullOrWhiteSpace(content.PersistKey).Trim();
      if (_ByKey.ContainsKey(key)) return false;

      _ByKey[key] = content;
      _Events?.RaiseContentAdded(content);
      return true;
    }

    /// <summary>PersistKey로 컨텐츠를 생성(Factory)하여 등록한다. 지원하지 않거나 실패하면 null</summary>
    public IDockContent? CreateAndRegister(string persistKey)
    {
      Guard.NotNullOrWhiteSpace(persistKey);

      var key = persistKey.Trim();

      if (_ByKey.TryGetValue(key, out var existing)) return existing;
      if (_Factory is null) return null;

      IDockContent? created = _Factory.Create(key);
      if (created is null) return null;

      if (!string.Equals(created.PersistKey, key, StringComparison.Ordinal))
        throw new InvalidOperationException($"_Factory 에서 다른 PersistKey로 컨텐츠를 반환했습니다. Request = '{key}', Retruned = '{created.PersistKey}'");

      Register(created);
      return created;
    }

    /// <summary>PersistKey의 컨텐츠를 보장한다. 없으면 Factory로 생성하여 등록한다.</summary>
    public IDockContent? Ensure(string persistKey)
    {
      Guard.NotNullOrWhiteSpace(persistKey);

      var key = persistKey.Trim();
      if (_ByKey.TryGetValue(key, out var existing)) return existing;

      return CreateAndRegister(key);
    }

    // Remove/Close ============================================================

    /// <summary>등록만 해제한다(Dispose/Close 이벤트 없음). 제거되면 true</summary>
    public bool Unregister(string persistKey)
    {
      Guard.NotNullOrWhiteSpace(persistKey);

      var key = persistKey.Trim();
      if (!_ByKey.TryGetValue(key, out var content)) return false;

      _ByKey.Remove(key);
      _Events?.RaiseContentRemoved(content);
      return true;
    }

    /// <summary>컨텐츠를 닫는다(레지스트리에서 제거 + Closed 이벤트 + (옵션) Dispose).</summary>
    public bool Close(string persistKey)
    {
      Guard.NotNullOrWhiteSpace(persistKey);

      var key = persistKey.Trim();
      if (!_ByKey.TryGetValue(key, out var content)) return false;
      if (!content.CanClose) return false;

      _ByKey.Remove(key);
      _Events?.RaiseContentClosed(content);
      _Events?.RaiseContentRemoved(content);

      if (ShouldDisposeOnClose(content))
        TryDisposeContent(content);

      return true;
    }

    /// <summary>모든 컨텐츠를 레지스트리에서 제거한다. dispose=true면 설정을 무시하고 전부 Dispose 시도한다.</summary>
    public void Clear(bool dispose = false)
    {
      if (_ByKey.Count == 0) return;

      var list = new List<IDockContent>(_ByKey.Count);
      foreach (var kv in _ByKey) list.Add(kv.Value);

      _ByKey.Clear();

      for (int i = 0; i < list.Count; i++)
      {
        var c = list[i];
        _Events?.RaiseContentRemoved(c);
        if (dispose) TryDisposeContent(c);
      }
    }


    // Helpers ==================================================================

    private bool ShouldDisposeOnClose(IDockContent content)
    {
      if (content.Kind == DockContentKind.Document) return DisposeDocumentsOnClose;
      if (content.Kind == DockContentKind.ToolWindow) return DisposeToolWindowsOnClose;
      return false;
    }

    private static void TryDisposeContent(IDockContent content)
    {
      // content 자체가 IDisposable이면 정리
      if (content is IDisposable d1)
      {
        try { d1.Dispose(); } catch { }
      }

      // View(Control)도 IDisposable이지만, 부모 컨테이너가 Dispose할 수도 있으므로 여기서는 선택적으로만 처리.
      // 기본 정책은 "content Dispose에 맡긴다".
    }
  }
}

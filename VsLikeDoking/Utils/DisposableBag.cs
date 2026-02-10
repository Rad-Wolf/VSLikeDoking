using System;
using System.Collections.Generic;

namespace VsLikeDoking.Utils
{
  /// <summary>특정 객체의 수명 ( scope ) 에 종속된 IDisposable 수집/정리 컨테이너</summary>
  /// <remarks>전역 공유(싱글턴) 자원 관리자가 아님.</remarks>
  public class DisposableBag : IDisposable
  {
    private readonly List<IDisposable> _Items = new();
    private bool _Disposed;

    // Add ======================================================================

    /// <summary>항목을 추가한다.</summary>
    /// <remarks>null 이면 무시한다.</remarks>
    public void Add(IDisposable? item)
    {
      if (item is null) return;
      if (_Disposed)
      {
        item.Dispose();
        return;
      }
      _Items.Add(item);
    }

    /// <summary>항목을 추가하고 그대로 반환</summary>
    /// <remarks>null 이라면 null 반환</remarks>
    public T? Add<T>(T? item) where T : class, IDisposable
    {
      if (item is null) return null;
      Add((IDisposable)item);
      return item;
    }

    /// <summary>등록된 항목 수</summary>
    public int Count => _Items.Count;

    public void Dispose()
    {
      if (_Disposed) return;
      _Disposed = true;

      for (int i = _Items.Count - 1; i >= 0; i--)
      {
        try { _Items[i].Dispose(); }
        catch { /*dispose 에서 예외를 올리면 정리 중단 위험. 필요하면 Disagnostics로 훅을 달것*/ }
      }
      _Items.Clear();
    }
  }
}
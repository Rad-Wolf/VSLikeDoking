using System;

using VsLikeDoking.Abstractions;
using VsLikeDoking.Layout.Nodes;

namespace VsLikeDoking.Core
{
  /// <summary>DockManager가 레이아웃/컨텐츠 변화(레이아웃 변경,활성 탭 변경, 컨텐츠 추가/제거/닫힘 등)을 외부로 통지하기 위한 이벤트 묶음이다.</summary>
  /// <remarks>실제 이벤트 발생(Raise)은 Core 내부에서만 호출하도록 internal로 둔다.</remarks>
  public class DockEvents
  {
    // Fields ====================================================================

    private int _SuppressCount;

    // Events ===================================================================

    /// <summary>레이아웃 트리(Root)가 변경되었을 때 발생</summary>
    public event EventHandler<DockLayoutChangedEventArgs>? LayoutChanged;

    /// <summary>활설 컨텐츠가 변경되었을 때 발생한다.</summary>
    public event EventHandler<DockActiveContentChangedEventArgs>? ActiveContentChanged;

    /// <summary>컨텐츠가 레지스트리에 추가되었을 때 발생한다.</summary>
    public event EventHandler<DockContentEventArgs>? ContentAdded;

    /// <summary>컨텐츠가 레지스트리에서 제거되었을 때 발생한다.</summary>
    public event EventHandler<DockContentEventArgs>? ContentRemoved;

    /// <summary>컨텐츠가 닫혔을 때 발생한다.</summary>
    public event EventHandler<DockContentEventArgs>? ContentClosed;

    // Public ====================================================================

    /// <summary>이벤트 발생을 일시적으로 억제한다. 반환된 토큰 Dispose 시 억제가 해제된다.</summary>
    public IDisposable Suppress()
    {
      _SuppressCount++;
      return new SuppressToken(this);
    }

    // Internal Raise =============================================================

    internal void RaiseLayoutChanged(DockNode? oldRoot, DockNode newRoot, string? reason = null)
    {
      if (_SuppressCount > 0) return;
      LayoutChanged?.Invoke(this, new DockLayoutChangedEventArgs(oldRoot, newRoot, reason));
    }

    internal void RaiseActiveContentChanged(IDockContent? oldContent, IDockContent? newContent)
    {
      if (_SuppressCount > 0) return;
      ActiveContentChanged?.Invoke(this, new DockActiveContentChangedEventArgs(oldContent, newContent));
    }

    internal void RaiseContentAdded(IDockContent content)
    {
      if (_SuppressCount > 0) return;
      ContentAdded?.Invoke(this, new DockContentEventArgs(content));
    }

    internal void RaiseContentRemoved(IDockContent content)
    {
      if (_SuppressCount > 0) return;
      ContentRemoved?.Invoke(this, new DockContentEventArgs(content));
    }

    internal void RaiseContentClosed(IDockContent content)
    {
      if (_SuppressCount > 0) return;
      ContentClosed?.Invoke(this, new DockContentEventArgs(content));
    }

    // Types ====================================================================

    private sealed class SuppressToken : IDisposable
    {
      private DockEvents? _Owner;

      public SuppressToken(DockEvents owner)
      {
        _Owner = owner;
      }

      public void Dispose()
      {
        if (_Owner is null) return;

        _Owner._SuppressCount = Math.Max(0, _Owner._SuppressCount - 1);
        _Owner = null;
      }
    }
  }

  /// <summary>레이아웃 트리(Root)가 변경되었을 때</summary>
  public sealed class DockLayoutChangedEventArgs : EventArgs
  {
    public DockNode? OldRoot { get; }
    public DockNode NewRoot { get; }
    public string? Reason { get; }
    public DockLayoutChangedEventArgs(DockNode? oldRoot, DockNode newRoot, string? reason)
    {
      OldRoot = oldRoot;
      NewRoot = newRoot;
      Reason = reason;
    }
  }

  /// <summary>활설 컨텐츠가 변경되었을 때</summary>
  public sealed class DockActiveContentChangedEventArgs : EventArgs
  {
    public IDockContent? OldContent { get; }
    public IDockContent? NewContent { get; }

    public DockActiveContentChangedEventArgs(IDockContent? oldContent, IDockContent? newContent)
    {
      OldContent = oldContent;
      NewContent = newContent;
    }
  }

  /// <summary>컨텐츠가 변경되었을 때</summary>
  public sealed class DockContentEventArgs : EventArgs
  {
    public IDockContent Content { get; }

    public DockContentEventArgs(IDockContent content)
    {
      Content = content ?? throw new ArgumentNullException(nameof(content));
    }
  }
}

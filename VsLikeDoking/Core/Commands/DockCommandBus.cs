using System;
using System.Collections.Generic;

using VsLikeDoking.Abstractions;

namespace VsLikeDoking.Core.Commands
{
  /// <summary>Dock 커맨드를 큐잉하고 순차 실행하는 버스.</summary>
  /// <remarks>실행은 외부에서 제공한 executor(delegate)로만 수행하여 IDockCommand 형태 변화의 영향을 최소화한다.</remarks>
  public sealed class DockCommandBus
  {
    // Field =====================================================================

    private readonly object _Sync = new();
    private readonly List<IDockCommand> _Queue = new(32); // 큐의 목적 뿐만 아니라 임의제거와 트리밍을 싸게 하려고
    private readonly Func<IDockCommand, DockCommandContext, DockCommandResult> _Executor;
    private int _Head;

    // Properties ================================================================

    /// <summary>커맨드 실행 컨텍스트</summary>
    public DockCommandContext Context { get; private set; }

    /// <summary>대기 중인 커맨드 개수</summary>
    public int PendingCount
    {
      get
      {
        lock (_Sync)
        {
          var count = _Queue.Count - _Head;
          return count < 0 ? 0 : count;
        }
      }
    }

    /// <summary>대기 커맨드 존재 여부.</summary>
    public bool HasPending
      => PendingCount > 0;

    /// <summary>같은 런타임 타입의 커맨드가 연속적으로 쌓일 때 이전 것을 제거하고 마지막 것만 유지할지 여부</summary>
    public bool CoalesceByType { get; set; } = true;

    /// <summary>커맨드가 실행된 후 호출된다</summary>
    public event Action<IDockCommand, DockCommandResult>? Executed;

    // Ctor ======================================================================

    /// <summary>커맨드 버스 생성</summary>
    /// <param name="context">실행 컨텍스트</param>
    /// <param name="executor">커맨드 실행 Delegate</param>
    public DockCommandBus(DockCommandContext context, Func<IDockCommand, DockCommandContext, DockCommandResult> executor)
    {
      Context = context ?? throw new ArgumentNullException(nameof(context));
      _Executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    // Public API ================================================================

    /// <summary>실행 컨텍스트를 교체한다.</summary>
    public void SetContext(DockCommandContext context)
    {
      if (context is null) throw new ArgumentNullException(nameof(context));
      Context = context;
    }

    /// <summary>커맨드를 큐에 추가한다.</summary>
    public void Enqueue(IDockCommand command)
    {
      if (command is null) throw new ArgumentNullException(nameof(command));

      lock (_Sync)
      {
        if (CoalesceByType) CoalesceByTypeUnsafe(command);
        _Queue.Add(command);
      }
    }

    /// <summary>대기중인 커맨드를 모두 제거한다</summary>
    public void Clear()
    {
      lock (_Sync)
      {
        _Queue.Clear();
        _Head = 0;
      }
    }

    /// <summary>다음 커맨드 1개를 실행한다.</summary>
    public bool TryProcessNext(out DockCommandResult result)
    {
      IDockCommand? cmd;

      lock (_Sync)
      {
        if (_Head >= _Queue.Count)
        {
          TrimUnsafe();
          result = DockCommandResult.NoOp();
          return false;
        }
        cmd = _Queue[_Head++];
      }
      result = ExecuteSafe(cmd);
      Executed?.Invoke(cmd, result);
      return true;
    }

    /// <summary>대기 중인 커맨드를 순차 실행한다.</summary>
    /// <param name="maxCommands">한 번에 처리할 최대 개수.</param>
    /// <returns>처리 요약 결과</returns>
    public DockCommandResult ProcessAll(int maxCommands = int.MaxValue)
    {
      if (maxCommands <= 0) return DockCommandResult.NoOp();

      var executedAny = false;
      var anyChanged = false;

      for (int i = 0; i < maxCommands; i++)
      {
        if (!TryProcessNext(out var r)) break;

        executedAny = true;

        if (r.Status == DockCommandStatus.Failed) return r;
        if (r.Status == DockCommandStatus.Canceled) return r;

        if (r.Changed) anyChanged = true;
      }
      if (!executedAny) return DockCommandResult.NoOp();
      return anyChanged ? DockCommandResult.Succeeded(true) : DockCommandResult.NoOp();
    }

    // Internals =================================================================

    private DockCommandResult ExecuteSafe(IDockCommand command)
    {
      try { return _Executor(command, Context); }
      catch (Exception e) { return DockCommandResult.Failed(e); }
    }

    private void CoalesceByTypeUnsafe(IDockCommand incoming)
    {
      var incomingType = incoming.GetType();

      for (var i = _Queue.Count - 1; i >= _Head; i--)
      {
        if (_Queue[i].GetType() != incomingType) continue;
        _Queue.RemoveAt(i);
        break;
      }
      TrimUnsafe();
    }

    private void TrimUnsafe()
    {
      if (_Head == 0) return;
      if (_Head < 64) return;
      if (_Head < _Queue.Count / 2) return;

      _Queue.RemoveRange(0, _Head);
      _Head = 0;
    }
  }
}

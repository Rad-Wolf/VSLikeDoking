using System;

namespace VsLikeDoking.Core.Commands
{
  /// <summary>Dock 커맨드 실행 상태</summary>
  public enum DockCommandStatus : byte
  {
    /// <summary>성공(레이아웃/상태 변경이 있을 수 있음</summary>
    Succeeded,
    /// <summary>정상 처리했으나 변경이 없음(예: 동일 탭 재선택)</summary>
    NoOp,
    /// <summary>사용자/시스템에 의해 취소됨</summary>
    Canceled,
    /// <summary>실패(예외/오류)</summary>
    Failed
  }
  /// <summary>Dock 커맨드 실행 결과.</summary>
  public readonly struct DockCommandResult
  {
    // Properties ================================================================

    /// <summary>실행 상태</summary>
    public DockCommandStatus Status { get; }

    /// <summary>레이아숫/상태가 실제로 변경되었는지 여부.</summary>
    public bool Changed { get; }

    /// <summary>추가 정보(선택)</summary>
    public string? Message { get; }

    /// <summary>실패 시 예외(선택)</summary>
    public Exception? Exception { get; }

    /// <summary>성공(성공/무시) 여부</summary>
    public bool IsSuccess
      => Status is DockCommandStatus.Succeeded or DockCommandStatus.NoOp;

    /// <summary>실패 여부</summary>
    public bool IsFailure
      => Status == DockCommandStatus.Failed;

    /// <summary>취소 여부</summary>
    public bool IsCanceled
      => Status == DockCommandStatus.Canceled;

    // Ctor ======================================================================

    private DockCommandResult(DockCommandStatus status, bool changed, string? message, Exception? exception)
    {
      Status = status;
      Changed = changed;
      Message = message;
      Exception = exception;
    }

    // Factories =================================================================

    /// <summary>결과 : 성공</summary>
    public static DockCommandResult Succeeded(bool changed = true, string? message = null)
      => new(DockCommandStatus.Succeeded, changed, message, null);

    /// <summary>결과 : 변경 없이 정상 처리</summary>
    public static DockCommandResult NoOp(string? message = null)
      => new(DockCommandStatus.NoOp, false, message, null);

    /// <summary>결과 : 취소</summary>
    public static DockCommandResult Canceled(string? message = null)
      => new(DockCommandStatus.Canceled, false, message, null);

    /// <summary>결과 : 실패</summary>
    public static DockCommandResult Failed(Exception exception, string? message = null)
    {
      if (exception is null) throw new ArgumentNullException(nameof(exception));
      return new(DockCommandStatus.Failed, false, message, exception);
    }

    // Overrides ================================================================

    /// <summary>디버깅을 위한 문자열을 반환한다.</summary>
    public override string ToString()
    {
      if (Status == DockCommandStatus.Failed)
      {
        var exName = Exception?.GetType().Name ?? "Exception";
        return Message is null ? $"{Status} ({exName})" : $"{Status} ({exName}) : {Message}";
      }
      if (Message is null) return Changed ? $"{Status} (Changed)" : $"{Status} (NoChange)";
      return Changed ? $"{Status} (Changed): {Message}" : $"{Status} (NoChange): {Message}";
    }
  }
}

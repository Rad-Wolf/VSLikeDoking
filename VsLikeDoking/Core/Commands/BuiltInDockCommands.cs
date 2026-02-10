using System;

using VsLikeDoking.Abstractions;

namespace VsLikeDoking.Core.Commands
{
  /// <summary>기본 커맨드의 표준 ID/이름을 정의한다.</summary>
  /// <remarks>구현(클래스/데이터)은 바뀌어도, 로그/정책(예: coalescing)은 이 ID를 기준으로 고정한다.</remarks>
  public static class BuiltInDockCommands
  {
    // IDs (Stable) ==============================================================

    /// <summary>탭 선택.</summary>
    public const string ActivateTab = "ActivateTab";

    /// <summary>탭 닫기.</summary>
    public const string CloseTab = "CloseTab";

    /// <summary>그룹 닫기.</summary>
    public const string CloseGroup = "CloseGroup";

    /// <summary>그룹을 플로팅으로 전환.</summary>
    public const string FloatGroup = "FloatGroup";

    /// <summary>그룹/탭을 목표 영역으로 도킹.</summary>
    public const string DockToTarget = "DockToTarget";

    /// <summary>스플리터 ratio 변경(드래그/프로그래매틱).</summary>
    public const string SetSplitterRatio = "SetSplitterRatio";

    /// <summary>오토하이드 토글.</summary>
    public const string ToggleAutoHide = "ToggleAutoHide";

    /// <summary>레이아웃 저장.</summary>
    public const string SaveLayout = "SaveLayout";

    /// <summary>레이아웃 복원.</summary>
    public const string LoadLayout = "LoadLayout";

    /// <summary>기본 레이아웃으로 초기화.</summary>
    public const string ResetLayout = "ResetLayout";

    /// <summary>드래그 프리뷰 시작(탭 드래그).</summary>
    public const string DragPreviewBegin = "DragPreviewBegin";

    /// <summary>드래그 프리뷰 업데이트(탭 드래그).</summary>
    public const string DragPreviewUpdate = "DragPreviewUpdate";

    /// <summary>드래그 프리뷰 커밋(드랍).</summary>
    public const string DragPreviewCommit = "DragPreviewCommit";

    /// <summary>드래그 프리뷰 취소.</summary>
    public const string DragPreviewCancel = "DragPreviewCancel";

    // Helpers ==================================================================

    /// <summary>고빈도(연속 입력) 커맨드인지 여부를 반환한다.</summary>
    /// <remarks>예: 스플리터 드래그 / 드래그 프리뷰 업데이트 등은 버스에서 coalescing 대상으로 삼기 쉽다.</remarks>
    public static bool IsHighFrequencyId(string id)
    {
      if (id is null) throw new ArgumentNullException(nameof(id));

      return id switch
      {
        SetSplitterRatio => true,
        DragPreviewUpdate => true,
        _ => false
      };
    }

    /// <summary>커맨드의 디버그용 이름을 얻는다.</summary>
    /// <remarks>표준 ID를 모르면 타입명으로 폴백</remarks>
    public static string GetDebugName(IDockCommand command, string? id = null)
    {
      if (command is null) throw new ArgumentNullException(nameof(command));
      return string.IsNullOrEmpty(id) ? command.GetType().Name : id!;
    }
  }
}

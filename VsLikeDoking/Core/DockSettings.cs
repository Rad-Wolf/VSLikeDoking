using System;
using System.Drawing;

using VsLikeDoking.Utils;

namespace VsLikeDoking.Core
{
  public sealed class DockSettings
  {
    // Defaults ==================================================================

    public static DockSettings Default
      => new DockSettings();

    // Layout ===================================================================

    /// <summary>Splitter 두께(px)</summary>
    public int SplitterThickness { get; set; } = 6;

    /// <summary>패널 최소 크기(px)</summary>
    public int MinPaneSize { get; set; } = 80;

    /// <summary>Split ratio 최소 보정값(안전값)</summary>
    public double MinSplitRatio { get; set; } = 0.05;

    /// <summary>Split ratio 최대 보정값(안전값)</summary>
    public double MaxSplitRatio { get; set; } = 0.95;

    /// <summary>Layout 적용 시 Validate 정리/수행 여부</summary>
    public bool ValidateLayoutOnApply { get; set; } = true;

    // Tabs/Captions ============================================================

    /// <summary>탭 스트립 높이(px)</summary>
    public int TabStripHeight { get; set; } = 26;
    /// <summary>도구창 캡션 높이</summary>
    public int ToolCaptionsHeight { get; set; } = 24;

    /// <summary>탭/캡션 닫기 버튼 표시 여부</summary>
    public bool ShowCloseButton { get; set; } = true;

    /// <summary>탭 드래그로 순서 변경 허용</summary>
    public bool AllowTabReorder { get; set; } = true;

    /// <summary>탭 트래그로 플로팅(tear-off) 허용</summary>
    public bool AllowTabTearOff { get; set; } = true;

    // DragDrop =================================================================

    /// <summary>드래그 시작 거리(px)(마우스 다운 후 이 거리 이상 움직이면 드래그로 간주)</summary>
    public int DragStartDistance { get; set; } = 6;

    /// <summary>드롭 프리뷰(하이라이트) 불투명도(0.0~1.0)</summary>
    public double DockPreviewOpacity { get; set; } = 0.35;

    // Floating ==================================================================

    /// <summary>플로팅 최소 크기</summary>
    public Size FloatingMinSize { get; set; } = new Size(240, 160);

    /// <summary>플로팅 기본 크기(처음 만들 때)</summary>
    public Size FloatingDefaultSize { get; set; } = new Size(900, 650);

    // AutoHide =================================================================

    /// <summary>오토하이드 팝업 지연(ms)</summary>
    public int AutoHidePopupDelayMs { get; set; } = 250;

    /// <summary>오토하이드 팝업 숨김 지연(ms)</summary>
    public int AutoHideHideDelayMs { get; set; } = 250;

    // Rendering ================================================================

    /// <summary>컨트롤 DoublcBuffered 사용 여부(가능한 범위에서)</summary>
    public bool UseDoubleBuffered { get; set; } = true;

    // JSON ====================================================================

    /// <summary>레이아웃 JSON저장 시 들여쓰기</summary>
    public bool JsonWriteIndented { get; set; } = true;

    // Validate ==================================================================

    /// <summary>설정값을 안전 범위로 보정한다.</summary>
    public DockSettings Normalize()
    {
      SplitterThickness = Math.Max(1, SplitterThickness);
      MinPaneSize = Math.Max(0, MinPaneSize);
      TabStripHeight = Math.Max(16, TabStripHeight);
      ToolCaptionsHeight = Math.Max(16, ToolCaptionsHeight);
      DragStartDistance = Math.Max(0, DragStartDistance);

      DockPreviewOpacity = MathEx.ClampPer(DockPreviewOpacity);

      if (FloatingMinSize.Width < 1) FloatingMinSize = new Size(1, FloatingMinSize.Height);
      if (FloatingMinSize.Height < 1) FloatingMinSize = new Size(FloatingMinSize.Width, 1);

      if (FloatingDefaultSize.Width < FloatingMinSize.Width) FloatingDefaultSize = new Size(FloatingMinSize.Width, FloatingDefaultSize.Height);
      if (FloatingDefaultSize.Height < FloatingMinSize.Height) FloatingDefaultSize = new Size(FloatingDefaultSize.Width, FloatingMinSize.Height);

      AutoHidePopupDelayMs = Math.Max(0, AutoHidePopupDelayMs);
      AutoHideHideDelayMs = Math.Max(0, AutoHideHideDelayMs);

      if (MinSplitRatio > MaxSplitRatio) (MinSplitRatio, MaxSplitRatio) = (MaxSplitRatio, MinSplitRatio);
      MinSplitRatio = MathEx.Clamp(MinSplitRatio, 0.0, 0.49);
      MaxSplitRatio = MathEx.Clamp(MaxSplitRatio, 0.51, 1.0);

      return this;
    }
  }
}

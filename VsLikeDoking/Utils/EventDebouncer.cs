using System;
using System.Windows.Forms;

namespace VsLikeDoking.Utils
{
  /// <summary>짧은 시간에 연속적으로 발생하는 이벤트를 묶어 마지막 호출 이후 일정 시간이 지나면 한 번만 실행</summary>
  /// <remarks>WinForms UI 스레드에서 쓴다는 전제를 가진다. Form/Control의 필드1개를 두고 Dispose시 함께 정리</remarks>
  public class EventDebouncer
  {
    // Fields ====================================================================

    private readonly Timer _Timer;
    private Action? _Action;
    private bool _Disposed;

    // Ctor ======================================================================

    /// <summary>디바운서 생성</summary>
    /// <remarks>intervalMs = 최종 호출 이후 실행까지의 지연(ms). 최소 1ms로 보정</remarks>
    public EventDebouncer(int intervalMs = 150)
    {
      _Timer = new();
      _Timer.Interval = Math.Max(1, intervalMs);
      _Timer.Tick += OnTick;
    }

    // Properties ================================================================

    /// <summary>지연시간(ms)</summary>
    /// <remarks>최소1ms로 보정된다.</remarks>
    public int IntervalMs
    {
      get { return _Timer.Interval; }
      set { _Timer.Interval = Math.Max(1, value); }
    }

    // Methods =================================================================

    /// <summary>Action 실행을 예약한다. 같은 구간 내 다시 호출되면 이전 예약은 취소되고 마지막 Action만 실행된다.</summary>
    public void Debounce(Action action)
    {
      if (_Disposed) return;
      if (action is null) throw new ArgumentNullException(nameof(action));

      _Action = action;
      _Timer.Stop();
      _Timer.Start();
    }
    /// <summary>예약된 실행이 있다면 취소한다.</summary>
    public void Cancel()
    {
      if (_Disposed) return;
      _Action = null;
      _Timer.Stop();
    }

    // Tick ======================================================================

    private void OnTick(object? s, EventArgs e)
    {
      _Timer.Stop();

      var a = _Action;
      _Action = null;
    }

    // Dispose ==================================================================

    public void Dispose()
    {
      if (_Disposed) return;
      _Disposed = true;

      _Action = null;
      _Timer.Stop();
      _Timer.Tick -= OnTick;
      _Timer.Dispose();
    }
  }
}
using System;
using System.Reflection;
using System.Windows.Forms;

using VsLikeDoking.Core;
using VsLikeDoking.Layout.Nodes;
using VsLikeDoking.Rendering;
using VsLikeDoking.Utils;

namespace VsLikeDoking.UI.Host
{
  /// <summary>최상위 도킹 호스트 컨트롤</summary>
  /// <remarks>DockManager / VsDockRenderer 보관, Root DockNode 반영, Invalidate 정책의 진입점</remarks>
  public sealed class DockHostControl : UserControl
  {
    // Fields =====================================================================================

    private readonly Control _Surface;
    private bool _InvalidateQueued;

    private DockManager? _Manager;
    private VsDockRenderer? _Renderer;
    private DockNode? _Root;

    // Properties ==================================================================================

    /// <summary>도킹 운영(변경 적용/검증/루트 반영)의 중심 매니저</summary>
    public DockManager? Manager => _Manager;

    /// <summary>도킹 렌더러(VS 룩)</summary>
    public VsDockRenderer? Renderer => _Renderer;

    /// <summary>현재 루트 레이아웃 노드</summary>
    public DockNode? Root => _Root;

    /// <summary>초기화 여부</summary>
    public bool IsInitialized => _Manager is not null && _Renderer is not null;

    // Events ======================================================================================

    /// <summary>루트 레이아웃이 변경되면 발생한다.</summary>
    public event EventHandler? RootChanged;

    // Ctor ========================================================================================

    /// <summary>Dock Host Control 생성</summary>
    public DockHostControl()
    {
      SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

      _Surface = CreateSurfaceControl();
      _Surface.Dock = DockStyle.Fill;
      Controls.Add(_Surface);
    }

    // Initialization ==============================================================================

    /// <summary>가장 단순한 초기화. 기본 DockManager / VsDockRenderer를 생성하고 Surface 바인딩까지 완료한다.</summary>
    public void Initialize()
    {
      ThrowIfDisposed();
      Initialize(CreateDefaultManager(), CreateDefaultRenderer());
    }

    /// <summary>DockManager만 제공하는 초기화. 기본 VsDockRenderer를 생성하고 Surface 바인딩까지 완료한다.</summary>
    public void Initialize(DockManager manager)
    {
      ThrowIfDisposed();
      Guard.NotNull(manager);
      Initialize(manager, CreateDefaultRenderer());
    }

    /// <summary>VsDockRenderer만 제공하는 초기화. 기본 DockManager를 생성하고 Surface 바인딩까지 완료한다.</summary>
    public void Initialize(VsDockRenderer renderer)
    {
      ThrowIfDisposed();
      Guard.NotNull(renderer);
      Initialize(CreateDefaultManager(), renderer);
    }

    /// <summary>초기화. 주어진 DockManager / VsDockRenderer를 바인딩하고 Root를 동기화한다.</summary>
    public void Initialize(DockManager manager, VsDockRenderer renderer)
    {
      ThrowIfDisposed();
      Guard.NotNull(manager);
      Guard.NotNull(renderer);

      if (ReferenceEquals(_Manager, manager) && ReferenceEquals(_Renderer, renderer) && IsInitialized)
      {
        SetRootInternal(manager.Root, raiseEvent: false, forceApplyToSurface: true);
        ApplyBindingsToSurface();
        RequestRender();
        return;
      }

      DetachManagerEvents(_Manager);

      _Manager = manager;
      _Renderer = renderer;

      AttachManagerEvents(_Manager);

      // Host에 Root가 먼저 지정된 상태면, 초기화 시 Manager에 반영을 시도한다.
      if (_Root is not null && !ReferenceEquals(_Root, _Manager.Root))
      {
        try { _Manager.ApplyLayout(_Root, "UI:Host:Initialize:UseHostRoot", null); }
        catch { }
      }

      SetRootInternal(_Manager.Root, raiseEvent: false, forceApplyToSurface: true);

      ApplyBindingsToSurface();
      RequestRender();
    }

    /// <summary>
    /// 기본 레이아웃(좌:문서 / 우:도구창 / 하단:도구창)을 생성해 적용한 뒤 초기화한다.
    /// </summary>
    /// <remarks>
    /// - CreateDefaultLayout(double,double) 또는 CreateDefaultLayout()을 리플렉션으로 탐색한다.
    /// - 실패하면 DockManager 기본 루트를 그대로 사용한다.
    /// </remarks>
    public void InitializeWithDefaultLayout(double documentWidthRatio = 0.78, double topHeightRatio = 0.78)
    {
      ThrowIfDisposed();

      var manager = CreateDefaultManager();
      var renderer = CreateDefaultRenderer();

      var root = TryCreateDefaultLayout(documentWidthRatio, topHeightRatio);
      if (root is not null)
      {
        try { manager.ApplyLayout(root, "UI:Host:InitDefaultLayout", null); }
        catch { }
      }

      Initialize(manager, renderer);
    }

    /// <summary>Root를 변경한다. Manager가 연결되어 있으면 Manager에 우선 적용한다.</summary>
    public void SetRoot(DockNode? root)
    {
      ThrowIfDisposed();

      // Root=null은 DockManager 설계(Non-Null Root)와 충돌한다.
      // 이 경우 Host/Surface 표시만 변경한다.
      if (root is null)
      {
        SetRootInternal(null, raiseEvent: true, forceApplyToSurface: true);
        RequestRender();
        return;
      }

      if (_Manager is not null)
      {
        try
        {
          _Manager.ApplyLayout(root, "UI:Host:SetRoot", null);
          return; // LayoutChanged 이벤트에서 Host/Surface 동기화됨
        }
        catch
        {
          // 폴백 : Manager 적용 실패 시 Host/Surface만이라도 반영
        }
      }

      SetRootInternal(root, raiseEvent: true, forceApplyToSurface: true);
      RequestRender();
    }

    // Render Policy ===============================================================================

    /// <summary>렌더를 요청한다(Invalidate coalescing).</summary>
    public void RequestRender()
    {
      if (IsDisposed) return;

      if (!IsHandleCreated)
      {
        _InvalidateQueued = true;
        return;
      }

      if (_InvalidateQueued) return;
      _InvalidateQueued = true;

      try { BeginInvoke(new Action(FlushInvalidate)); }
      catch { _InvalidateQueued = false; }
    }

    // Overrides ===================================================================================

    /// <summary>핸들이 생성되면 보류된 Invalidate를 처리한다.</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
      base.OnHandleCreated(e);

      ApplyBindingsToSurface();

      if (_InvalidateQueued)
      {
        try { BeginInvoke(new Action(FlushInvalidate)); }
        catch { _InvalidateQueued = false; }
      }
    }

    /// <summary>크기 변경 시 렌더를 요청한다.</summary>
    protected override void OnSizeChanged(EventArgs e)
    {
      base.OnSizeChanged(e);
      RequestRender();
    }

    /// <summary>리소스/구독을 해제한다.</summary>
    protected override void Dispose(bool disposing)
    {
      if (disposing)
      {
        DetachManagerEvents(_Manager);

        // Surface가 DockSurfaceControl이면 내부 이벤트를 풀 수 있게 null 주입 시도(리플렉션 기반)
        TrySetSurfaceProperty("Manager", null);
        TrySetSurfaceProperty("Renderer", null);
        TrySetSurfaceProperty("Root", null);

        _Manager = null;
        _Renderer = null;
        _Root = null;
        _InvalidateQueued = false;
      }

      base.Dispose(disposing);
    }

    // Manager Events ==============================================================================

    private void AttachManagerEvents(DockManager? manager)
    {
      if (manager is null) return;
      manager.Events.LayoutChanged += OnManagerLayoutChanged;
    }

    private void DetachManagerEvents(DockManager? manager)
    {
      if (manager is null) return;
      manager.Events.LayoutChanged -= OnManagerLayoutChanged;
    }

    private void OnManagerLayoutChanged(object? sender, DockLayoutChangedEventArgs e)
    {
      if (IsDisposed) return;

      if (!IsHandleCreated)
      {
        SetRootInternal(e.NewRoot, raiseEvent: true, forceApplyToSurface: true);
        _InvalidateQueued = true;
        return;
      }

      if (InvokeRequired)
      {
        try { BeginInvoke(new Action(() => OnManagerLayoutChanged(sender, e))); }
        catch
        {
          SetRootInternal(e.NewRoot, raiseEvent: true, forceApplyToSurface: true);
          RequestRender();
        }
        return;
      }

      SetRootInternal(e.NewRoot, raiseEvent: true, forceApplyToSurface: true);
      RequestRender();
    }

    // Internals ===================================================================================

    private void SetRootInternal(DockNode? root, bool raiseEvent, bool forceApplyToSurface)
    {
      var sameRef = ReferenceEquals(_Root, root);
      if (sameRef && !forceApplyToSurface) return;

      _Root = root;

      ApplyRootToSurface(force: sameRef && forceApplyToSurface);

      if (raiseEvent) RootChanged?.Invoke(this, EventArgs.Empty);
    }

    private void FlushInvalidate()
    {
      if (IsDisposed) return;

      _InvalidateQueued = false;

      if (!_Surface.IsDisposed) _Surface.Invalidate();
      else Invalidate();
    }

    private Control CreateSurfaceControl()
    {
      // DockSurfaceControl이 존재하면 자동 생성한다(파일 단독 컴파일/의존 최소화를 위해 직접 참조하지 않음)
      const string surfaceTypeName = "VsLikeDoking.UI.Host.DockSurfaceControl";

      var assembly = typeof(DockHostControl).Assembly;
      var t = assembly.GetType(surfaceTypeName, throwOnError: false, ignoreCase: false);

      if (t is not null && typeof(Control).IsAssignableFrom(t))
      {
        try { return (Control)Activator.CreateInstance(t)!; }
        catch { }
      }

      return new Control();
    }

    private void ApplyBindingsToSurface()
    {
      ApplyManagerToSurface();
      ApplyRendererToSurface();
      ApplyRootToSurface(force: false);
    }

    private void ApplyManagerToSurface()
    {
      if (_Manager is null) return;

      if (TrySetSurfaceProperty("Manager", _Manager)) return;
      TryInvokeSurfaceMethod("SetManager", _Manager);
    }

    private void ApplyRendererToSurface()
    {
      if (_Renderer is null) return;

      if (TrySetSurfaceProperty("Renderer", _Renderer)) return;
      TryInvokeSurfaceMethod("SetRenderer", _Renderer);
    }

    private void ApplyRootToSurface(bool force)
    {
      if (!force)
      {
        if (TrySetSurfaceProperty("Root", _Root)) return;
        TryInvokeSurfaceMethod("SetRoot", _Root);
        return;
      }

      // 강제 재주입 : null -> root 토글로 Surface.Root setter 내부 최적화(ReferenceEquals) 우회
      if (_Root is null)
      {
        TrySetSurfaceProperty("Root", null);
        TryInvokeSurfaceMethod("SetRoot", null);
        return;
      }

      if (TrySetSurfaceProperty("Root", null))
      {
        TrySetSurfaceProperty("Root", _Root);
        return;
      }

      if (TryInvokeSurfaceMethod("SetRoot", null))
        TryInvokeSurfaceMethod("SetRoot", _Root);
    }

    // Defaults ====================================================================================

    private static DockManager CreateDefaultManager()
      => new DockManager();

    private static VsDockRenderer CreateDefaultRenderer()
      => new VsDockRenderer();

    private static DockNode? TryCreateDefaultLayout(double documentWidthRatio, double topHeightRatio)
    {
      // DockDefaults 위치는 프로젝트 구성에 따라 이동될 수 있으므로, 여러 후보를 순회한다.
      // (직접 참조 대신 리플렉션 사용: 레이어/모듈 분리 시 DLL 사용성을 유지)
      var assembly = typeof(DockHostControl).Assembly;

      var typeNames = new[]
      {
        "VsLikeDoking.DockDefaults",
        "VsLikeDoking.Core.DockDefaults",
        "VsLikeDoking.Layout.DockDefaults",
        "VsLikeDoking.Layout.Model.DockDefaults",
        "VsLikeDoking.UI.DockDefaults",
        "VsLikeDoking.UI.Host.DockDefaults",
      };

      var argTypes2 = new[] { typeof(double), typeof(double) };
      var args2 = new object[] { documentWidthRatio, topHeightRatio };

      for (int i = 0; i < typeNames.Length; i++)
      {
        var t = assembly.GetType(typeNames[i], throwOnError: false, ignoreCase: false);
        if (t is null) continue;

        // 우선 : CreateDefaultLayout(double,double)
        var m = t.GetMethod("CreateDefaultLayout", BindingFlags.Public | BindingFlags.Static, null, argTypes2, null);
        if (m is not null && typeof(DockNode).IsAssignableFrom(m.ReturnType))
        {
          try { return (DockNode?)m.Invoke(null, args2); }
          catch { }
        }

        // 폴백 : CreateDefaultLayout()
        m = t.GetMethod("CreateDefaultLayout", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        if (m is not null && typeof(DockNode).IsAssignableFrom(m.ReturnType))
        {
          try { return (DockNode?)m.Invoke(null, Array.Empty<object>()); }
          catch { }
        }
      }

      return null;
    }

    // Reflection Helpers ==========================================================================

    private bool TrySetSurfaceProperty(string name, object? value)
    {
      var p = _Surface.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
      if (p is null) return false;
      if (!p.CanWrite) return false;

      try
      {
        p.SetValue(_Surface, value);
        return true;
      }
      catch
      {
        return false;
      }
    }

    private bool TryInvokeSurfaceMethod(string name, object? arg)
    {
      var methods = _Surface.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);

      var argType = arg?.GetType();

      MethodInfo? best = null;
      Type? bestParamType = null;

      for (int i = 0; i < methods.Length; i++)
      {
        var mi = methods[i];

        if (!string.Equals(mi.Name, name, StringComparison.Ordinal)) continue;

        var ps = mi.GetParameters();
        if (ps.Length != 1) continue;

        var pType = ps[0].ParameterType;

        if (arg is null)
        {
          if (!CanAcceptNull(pType)) continue;

          // null일 때는 가장 구체적인(좁은) 타입을 선호한다.
          if (best is null)
          {
            best = mi;
            bestParamType = pType;
            continue;
          }

          if (bestParamType is not null && bestParamType.IsAssignableFrom(pType))
          {
            best = mi;
            bestParamType = pType;
          }

          continue;
        }

        if (!pType.IsAssignableFrom(argType!)) continue;

        if (pType == argType)
        {
          best = mi;
          bestParamType = pType;
          break;
        }

        if (best is null)
        {
          best = mi;
          bestParamType = pType;
          continue;
        }

        if (bestParamType is not null && bestParamType.IsAssignableFrom(pType))
        {
          best = mi;
          bestParamType = pType;
        }
      }

      if (best is null) return false;

      try
      {
        best.Invoke(_Surface, new[] { arg });
        return true;
      }
      catch
      {
        return false;
      }

      static bool CanAcceptNull(Type t)
      {
        if (!t.IsValueType) return true;
        return Nullable.GetUnderlyingType(t) is not null;
      }
    }

    private void ThrowIfDisposed()
    {
      if (IsDisposed) throw new ObjectDisposedException(nameof(DockHostControl));
    }
  }
}
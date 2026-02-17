// VsLikeDocking - VsLikeDoking.Demo - Demo/Forms/DockHostUsageGuideForm.cs - DockHostUsageGuideForm - (File)

using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

using VsLikeDoking.Abstractions;
using VsLikeDoking.Core;
using VsLikeDoking.Demo.Docking;
using VsLikeDoking.Layout.Model;
using VsLikeDoking.Layout.Nodes;
using VsLikeDoking.Rendering;
using VsLikeDoking.Rendering.Theme;
using VsLikeDoking.UI.Host;

namespace VsLikeDoking.Demo.Forms
{
  internal sealed class DockHostUsageGuideForm : Form
  {
    // Fields =====================================================================================================

    private readonly MenuStrip _Menu;

    private readonly SplitContainer _Split;

    private readonly Panel _SideTop;
    private readonly Label _AutoHideState;

    private readonly TextBox _LogSide;

    private DockHostControl? _Host;

    private DockManager? _HeldManager;
    private VsDockRenderer? _HeldRenderer;
    private DemoDockContentFactory? _Factory;

    private DockManager? _BoundManager;

    private int _DocSeq;
    private int _ToolSeq;

    private readonly TextBox _LogDock;     // Tool 탭 안에 들어갈 Log 뷰(오른쪽 Log와 별개)
    private string? _LastDocKey;
    private string? _LastToolKey;
    private string? _LastAutoHideKey;      // AutoHide 후보 키(팝업 토글/Unpin용)
    private string? _LastActiveKey;        // ActiveChanged 로그용(이벤트 args가 old/new를 안 주는 버전 대응)

    private ToolStripMenuItem? _MiAutoInitOnLoad;
    private ToolStripMenuItem? _MiAutoInitOnShown;
    private ToolStripMenuItem? _MiUseDefaultLayout;

    // Ctor =======================================================================================================

    public DockHostUsageGuideForm()
    {
      Text = "DockHostControl Usage Guide Demo";
      StartPosition = FormStartPosition.CenterScreen;
      Width = 1400;
      Height = 900;

      _Menu = new MenuStrip { Dock = DockStyle.Top };

      _Split = new SplitContainer
      {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Vertical,
        SplitterWidth = 6,
        SplitterDistance = 1120,
      };

      _SideTop = new Panel { Dock = DockStyle.Top, Height = 54 };

      _AutoHideState = new Label
      {
        Dock = DockStyle.Fill,
        Height = 20,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(6, 0, 6, 0),
        Text = "AutoHide: -",
      };

      _SideTop.Controls.Add(_AutoHideState);

      _LogSide = CreateLogTextBox();

      _Split.Panel2.Controls.Add(_LogSide);
      _Split.Panel2.Controls.Add(_SideTop);

      _LogDock = CreateLogTextBox();
      _LogDock.ReadOnly = true;

      Controls.Add(_Split);
      Controls.Add(_Menu);

      MainMenuStrip = _Menu;

      _DocSeq = 0;
      _ToolSeq = 0;

      BuildMenu();

      RecreateHost();
      Log("[BOOT] Usage demo form initialized.");

      Load += OnFormLoad;
      Shown += OnFormShown;
    }

    // Menu ========================================================================================================

    private void BuildMenu()
    {
      _Menu.Items.Clear();

      var init = new ToolStripMenuItem("Init");
      var actions = new ToolStripMenuItem("Actions");
      var autoHide = new ToolStripMenuItem("AutoHide");

      _MiAutoInitOnLoad = new ToolStripMenuItem("Auto Init (Load)") { CheckOnClick = true };
      _MiAutoInitOnShown = new ToolStripMenuItem("Auto Init (Shown)") { CheckOnClick = true };
      _MiUseDefaultLayout = new ToolStripMenuItem("Use DefaultLayout") { CheckOnClick = true, Checked = true };

      init.DropDownItems.Add(_MiAutoInitOnLoad);
      init.DropDownItems.Add(_MiAutoInitOnShown);
      init.DropDownItems.Add(new ToolStripSeparator());
      init.DropDownItems.Add(_MiUseDefaultLayout);
      init.DropDownItems.Add(new ToolStripSeparator());

      var miInit = new ToolStripMenuItem("Initialize() [quick start]");
      var miInitDefault = new ToolStripMenuItem("Initialize + Default Layout + Seed Content");
      var miInitProvided = new ToolStripMenuItem("Initialize with Provided Manager/Renderer/Factory");
      var miRequestRender = new ToolStripMenuItem("RequestRender()");
      var miSetRootNull = new ToolStripMenuItem("SetRoot(null)");
      var miRecreateHost = new ToolStripMenuItem("Recreate Host (reset demo runtime)");

      miInit.Click += (s, e) => RunInit(InitMode.InitializeOnly);
      miInitDefault.Click += (s, e) => RunInit(InitMode.InitializeWithDefaultLayout);
      miInitProvided.Click += (s, e) => RunInit(InitMode.InitializeWithProvided);

      miRequestRender.Click += (s, e) => _Host?.RequestRender();
      miSetRootNull.Click += (s, e) => _Host?.SetRoot(null);
      miRecreateHost.Click += (s, e) => RecreateHost();

      init.DropDownItems.Add(miInit);
      init.DropDownItems.Add(miInitDefault);
      init.DropDownItems.Add(miInitProvided);
      init.DropDownItems.Add(new ToolStripSeparator());
      init.DropDownItems.Add(miRequestRender);
      init.DropDownItems.Add(miSetRootNull);
      init.DropDownItems.Add(new ToolStripSeparator());
      init.DropDownItems.Add(miRecreateHost);

      var miAddDoc = new ToolStripMenuItem("+Doc Tab");
      var miAddTool = new ToolStripMenuItem("+Tool Tab");
      var miNewDocRight = new ToolStripMenuItem("Doc -> New Right");
      var miNewToolBottom = new ToolStripMenuItem("Tool -> New Bottom");
      var miCrossDocToTool = new ToolStripMenuItem("Doc -> Tool (Center)");
      var miCrossToolToDoc = new ToolStripMenuItem("Tool -> Doc (Center)");
      var miDump = new ToolStripMenuItem("Dump Layout");
      var miClear = new ToolStripMenuItem("Clear Log");

      miAddDoc.Click += (s, e) => AddDocTabToFirstDocGroup();
      miAddTool.Click += (s, e) => AddToolTabToFirstToolGroupOrCreate();
      miNewDocRight.Click += (s, e) => CreateNewDocGroupRight();
      miNewToolBottom.Click += (s, e) => CreateNewToolGroupBottom();
      miCrossDocToTool.Click += (s, e) => CrossDockDocToToolCenter();
      miCrossToolToDoc.Click += (s, e) => CrossDockToolToDocCenter();
      miDump.Click += (s, e) => DumpLayoutFromManager("Dump Layout (manual)");
      miClear.Click += (s, e) => { try { _LogSide.Clear(); _LogDock.Clear(); } catch { } };

      actions.DropDownItems.Add(miAddDoc);
      actions.DropDownItems.Add(miAddTool);
      actions.DropDownItems.Add(new ToolStripSeparator());
      actions.DropDownItems.Add(miNewDocRight);
      actions.DropDownItems.Add(miNewToolBottom);
      actions.DropDownItems.Add(new ToolStripSeparator());
      actions.DropDownItems.Add(miCrossDocToTool);
      actions.DropDownItems.Add(miCrossToolToDoc);
      actions.DropDownItems.Add(new ToolStripSeparator());
      actions.DropDownItems.Add(miDump);
      actions.DropDownItems.Add(miClear);

      // AutoHide Menu --------------------------------------------------------------------------------------------

      var miPinL = new ToolStripMenuItem("Pin Tool -> AutoHide (Left)");
      var miPinR = new ToolStripMenuItem("Pin Tool -> AutoHide (Right)");
      var miPinT = new ToolStripMenuItem("Pin Tool -> AutoHide (Top)");
      var miPinB = new ToolStripMenuItem("Pin Tool -> AutoHide (Bottom)");
      var miUnpin = new ToolStripMenuItem("Unpin (Last AutoHide Key)");
      var miToggle = new ToolStripMenuItem("Toggle Popup");
      var miHide = new ToolStripMenuItem("Hide Popup");
      var miDumpAh = new ToolStripMenuItem("Dump AutoHide");

      miPinL.Click += (s, e) => PinToolToAutoHide(DockAutoHideSide.Left);
      miPinR.Click += (s, e) => PinToolToAutoHide(DockAutoHideSide.Right);
      miPinT.Click += (s, e) => PinToolToAutoHide(DockAutoHideSide.Top);
      miPinB.Click += (s, e) => PinToolToAutoHide(DockAutoHideSide.Bottom);
      miUnpin.Click += (s, e) => UnpinLastAutoHide();

      miToggle.Click += (s, e) => ToggleAutoHidePopup();
      miHide.Click += (s, e) => HideAutoHidePopup();
      miDumpAh.Click += (s, e) => DumpAutoHideState();

      autoHide.DropDownItems.Add(miPinL);
      autoHide.DropDownItems.Add(miPinR);
      autoHide.DropDownItems.Add(miPinT);
      autoHide.DropDownItems.Add(miPinB);
      autoHide.DropDownItems.Add(miUnpin);
      autoHide.DropDownItems.Add(new ToolStripSeparator());
      autoHide.DropDownItems.Add(miToggle);
      autoHide.DropDownItems.Add(miHide);
      autoHide.DropDownItems.Add(new ToolStripSeparator());
      autoHide.DropDownItems.Add(miDumpAh);

      _Menu.Items.Add(init);
      _Menu.Items.Add(actions);
      _Menu.Items.Add(autoHide);
    }

    // Event Handlers ===============================================================================================

    private void OnFormLoad(object? sender, EventArgs e)
    {
      Log("[EVT] Form Load.");

      if (_MiAutoInitOnLoad?.Checked == true)
        AutoInitFromCheckbox("Load");
    }

    private void OnFormShown(object? sender, EventArgs e)
    {
      Log("[EVT] Form Shown.");

      if (_MiAutoInitOnShown?.Checked == true)
        AutoInitFromCheckbox("Shown");
    }

    private void AutoInitFromCheckbox(string from)
    {
      var mode = (_MiUseDefaultLayout?.Checked == true) ? InitMode.InitializeWithDefaultLayout : InitMode.InitializeOnly;
      Log($"[DO] AutoInit from {from}: {mode}");
      RunInit(mode);
    }

    // Host Lifecycle ===============================================================================================

    private void RecreateHost()
    {
      UnbindManagerHooks();

      if (_Host is not null)
      {
        try { _Host.RootChanged -= OnHostRootChanged; } catch { }
        try
        {
          _Split.Panel1.Controls.Remove(_Host);
          _Host.Dispose();
        }
        catch { }
      }

      _Host = new DockHostControl { Dock = DockStyle.Fill };
      _Host.RootChanged += OnHostRootChanged;

      _Split.Panel1.Controls.Add(_Host);

      Log("[OK] Host recreated.");
      UpdateAutoHideUi();
    }

    private void OnHostRootChanged(object? sender, EventArgs e)
    {
      Log($"[EVT] Host RootChanged. Root={(_Host?.Root is null ? "null" : _Host!.Root.GetType().Name)}");
      UpdateAutoHideUi();
    }

    // Init Paths ===================================================================================================

    private enum InitMode
    {
      InitializeOnly,
      InitializeWithDefaultLayout,
      InitializeWithProvided,
    }

    private void RunInit(InitMode mode)
    {
      if (_Host is null) return;

      try
      {
        switch (mode)
        {
          case InitMode.InitializeOnly:
            EnsureProvidedManagerRendererFactory();
            Log($"[DO] Initialize(manager, renderer)  (IsHandleCreated={_Host.IsHandleCreated})");
            _Host.Initialize(_HeldManager!, _HeldRenderer!);
            BindManagerHooks(_HeldManager);
            ApplyDemoThemeToExistingContents(_HeldManager);
            break;

          case InitMode.InitializeWithDefaultLayout:
            EnsureProvidedManagerRendererFactory();
            Log($"[DO] Initialize + DefaultLayout  (IsHandleCreated={_Host.IsHandleCreated})");
            _Host.Initialize(_HeldManager!, _HeldRenderer!);
            BindManagerHooks(_HeldManager);

            _HeldManager!.ApplyLayout(DockDefaults.CreateDefaultLayout(), "Demo:DefaultLayout", validateOverride: true);
            SeedDefaultTabs(_HeldManager);
            DumpLayout(_HeldManager.Root, "[DUMP] After InitDefaultLayout+Seed");

            ApplyDemoThemeToExistingContents(_HeldManager);
            break;

          case InitMode.InitializeWithProvided:
            EnsureProvidedManagerRendererFactory();
            Log($"[DO] Initialize(manager, renderer, factory)  (IsHandleCreated={_Host.IsHandleCreated})");
            _Host.Initialize(_HeldManager!, _HeldRenderer!);
            BindManagerHooks(_HeldManager);

            SeedDefaultTabs(_HeldManager);
            DumpLayout(_HeldManager.Root, "[DUMP] After InitProvided+Seed");

            ApplyDemoThemeToExistingContents(_HeldManager);
            break;
        }

        Log($"[OK] Init done. IsInitialized={_Host.IsInitialized}, Manager={(_Host.Manager is null ? "null" : "set")}, Renderer={(_Host.Renderer is null ? "null" : "set")}");
        UpdateAutoHideUi();
      }
      catch (Exception ex)
      {
        Log("[ERR] Init FAILED: " + ex);
      }
    }

    private void EnsureProvidedManagerRendererFactory()
    {
      _HeldRenderer ??= new VsDockRenderer();

      // NOTE: DemoDockContentFactory는 다음 단계에서 IDockContentFactory.Create(string) 시그니처를 맞춰야 컴파일된다.
      _Factory ??= new DemoDockContentFactory(() => _LogDock);

      _HeldManager ??= new DockManager(_Factory);
    }

    private void SeedDefaultTabs(DockManager m)
    {
      var root = m.Root;
      if (root is null) return;

      var docId = FindNthGroupId(root, DockContentKind.Document, 0);
      if (string.IsNullOrWhiteSpace(docId))
      {
        var doc = new DockGroupNode(DockContentKind.Document);
        m.ApplyLayout(doc, "Demo:EnsureDocRoot", validateOverride: true);
        docId = FindNthGroupId(m.Root, DockContentKind.Document, 0);
      }

      if (!string.IsNullOrWhiteSpace(docId))
      {
        _DocSeq = Math.Max(_DocSeq, 1);
        _LastDocKey = "Doc:0001";
        m.Dock(_LastDocKey, docId!, DockDropSide.Center, 0.5, true, "Seed:Doc0001");
      }

      var toolId0 = FindNthGroupId(m.Root, DockContentKind.ToolWindow, 0);
      if (string.IsNullOrWhiteSpace(toolId0) && !string.IsNullOrWhiteSpace(docId))
      {
        _ToolSeq = Math.Max(_ToolSeq, 1);
        _LastToolKey = "Tool:0001";
        m.Dock(_LastToolKey, docId!, DockDropSide.Right, 0.22, true, "Seed:Tool0001:CreateToolArea");
        toolId0 = FindNthGroupId(m.Root, DockContentKind.ToolWindow, 0);
      }

      if (!string.IsNullOrWhiteSpace(toolId0))
      {
        if (string.IsNullOrWhiteSpace(_LastToolKey))
          _LastToolKey = "Tool:0001";

        m.Dock(_LastToolKey!, toolId0!, DockDropSide.Center, 0.5, true, "Seed:Tool0001");

        // Log 탭(툴 영역 탭으로) - Factory에서 "Log"를 특별 처리(폼의 _LogDock를 그대로 탭에 꽂음)
        m.Dock("Log", toolId0!, DockDropSide.Center, 0.5, false, "Seed:Log");

        // Output은 Bottom으로 분할
        m.Dock("Output", toolId0!, DockDropSide.Bottom, 0.80, true, "Seed:Output");
      }

      UpdateAutoHideUi();
    }

    // Manager Hooks (Theme / Diagnostics) ==========================================================================

    private void BindManagerHooks(DockManager? manager)
    {
      if (ReferenceEquals(_BoundManager, manager)) return;

      UnbindManagerHooks();

      _BoundManager = manager;
      if (_BoundManager is null) return;

      _BoundManager.Events.LayoutChanged += OnManagerLayoutChanged;
      _BoundManager.Events.ActiveContentChanged += OnManagerActiveContentChanged;

      _BoundManager.Events.ContentAdded += OnManagerContentAdded;
      _BoundManager.Events.ContentRemoved += OnManagerContentChanged;
      _BoundManager.Events.ContentClosed += OnManagerContentChanged;

      _LastActiveKey = _BoundManager.ActiveContent?.PersistKey;
      UpdateAutoHideUi();
    }

    private void UnbindManagerHooks()
    {
      if (_BoundManager is null) return;

      try { _BoundManager.Events.LayoutChanged -= OnManagerLayoutChanged; } catch { }
      try { _BoundManager.Events.ActiveContentChanged -= OnManagerActiveContentChanged; } catch { }

      try { _BoundManager.Events.ContentAdded -= OnManagerContentAdded; } catch { }
      try { _BoundManager.Events.ContentRemoved -= OnManagerContentChanged; } catch { }
      try { _BoundManager.Events.ContentClosed -= OnManagerContentChanged; } catch { }

      _BoundManager = null;
      _LastActiveKey = null;
      UpdateAutoHideUi();
    }

    private void OnManagerLayoutChanged(object? sender, DockLayoutChangedEventArgs e)
    {
      Log("[EVT] Manager LayoutChanged.");
      UpdateAutoHideUi();
    }

    private void OnManagerActiveContentChanged(object? sender, DockActiveContentChangedEventArgs e)
    {
      var m = _Host?.Manager;
      if (m is null) return;

      // FIX: sender는 DockManager가 아니라 DockEvents일 수 있다.
      //      sender가 m 또는 m.Events가 아니면 무시(진짜 다른 매니저 이벤트만 차단)
      if (sender is not null && !ReferenceEquals(sender, m) && !ReferenceEquals(sender, m.Events))
      {
        Log($"[EVT] Manager ActiveChanged (IGNORED foreign sender). sender={sender.GetType().Name}, host={m.GetType().Name}");
        return;
      }

      var prev = _LastActiveKey ?? "null";
      var cur = m.ActiveContent?.PersistKey ?? "null";
      _LastActiveKey = (cur == "null") ? null : cur;

      Log($"[EVT] Manager ActiveChanged. {prev} -> {cur}");
      UpdateAutoHideUi();
    }

    private static string DescribeObj(object? o)
    {
      if (o is null) return "null";
      return $"{o.GetType().Name}#{o.GetHashCode()}";
    }

    private static string? TryGetPersistKeyFromActiveChangedArgs(object e)
    {
      // e 자체가 Key를 들고 있는 경우
      var k = TryGetStringByReflection(e, "PersistKey", "Key", "ActiveKey", "NewKey", "NewActiveKey");
      if (!string.IsNullOrWhiteSpace(k)) return k!.Trim();

      // e가 Content/NewContent 같은 걸 들고 있는 경우
      object? c =
        TryGetPropertyValueByReflection(e, "NewContent")
        ?? TryGetPropertyValueByReflection(e, "Content")
        ?? TryGetPropertyValueByReflection(e, "ActiveContent")
        ?? TryGetPropertyValueByReflection(e, "NewActive");

      if (c is null) return null;

      if (c is IDockContent dc)
        return string.IsNullOrWhiteSpace(dc.PersistKey) ? null : dc.PersistKey.Trim();

      k = TryGetStringByReflection(c, "PersistKey", "Key", "Id", "Name");
      return string.IsNullOrWhiteSpace(k) ? null : k!.Trim();
    }

    private static object? TryGetPropertyValueByReflection(object instance, string propertyName)
    {
      var t = instance.GetType();
      var p = t.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      if (p is null || !p.CanRead) return null;

      try { return p.GetValue(instance); }
      catch { return null; }
    }

    private static string? TryGetStringByReflection(object instance, params string[] propertyNames)
    {
      for (int i = 0; i < propertyNames.Length; i++)
      {
        var v = TryGetPropertyValueByReflection(instance, propertyNames[i]);
        if (v is string s && !string.IsNullOrWhiteSpace(s)) return s;
      }
      return null;
    }

    private void OnManagerContentAdded(object? sender, DockContentEventArgs e)
    {
      var renderer = _Host?.Renderer;
      if (renderer is null) return;
      if (e.Content is null) return;

      ApplyThemeToControlTree(e.Content.View, renderer.Palette);
    }

    private void OnManagerContentChanged(object? sender, DockContentEventArgs e)
    {
      // no-op (필요 시 여기서 추가 로그/검증)
      UpdateAutoHideUi();
    }

    // Actions (Add / Split / Cross) =================================================================================

    private void AddDocTabToFirstDocGroup()
    {
      var m = _Host?.Manager;
      if (m is null) return;

      var root = m.Root;
      if (root is null) return;

      var docId = FindNthGroupId(root, DockContentKind.Document, 0);
      if (string.IsNullOrWhiteSpace(docId))
      {
        Log("[WARN] AddDocTab: Document group missing.");
        return;
      }

      _DocSeq = Math.Max(_DocSeq, 1);
      _DocSeq++;
      _LastDocKey = $"Doc:{_DocSeq:0000}";

      try
      {
        m.Dock(_LastDocKey, docId!, DockDropSide.Center, 0.5, true, $"Demo:AddDocTab:{_LastDocKey}");
        DumpLayout(m.Root, $"[DUMP] After AddDocTab({_LastDocKey})");
      }
      catch (Exception ex)
      {
        Log("[ERR] AddDocTab FAILED: " + ex.GetType().Name + " - " + ex.Message);
      }
    }

    private void AddToolTabToFirstToolGroupOrCreate()
    {
      var m = _Host?.Manager;
      if (m is null) return;

      var root = m.Root;
      if (root is null) return;

      var toolId = FindNthGroupId(root, DockContentKind.ToolWindow, 0);
      if (string.IsNullOrWhiteSpace(toolId))
      {
        var docId = FindNthGroupId(root, DockContentKind.Document, 0);
        if (string.IsNullOrWhiteSpace(docId))
        {
          Log("[WARN] AddToolTab: Document group missing.");
          return;
        }

        _ToolSeq = Math.Max(_ToolSeq, 0);
        _ToolSeq++;
        _LastToolKey = $"Tool:{_ToolSeq:0000}";

        try
        {
          // Doc에 붙일 때 Tool 20% (요구사항)
          m.Dock(_LastToolKey, docId!, DockDropSide.Right, 0.20, true, $"Demo:AddToolTab:CreateToolArea:{_LastToolKey}");

          toolId = FindNthGroupId(m.Root, DockContentKind.ToolWindow, 0);
          if (string.IsNullOrWhiteSpace(toolId))
          {
            Log("[ERR] AddToolTab: Tool group creation failed (no ToolWindow group found).");
            return;
          }

          // Log 탭을 같이 보장
          m.Dock("Log", toolId!, DockDropSide.Center, 0.5, false, "Demo:EnsureLogTab");

          DumpLayout(m.Root, $"[DUMP] After AddToolTab(CreateToolArea:{_LastToolKey})");
          return;
        }
        catch (Exception ex)
        {
          Log("[ERR] AddToolTab(CreateToolArea) FAILED: " + ex.GetType().Name + " - " + ex.Message);
          return;
        }
      }

      _ToolSeq = Math.Max(_ToolSeq, 0);
      _ToolSeq++;
      _LastToolKey = $"Tool:{_ToolSeq:0000}";

      try
      {
        m.Dock(_LastToolKey, toolId!, DockDropSide.Center, 0.5, true, $"Demo:AddToolTab:{_LastToolKey}");
        DumpLayout(m.Root, $"[DUMP] After AddToolTab({_LastToolKey})");
      }
      catch (Exception ex)
      {
        Log("[ERR] AddToolTab FAILED: " + ex.GetType().Name + " - " + ex.Message);
      }
    }

    private void CreateNewDocGroupRight()
    {
      var m = _Host?.Manager;
      if (m is null) return;

      var root = m.Root;
      if (root is null) return;

      var docId = FindNthGroupId(root, DockContentKind.Document, 0);
      if (string.IsNullOrWhiteSpace(docId))
      {
        Log("[WARN] NewDocGroupRight: Document group missing.");
        return;
      }

      _DocSeq = Math.Max(_DocSeq, 1);
      _DocSeq++;
      var key = $"Doc:{_DocSeq:0000}";
      _LastDocKey = key;

      try
      {
        // Doc 신규 그룹 50%
        m.Dock(key, docId!, DockDropSide.Right, 0.5, true, $"Demo:NewDocGroupRight:{key}");
        DumpLayout(m.Root, $"[DUMP] After NewDocGroupRight({key})");
      }
      catch (Exception ex)
      {
        Log("[ERR] NewDocGroupRight FAILED: " + ex.GetType().Name + " - " + ex.Message);
      }
    }

    private void CreateNewToolGroupBottom()
    {
      var m = _Host?.Manager;
      if (m is null) return;

      var root = m.Root;
      if (root is null) return;

      var toolId = FindNthGroupId(root, DockContentKind.ToolWindow, 0);
      if (string.IsNullOrWhiteSpace(toolId))
      {
        Log("[WARN] NewToolGroupBottom: ToolWindow group missing. Use +Tool Tab first.");
        return;
      }

      _ToolSeq = Math.Max(_ToolSeq, 0);
      _ToolSeq++;
      var key = $"Tool:{_ToolSeq:0000}";
      _LastToolKey = key;

      try
      {
        // Tool->Tool 신규 그룹 50%
        m.Dock(key, toolId!, DockDropSide.Bottom, 0.5, true, $"Demo:NewToolGroupBottom:{key}");
        DumpLayout(m.Root, $"[DUMP] After NewToolGroupBottom({key})");
      }
      catch (Exception ex)
      {
        Log("[ERR] NewToolGroupBottom FAILED: " + ex.GetType().Name + " - " + ex.Message);
      }
    }

    private void CrossDockDocToToolCenter()
    {
      var m = _Host?.Manager;
      if (m is null) return;

      var root = m.Root;
      if (root is null) return;

      var toolId = FindNthGroupId(root, DockContentKind.ToolWindow, 0);
      if (string.IsNullOrWhiteSpace(toolId))
      {
        Log("[WARN] CrossDock Doc->Tool: ToolWindow group missing.");
        return;
      }

      var docKey = FindAnyKey(root, DockContentKind.Document) ?? _LastDocKey;
      if (string.IsNullOrWhiteSpace(docKey))
      {
        Log("[WARN] CrossDock Doc->Tool: Document key missing.");
        return;
      }

      try
      {
        m.Dock(docKey!, toolId!, DockDropSide.Center, 0.5, true, $"Demo:CrossDock:DocToTool:{docKey}");
        DumpLayout(m.Root, $"[DUMP] After CrossDock Doc->Tool ({docKey})");
      }
      catch (Exception ex)
      {
        Log("[ERR] CrossDock Doc->Tool FAILED: " + ex.GetType().Name + " - " + ex.Message);
      }
    }

    private void CrossDockToolToDocCenter()
    {
      var m = _Host?.Manager;
      if (m is null) return;

      var root = m.Root;
      if (root is null) return;

      var docId = FindNthGroupId(root, DockContentKind.Document, 0);
      if (string.IsNullOrWhiteSpace(docId))
      {
        Log("[WARN] CrossDock Tool->Doc: Document group missing.");
        return;
      }

      var toolKey = FindAnyKey(root, DockContentKind.ToolWindow) ?? _LastToolKey;
      if (string.IsNullOrWhiteSpace(toolKey))
      {
        Log("[WARN] CrossDock Tool->Doc: Tool key missing.");
        return;
      }

      try
      {
        m.Dock(toolKey!, docId!, DockDropSide.Center, 0.5, true, $"Demo:CrossDock:ToolToDoc:{toolKey}");
        DumpLayout(m.Root, $"[DUMP] After CrossDock Tool->Doc ({toolKey})");
      }
      catch (Exception ex)
      {
        Log("[ERR] CrossDock Tool->Doc FAILED: " + ex.GetType().Name + " - " + ex.Message);
      }
    }

    private void DumpLayoutFromManager(string title)
    {
      var m = _Host?.Manager;
      if (m?.Root is null) { Log("[WARN] DumpLayout: Manager/Root null"); return; }
      DumpLayout(m.Root, title);
    }

    // AutoHide Actions ============================================================================================

    private void PinToolToAutoHide(DockAutoHideSide side)
    {
      var m = _Host?.Manager;
      if (m is null) { Log("[WARN] AutoHide: Manager null"); return; }
      if (m.Root is null) { Log("[WARN] AutoHide: Root null"); return; }

      var key = ResolveToolKeyForPin(m);
      if (string.IsNullOrWhiteSpace(key))
      {
        Log("[WARN] AutoHide: No ToolWindow key to pin. Use +Tool Tab first.");
        return;
      }

      key = key.Trim();
      if (key.Length == 0) return;

      var ok = false;
      try { ok = m.PinToAutoHide(key, side, popupSize: null, showPopup: false, reason: $"Demo:AutoHide:Pin:{key}:{side}"); } catch { ok = false; }

      if (ok)
      {
        _LastAutoHideKey = key;
        Log($"[OK] AutoHide Pin requested. key={key}, side={side}");
      }
      else
      {
        Log($"[WARN] AutoHide Pin FAILED. key={key}, side={side} (must be in group?)");
      }

      DumpAutoHideState();
      UpdateAutoHideUi();
    }

    private void UnpinLastAutoHide()
    {
      var m = _Host?.Manager;
      if (m is null) return;

      var key = NormalizeKey(_LastAutoHideKey)
        ?? NormalizeKey(m.ActiveAutoHideKey)
        ?? FindAnyAutoHideKey(m.Root);

      if (string.IsNullOrWhiteSpace(key))
      {
        Log("[WARN] AutoHide Unpin: No auto-hide key found. Pin first.");
        return;
      }

      var ok = false;
      try { ok = m.UnpinFromAutoHide(key!, targetGroupNodeId: null, makeActive: true, reason: $"Demo:AutoHide:Unpin:{key}"); } catch { ok = false; }

      Log(ok
        ? $"[OK] AutoHide Unpin requested. key={key}"
        : $"[WARN] AutoHide Unpin NO-OP. key={key} (not in AutoHide?)");

      _LastAutoHideKey = key;
      DumpAutoHideState();
      UpdateAutoHideUi();
    }

    private void ToggleAutoHidePopup()
    {
      var m = _Host?.Manager;
      if (m is null) return;

      var key = NormalizeKey(_LastAutoHideKey)
        ?? NormalizeKey(m.ActiveAutoHideKey)
        ?? FindAnyAutoHideKey(m.Root);

      if (string.IsNullOrWhiteSpace(key))
      {
        Log("[WARN] AutoHide Toggle: No auto-hide key found. Pin first.");
        return;
      }

      var ok = false;
      try { ok = m.ToggleAutoHidePopup(key!, $"Demo:AutoHide:Toggle:{key}"); } catch { ok = false; }

      Log(ok
        ? $"[OK] AutoHide Toggle requested. key={key}"
        : $"[WARN] AutoHide Toggle NO-OP. key={key} (not in AutoHide?)");

      _LastAutoHideKey = key;
      UpdateAutoHideUi();
    }

    private void HideAutoHidePopup()
    {
      var m = _Host?.Manager;
      if (m is null) return;

      try { m.HideAutoHidePopup("Demo:AutoHide:Hide"); } catch { }
      Log("[OK] AutoHide Hide requested.");
      UpdateAutoHideUi();
    }

    private void DumpAutoHideState()
    {
      var m = _Host?.Manager;
      if (m is null) { Log("[WARN] DumpAutoHide: Manager null"); return; }

      var ahCount = CountAutoHideItems(m.Root);
      var activeKey = string.IsNullOrWhiteSpace(m.ActiveAutoHideKey) ? "-" : m.ActiveAutoHideKey!.Trim();

      Log($"[AH] Items={ahCount}, PopupVisible={m.IsAutoHidePopupVisible}, ActiveAutoHideKey={activeKey}");

      var anyKey = FindAnyAutoHideKey(m.Root);
      if (!string.IsNullOrWhiteSpace(anyKey))
        Log($"[AH] AnyKeyInAutoHide={anyKey}");

      DumpAutoHideApis(m);
    }

    private void DumpAutoHideApis(DockManager m)
    {
      try
      {
        Log("[AH] ==== AutoHide API Scan (DockManager) ===========================================================");

        var t = m.GetType();
        var ms = t.GetMethods(BindingFlags.Public | BindingFlags.Instance);

        var hit = 0;
        for (int i = 0; i < ms.Length; i++)
        {
          var name = ms[i].Name;
          if (name.IndexOf("AutoHide", StringComparison.OrdinalIgnoreCase) < 0
            && name.IndexOf("Pin", StringComparison.OrdinalIgnoreCase) < 0
            && name.IndexOf("Unpin", StringComparison.OrdinalIgnoreCase) < 0)
            continue;

          Log($"[AH] Manager: {FormatMethod(ms[i])}");
          hit++;
        }

        if (hit == 0)
          Log("[AH] Manager: (no public AutoHide/Pin APIs found)");
      }
      catch { }

      try
      {
        Log("[AH] ==== AutoHide API Scan (DockMutator) ===========================================================");

        var t = typeof(DockMutator);
        var ms = t.GetMethods(BindingFlags.Public | BindingFlags.Static);

        var hit = 0;
        for (int i = 0; i < ms.Length; i++)
        {
          var name = ms[i].Name;
          if (name.IndexOf("AutoHide", StringComparison.OrdinalIgnoreCase) < 0
            && name.IndexOf("Pin", StringComparison.OrdinalIgnoreCase) < 0
            && name.IndexOf("Unpin", StringComparison.OrdinalIgnoreCase) < 0)
            continue;

          Log($"[AH] Mutator: {FormatMethod(ms[i])}");
          hit++;
        }

        if (hit == 0)
          Log("[AH] Mutator: (no public AutoHide/Pin APIs found)");
      }
      catch { }
    }

    private static string FormatMethod(MethodInfo m)
    {
      try
      {
        var ps = m.GetParameters();
        var list = new string[ps.Length];
        for (int i = 0; i < ps.Length; i++)
          list[i] = $"{ps[i].ParameterType.Name} {ps[i].Name}";

        return $"{m.ReturnType.Name} {m.Name}({string.Join(", ", list)})";
      }
      catch
      {
        return m.Name;
      }
    }

    private static string? NormalizeKey(string? s)
    {
      if (string.IsNullOrWhiteSpace(s)) return null;
      s = s.Trim();
      return s.Length == 0 ? null : s;
    }

    private static string? FindAnyAutoHideKey(DockNode root)
    {
      foreach (var n in root.TraverseDepthFirst(true))
      {
        if (n is not DockAutoHideNode a) continue;

        for (int i = 0; i < a.Items.Count; i++)
        {
          var k = a.Items[i].PersistKey;
          if (string.IsNullOrWhiteSpace(k)) continue;
          k = k.Trim();
          if (k.Length == 0) continue;
          return k;
        }
      }

      return null;
    }

    private static int CountAutoHideItems(DockNode root)
    {
      var count = 0;

      foreach (var n in root.TraverseDepthFirst(true))
      {
        if (n is not DockAutoHideNode a) continue;
        count += a.Items.Count;
      }

      return count;
    }

    private string? ResolveToolKeyForPin(DockManager m)
    {
      var root = m.Root;
      if (root is null) return null;

      // 1) 현재 ActiveContent가 ToolWindow 그룹 내부에 있으면 그 키(=사용자가 선택한 탭) 우선
      var active = NormalizeKey(m.ActiveContent?.PersistKey);
      if (!string.IsNullOrWhiteSpace(active) && IsKeyInGroupKind(root, DockContentKind.ToolWindow, active!))
        return active;

      // 2) 이벤트 기반으로 캐시된 마지막 Active 키가 ToolWindow면 사용
      var lastActive = NormalizeKey(_LastActiveKey);
      if (!string.IsNullOrWhiteSpace(lastActive) && IsKeyInGroupKind(root, DockContentKind.ToolWindow, lastActive!))
        return lastActive;

      // 3) 첫 ToolWindow 그룹의 ActiveKey(없으면 첫 아이템)
      var gKey = FindFirstGroupActiveOrFirstItemKey(root, DockContentKind.ToolWindow);
      if (!string.IsNullOrWhiteSpace(gKey)) return gKey;

      // 4) 마지막 Tool 키가 아직 ToolWindow 그룹에 남아있으면 사용
      var lastTool = NormalizeKey(_LastToolKey);
      if (!string.IsNullOrWhiteSpace(lastTool) && IsKeyInGroupKind(root, DockContentKind.ToolWindow, lastTool!))
        return lastTool;

      // 5) 마지막 폴백: 아무 ToolWindow 키(그래도 Tool만)
      return FindAnyKey(root, DockContentKind.ToolWindow);
    }

    private static bool IsKeyInGroupKind(DockNode root, DockContentKind kind, string persistKey)
    {
      if (string.IsNullOrWhiteSpace(persistKey)) return false;
      persistKey = persistKey.Trim();
      if (persistKey.Length == 0) return false;

      foreach (var node in root.TraverseDepthFirst(true))
      {
        if (node is not DockGroupNode g) continue;
        if (g.ContentKind != kind) continue;

        for (int i = 0; i < g.Items.Count; i++)
        {
          var k = g.Items[i].PersistKey;
          if (string.IsNullOrWhiteSpace(k)) continue;
          k = k.Trim();
          if (k.Length == 0) continue;

          if (string.Equals(k, persistKey, StringComparison.Ordinal))
            return true;
        }
      }

      return false;
    }

    private static string? FindFirstGroupActiveOrFirstItemKey(DockNode root, DockContentKind kind)
    {
      foreach (var node in root.TraverseDepthFirst(true))
      {
        if (node is not DockGroupNode g) continue;
        if (g.ContentKind != kind) continue;

        if (!string.IsNullOrWhiteSpace(g.ActiveKey))
        {
          var a = g.ActiveKey!.Trim();
          if (a.Length > 0) return a;
        }

        if (g.Items.Count > 0 && !string.IsNullOrWhiteSpace(g.Items[0].PersistKey))
        {
          var k = g.Items[0].PersistKey.Trim();
          if (k.Length > 0) return k;
        }

        return null;
      }

      return null;
    }

    private void UpdateAutoHideUi()
    {
      var m = _Host?.Manager;
      if (m is null)
      {
        _AutoHideState.Text = "AutoHide: -";
        return;
      }

      var activeKey = string.IsNullOrWhiteSpace(m.ActiveAutoHideKey) ? "-" : m.ActiveAutoHideKey!.Trim();
      var items = CountAutoHideItems(m.Root);
      _AutoHideState.Text = $"AutoHide: Items={items}, Popup={(m.IsAutoHidePopupVisible ? "ON" : "OFF")}, Key={activeKey}";
    }

    // Layout Dump ==================================================================================================

    private void DumpLayout(DockNode root, string title)
    {
      Log($"==== {title} ==========================================================================================");

      foreach (var n in root.TraverseDepthFirst(true))
      {
        if (n is DockSplitNode s)
          Log($"Split: {s.NodeId} {s.Orientation} Ratio={s.Ratio}");
        else if (n is DockGroupNode g)
          Log($"Group: {g.NodeId} ContentKind={g.ContentKind} Items={g.Items.Count} Active={g.ActiveKey ?? "null"}");
        else if (n is DockAutoHideNode ah)
          Log($"AutoHide: {ah.NodeId} Side={ah.Side} Items={ah.Items.Count} Active={ah.ActiveKey ?? "null"}");
        else if (n is DockFloatingNode f)
          Log($"Floating: {f.NodeId} Root={(f.Root is null ? "null" : f.Root.GetType().Name)}");
        else
          Log($"Node: {n.GetType().Name} {n.NodeId}");
      }
    }

    private static string? FindNthGroupId(DockNode root, DockContentKind kind, int n)
    {
      var hit = 0;

      foreach (var node in root.TraverseDepthFirst(true))
      {
        if (node is DockGroupNode g && g.ContentKind == kind)
        {
          if (hit == n) return g.NodeId;
          hit++;
        }
      }

      return null;
    }

    private static string? FindAnyKey(DockNode root, DockContentKind kind)
    {
      foreach (var node in root.TraverseDepthFirst(true))
      {
        if (node is DockGroupNode g && g.ContentKind == kind && g.Items.Count > 0)
          return g.Items[0].PersistKey;
      }
      return null;
    }

    // Demo Theme ===================================================================================================

    private void ApplyDemoThemeToExistingContents(DockManager manager)
    {
      var renderer = _Host?.Renderer;
      if (renderer is null) return;

      ApplyThemeToTextBox(_LogSide, renderer.Palette);
      ApplyThemeToTextBox(_LogDock, renderer.Palette);

      try
      {
        foreach (var c in manager.Registry.EnumerateAll())
          ApplyThemeToControlTree(c.View, renderer.Palette);
      }
      catch
      {
        // ignore
      }
    }

    private static void ApplyThemeToControlTree(Control root, ColorPalette palette)
    {
      if (root is TextBoxBase tb)
        ApplyThemeToTextBox(tb, palette);

      for (int i = 0; i < root.Controls.Count; i++)
        ApplyThemeToControlTree(root.Controls[i], palette);
    }

    private static void ApplyThemeToTextBox(TextBoxBase tb, ColorPalette palette)
    {
      try
      {
        tb.BackColor = palette[ColorPalette.Role.PanelBack];
        tb.ForeColor = palette[ColorPalette.Role.Text];
      }
      catch
      {
        // ignore
      }
    }

    // Logging ======================================================================================================

    private void Log(string message)
    {
      var line = $"{DateTime.Now:HH:mm:ss.fff}  {message}";

      try { _LogSide.AppendText(line + Environment.NewLine); } catch { }
      try { _LogDock.AppendText(line + Environment.NewLine); } catch { }
    }

    private static TextBox CreateLogTextBox()
    {
      return new TextBox
      {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font(FontFamily.GenericMonospace, 9.0f),
      };
    }
  }
}

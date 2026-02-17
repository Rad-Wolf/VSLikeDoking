using System;
using System.Drawing;
using System.Windows.Forms;

namespace VsLikeDoking.UI.Host
{
  public sealed partial class DockSurfaceControl
  {
    private sealed class AutoHidePopupChromePanel : Panel
    {
      public readonly struct ChromeTheme
      {
        public ChromeTheme(Color borderColor, Color fillColor)
        {
          BorderColor = borderColor;
          FillColor = fillColor;
        }

        public Color BorderColor { get; }
        public Color FillColor { get; }
      }

      public ChromeTheme? Theme { get; set; }

      protected override void OnPaint(PaintEventArgs e)
      {
        base.OnPaint(e);

        var rc = ClientRectangle;
        if (rc.Width <= 0 || rc.Height <= 0) return;

        var t = Theme;
        var fill = t?.FillColor ?? SystemColors.Control;
        var border = t?.BorderColor ?? SystemColors.Highlight;

        using var b = new SolidBrush(fill);
        e.Graphics.FillRectangle(b, rc);

        rc.Width -= 1;
        rc.Height -= 1;
        if (rc.Width <= 0 || rc.Height <= 0) return;

        using var p = new Pen(border, 1f);
        e.Graphics.DrawRectangle(p, rc);
      }
    }

    private sealed class DockPreviewOverlayForm : Form
    {
      // Types ==================================================================

      public enum PreviewMode : byte { None = 0, ZoneRect = 1, InsertLine = 2 }

      // Fields =================================================================

      private readonly Form _Owner;
      private PreviewMode _Mode;
      private Point _LineP0;
      private Point _LineP1;
      private Color _BorderColor;
      private Color _FillColor;

      // Properties =============================================================

      public PreviewMode Mode
      {
        get { return _Mode; }
        set { _Mode = value; }
      }

      public Point LineP0
      {
        get { return _LineP0; }
        set { _LineP0 = value; }
      }

      public Point LineP1
      {
        get { return _LineP1; }
        set { _LineP1 = value; }
      }

      public Color BorderColor
      {
        get { return _BorderColor; }
        set { _BorderColor = value; }
      }

      public Color FillColor
      {
        get { return _FillColor; }
        set { _FillColor = value; }
      }

      // Ctor ===================================================================

      public DockPreviewOverlayForm(Form owner)
      {
        _Owner = owner;
        _Mode = PreviewMode.None;
        _LineP0 = Point.Empty;
        _LineP1 = Point.Empty;
        _BorderColor = SystemColors.Highlight;
        _FillColor = Color.FromArgb(70, SystemColors.Highlight);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = false;

        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;

        DoubleBuffered = true;

        Owner = _Owner;
      }

      // Public =================================================================

      public void SetBoundsNoActivate(Rectangle screenBounds)
      {
        Bounds = screenBounds;
      }

      public void ShowNoActivate()
      {
        if (IsDisposed) return;

        if (!Visible)
        {
          if (Owner is null || Owner.IsDisposed) Owner = _Owner;

          Show();
        }
      }

      // Overrides ==============================================================

      protected override bool ShowWithoutActivation
        => true;

      protected override CreateParams CreateParams
      {
        get
        {
          const int WS_EX_NOACTIVATE = 0x08000000;
          const int WS_EX_TOOLWINDOW = 0x00000080;
          const int WS_EX_TRANSPARENT = 0x00000020;

          var cp = base.CreateParams;
          cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT;
          return cp;
        }
      }

      protected override void OnPaintBackground(PaintEventArgs e)
      {
        // 투명키 배경이므로 배경 페인트 불필요
      }

      protected override void OnPaint(PaintEventArgs e)
      {
        base.OnPaint(e);

        if (_Mode == PreviewMode.None) return;

        if (_Mode == PreviewMode.ZoneRect)
        {
          var rc = ClientRectangle;
          if (rc.Width <= 0 || rc.Height <= 0) return;

          if (_FillColor.A != 0)
          {
            using var b = new SolidBrush(_FillColor);
            e.Graphics.FillRectangle(b, rc);
          }

          rc.Width -= 1;
          rc.Height -= 1;

          if (rc.Width > 0 && rc.Height > 0)
          {
            using var pen = new Pen(_BorderColor, 2.0f);
            e.Graphics.DrawRectangle(pen, rc);
          }

          return;
        }

        if (_Mode == PreviewMode.InsertLine)
        {
          using var pen = new Pen(_BorderColor, 2.0f);
          e.Graphics.DrawLine(pen, _LineP0, _LineP1);
          return;
        }
      }

      protected override void WndProc(ref Message m)
      {
        const int WM_NCHITTEST = 0x0084;
        const int HTTRANSPARENT = -1;

        if (m.Msg == WM_NCHITTEST)
        {
          m.Result = (IntPtr)HTTRANSPARENT;
          return;
        }

        base.WndProc(ref m);
      }
    }
  }
}

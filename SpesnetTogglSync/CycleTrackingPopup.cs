namespace SpesnetTogglSync;

internal sealed class CycleTrackingPopup : Form
{
    private readonly Label _body;

    public CycleTrackingPopup()
    {
        FormBorderStyle = FormBorderStyle.FixedSingle;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ControlBox = false;
        MaximizeBox = false;
        MinimizeBox = false;
        KeyPreview = false;
        BackColor = Color.White;
        ClientSize = new Size(360, 148);
        Font = SystemFonts.MessageBoxFont;
        Cursor = Cursors.Hand;

        _body = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 10, 12, 10),
            TextAlign = ContentAlignment.TopLeft,
            Cursor = Cursors.Hand,
            UseMnemonic = false
        };
        _body.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        Controls.Add(_body);
        Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? OpenRequested;

    public void SetText(string text)
    {
        _body.Text = text;
    }

    public void ShowNearCursor()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var x = Cursor.Position.X - 28;
        var y = Cursor.Position.Y - Height - 6;
        if (x + Width > area.Right)
        {
            x = area.Right - Width;
        }

        if (x < area.Left)
        {
            x = area.Left;
        }

        if (y < area.Top)
        {
            y = Math.Min(Cursor.Position.Y + 18, area.Bottom - Height);
        }

        Location = new Point(x, y);
        if (!Visible)
        {
            Show();
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_NOACTIVATE = 0x08000000;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            var parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return parameters;
        }
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_MOUSEACTIVATE = 0x0021;
        const int MA_NOACTIVATE = 3;
        if (m.Msg == WM_MOUSEACTIVATE)
        {
            m.Result = (IntPtr)MA_NOACTIVATE;
            return;
        }

        base.WndProc(ref m);
    }
}

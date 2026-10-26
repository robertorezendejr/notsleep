using NoTSleep.Core;

namespace NoTSleep;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new NoTSleepApplicationContext());
    }
}

internal sealed class NoTSleepApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly Icon _inactiveClockIcon;
    private readonly Icon _activeClockIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly CapsLockWakeController _controller;
    private string _lastBalloonText = string.Empty;

    public NoTSleepApplicationContext()
    {
        _controller = new CapsLockWakeController(new WindowsCapsLockState(), new WindowsWakePlatform());
        _controller.StatusChanged += OnStatusChanged;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Caps Lock ligado = manter acordado", null, (_, _) => ShowStatus());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair e restaurar energia", null, (_, _) => ExitThread());

        _inactiveClockIcon = CreateClockIcon(
            Color.FromArgb(103, 113, 122),
            Color.FromArgb(238, 242, 245));
        _activeClockIcon = CreateClockIcon(
            Color.FromArgb(21, 128, 61),
            Color.FromArgb(232, 255, 239));
        _trayIcon = new NotifyIcon
        {
            Icon = _inactiveClockIcon,
            Text = "NoTSleep: desligado",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowStatus();

        // Short polling makes the physical Caps Lock key feel immediate while
        // still keeping the actual mouse action at the requested 10-minute interval.
        _timer = new System.Windows.Forms.Timer { Interval = 250 };
        _timer.Tick += (_, _) => _controller.Tick(DateTimeOffset.UtcNow);
        _controller.Tick(DateTimeOffset.UtcNow);
        _timer.Start();
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _controller.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _inactiveClockIcon.Dispose();
        _activeClockIcon.Dispose();
        base.ExitThreadCore();
    }

    private void OnStatusChanged(object? sender, string status)
    {
        var isActive = _controller.IsActive;
        _trayIcon.Icon = isActive ? _activeClockIcon : _inactiveClockIcon;
        _trayIcon.Text = isActive ? "NoTSleep: LIGADO" : "NoTSleep: desligado";
        if (status != _lastBalloonText)
        {
            _trayIcon.ShowBalloonTip(3000, "NoTSleep", status, ToolTipIcon.Info);
            _lastBalloonText = status;
        }
    }

    private void ShowStatus() => MessageBox.Show(
        _controller.Status + "\n\nLigue o Caps Lock para ativar; desligue para restaurar as opções de energia.",
        "NoTSleep",
        MessageBoxButtons.OK,
        MessageBoxIcon.Information);

    private static Icon CreateClockIcon(Color outlineColor, Color faceColor)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var outline = new Pen(outlineColor, 3);
        using var face = new SolidBrush(faceColor);
        graphics.FillEllipse(face, 3, 3, 26, 26);
        graphics.DrawEllipse(outline, 3, 3, 26, 26);

        using var hand = new Pen(outlineColor, 3)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };
        graphics.DrawLine(hand, 16, 16, 16, 8);
        graphics.DrawLine(hand, 16, 16, 22, 19);
        graphics.FillEllipse(Brushes.White, 14, 14, 4, 4);
        graphics.DrawEllipse(outline, 14, 14, 4, 4);

        return Icon.FromHandle(bitmap.GetHicon());
    }
}

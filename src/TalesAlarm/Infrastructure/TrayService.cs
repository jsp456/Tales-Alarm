using System.Drawing;

namespace TalesAlarm.Infrastructure;

public sealed class TrayService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon notifyIcon;
    private readonly System.Windows.Forms.ContextMenuStrip menu;
    private readonly Icon? ownedIcon;
    private bool disposed;

    public TrayService(Action show, Action exit)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(exit);

        menu = new System.Windows.Forms.ContextMenuStrip();
        var showItem = new System.Windows.Forms.ToolStripMenuItem("열기");
        var exitItem = new System.Windows.Forms.ToolStripMenuItem("종료");
        showItem.Click += (_, _) => show();
        exitItem.Click += (_, _) => exit();
        menu.Items.Add(showItem);
        menu.Items.Add(exitItem);

        try
        {
            ownedIcon = string.IsNullOrWhiteSpace(Environment.ProcessPath)
                ? null
                : Icon.ExtractAssociatedIcon(Environment.ProcessPath);
        }
        catch (Exception)
        {
            ownedIcon = null;
        }

        notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = ownedIcon ?? SystemIcons.Application,
            Text = "Tales Alarm",
            ContextMenuStrip = menu,
            Visible = false,
        };
        notifyIcon.DoubleClick += (_, _) => show();
    }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        notifyIcon.Visible = true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        menu.Dispose();
        ownedIcon?.Dispose();
    }
}

using System.Windows.Forms;
using LilAgents.Agents;
using LilAgents.Platform;

namespace LilAgents.UI;

/// <summary>
/// The notification-area icon and its menu — the Windows counterpart to the macOS
/// NSStatusItem. Sparkle's "Check for Updates" item has no equivalent here and is omitted.
/// </summary>
public sealed class TrayMenu : IDisposable
{
    private readonly AgentsController _controller;
    private readonly NotifyIcon _icon;
    private bool _disposed;

    public TrayMenu(AgentsController controller)
    {
        _controller = controller;

        _icon = new NotifyIcon
        {
            Text = "lil agents",
            Icon = EmbeddedAssets.LoadIcon("tray.ico") ?? System.Drawing.SystemIcons.Application,
            Visible = false
        };

        _icon.ContextMenuStrip = BuildMenu();
    }

    public void Show() => _icon.Visible = true;

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        // Rebuilt on open so provider availability, monitor list and check marks are
        // current rather than frozen at startup.
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            PopulateMenu(menu);
        };

        PopulateMenu(menu);
        return menu;
    }

    private void PopulateMenu(ContextMenuStrip menu)
    {
        foreach (var walker in _controller.Walkers)
        {
            var item = new ToolStripMenuItem(walker.Name)
            {
                Checked = walker.IsManuallyVisible,
                CheckOnClick = true
            };
            var captured = walker;
            item.Click += (_, _) => captured.SetManuallyVisible(!captured.IsManuallyVisible);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());

        var sounds = new ToolStripMenuItem("Sounds")
        {
            Checked = Settings.Current.SoundsEnabled,
            CheckOnClick = true
        };
        sounds.Click += (_, _) =>
        {
            Settings.Current.SoundsEnabled = !Settings.Current.SoundsEnabled;
            Settings.Current.Save();
        };
        menu.Items.Add(sounds);

        menu.Items.Add(BuildProviderMenu());
        menu.Items.Add(BuildSizeMenu());
        menu.Items.Add(BuildThemeMenu());
        menu.Items.Add(BuildDisplayMenu());

        menu.Items.Add(new ToolStripSeparator());

        var quit = new ToolStripMenuItem("Quit");
        quit.Click += (_, _) => System.Windows.Application.Current.Shutdown();
        menu.Items.Add(quit);
    }

    private ToolStripMenuItem BuildProviderMenu()
    {
        var root = new ToolStripMenuItem("Provider");
        var current = _controller.Walkers.FirstOrDefault()?.Provider ?? AgentProviderKind.Claude;

        foreach (var provider in AgentProvider.All)
        {
            var item = new ToolStripMenuItem(provider.DisplayName())
            {
                Checked = provider == current,
                Enabled = provider.IsAvailable()
            };
            var captured = provider;
            item.Click += (_, _) => _controller.SetProviderForAll(captured);
            root.DropDownItems.Add(item);
        }

        root.DropDownItems.Add(new ToolStripSeparator());

        var advanced = new ToolStripMenuItem("Advanced Settings…");
        advanced.Click += (_, _) => ShowOpenClawSettings();
        root.DropDownItems.Add(advanced);

        var rescan = new ToolStripMenuItem("Rescan for CLIs");
        rescan.Click += (_, _) =>
        {
            // Picks up a CLI the user installed while the app was already running.
            ShellEnvironment.InvalidateCache();
            _ = AgentProvider.DetectAvailableProvidersAsync();
        };
        root.DropDownItems.Add(rescan);

        return root;
    }

    private ToolStripMenuItem BuildSizeMenu()
    {
        var root = new ToolStripMenuItem("Size");
        var current = _controller.Walkers.FirstOrDefault()?.Size ?? CharacterSize.Large;

        foreach (var size in Enum.GetValues<CharacterSize>())
        {
            var item = new ToolStripMenuItem(size.DisplayName()) { Checked = size == current };
            var captured = size;
            item.Click += (_, _) => _controller.SetSizeForAll(captured);
            root.DropDownItems.Add(item);
        }

        return root;
    }

    private ToolStripMenuItem BuildThemeMenu()
    {
        var root = new ToolStripMenuItem("Style");
        var current = PopoverTheme.Current.Name;

        foreach (var theme in PopoverTheme.AllThemes)
        {
            var item = new ToolStripMenuItem(theme.Name) { Checked = theme.Name == current };
            var captured = theme;
            item.Click += (_, _) =>
            {
                PopoverTheme.Current = captured;
                _controller.ApplyThemeChange();
            };
            root.DropDownItems.Add(item);
        }

        return root;
    }

    private ToolStripMenuItem BuildDisplayMenu()
    {
        var root = new ToolStripMenuItem("Display");
        var pinned = _controller.PinnedMonitorIndex;

        var auto = new ToolStripMenuItem("Auto (Taskbar Display)") { Checked = pinned < 0 };
        auto.Click += (_, _) => _controller.PinnedMonitorIndex = -1;
        root.DropDownItems.Add(auto);

        root.DropDownItems.Add(new ToolStripSeparator());

        for (var i = 0; i < _controller.Monitors.Count; i++)
        {
            var monitor = _controller.Monitors[i];
            var label = monitor.IsPrimary
                ? $"Display {i + 1} ({monitor.Bounds.Width}x{monitor.Bounds.Height}, primary)"
                : $"Display {i + 1} ({monitor.Bounds.Width}x{monitor.Bounds.Height})";

            var item = new ToolStripMenuItem(label) { Checked = pinned == i };
            var index = i;
            item.Click += (_, _) => _controller.PinnedMonitorIndex = index;
            root.DropDownItems.Add(item);
        }

        return root;
    }

    private void ShowOpenClawSettings()
    {
        var window = new OpenClawSettingsWindow();
        if (window.ShowDialog() != true) return;

        // Reconnect anyone already pointed at OpenClaw so new credentials take effect.
        foreach (var walker in _controller.Walkers)
        {
            if (walker.Provider == AgentProviderKind.OpenClaw) walker.ResetSession();
        }

        _ = AgentProvider.DetectAvailableProvidersAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}

using System.Windows;
using System.Windows.Controls;

namespace LilAgents.UI;

/// <summary>
/// Connection settings for a self-hosted OpenClaw gateway. Replaces the macOS NSAlert
/// with accessory view — WPF has no equivalent, so this is a small modal window.
/// </summary>
public sealed class OpenClawSettingsWindow : Window
{
    private readonly TextBox _urlField;
    private readonly PasswordBox _tokenField;
    private readonly TextBox _prefixField;
    private readonly TextBox _agentField;

    public OpenClawSettingsWindow()
    {
        Title = "OpenClaw Connection";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var settings = Settings.Current;

        _urlField = new TextBox { Text = settings.OpenClawGatewayUrl };
        _tokenField = new PasswordBox { Password = settings.OpenClawAuthToken };
        _prefixField = new TextBox { Text = settings.OpenClawSessionPrefix };
        _agentField = new TextBox { Text = settings.OpenClawAgentId ?? "" };

        var grid = new Grid { Margin = new Thickness(16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var row = 0;

        void AddRow(string label, FrameworkElement field, string tooltip)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var text = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 6, 8, 6),
                ToolTip = tooltip
            };
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 0);
            grid.Children.Add(text);

            field.Margin = new Thickness(0, 6, 0, 6);
            field.ToolTip = tooltip;
            Grid.SetRow(field, row);
            Grid.SetColumn(field, 1);
            grid.Children.Add(field);

            row++;
        }

        AddRow("Server Address:", _urlField,
            "WebSocket URL of your OpenClaw gateway, e.g. ws://localhost:3001 or wss://gateway.example.com.");
        AddRow("Auth Token:", _tokenField,
            "Authentication token for your gateway.");
        AddRow("Session Prefix:", _prefixField,
            "Label used to group conversations on the server. Each character gets its own session within this prefix.");
        AddRow("Agent ID:", _agentField,
            "Route messages to a specific agent on the gateway. Leave blank to use the server default.");

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var hint = new TextBlock
        {
            Text = "Your token is stored in settings.json under %APPDATA%\\lil-agents.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            FontSize = 11,
            Margin = new Thickness(0, 8, 0, 0)
        };
        Grid.SetRow(hint, row);
        Grid.SetColumnSpan(hint, 2);
        grid.Children.Add(hint);
        row++;

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var cancel = new Button { Content = "Cancel", Width = 88, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var save = new Button { Content = "Save", Width = 88, Height = 28, IsDefault = true };
        save.Click += (_, _) => Persist();

        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        Grid.SetRow(buttons, row);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);

        Content = grid;
    }

    private void Persist()
    {
        var settings = Settings.Current;
        settings.OpenClawGatewayUrl = string.IsNullOrWhiteSpace(_urlField.Text)
            ? "ws://localhost:3001"
            : _urlField.Text.Trim();
        settings.OpenClawAuthToken = _tokenField.Password;
        settings.OpenClawSessionPrefix = string.IsNullOrWhiteSpace(_prefixField.Text)
            ? "lil-agents"
            : _prefixField.Text.Trim();
        settings.OpenClawAgentId = string.IsNullOrWhiteSpace(_agentField.Text) ? null : _agentField.Text.Trim();
        settings.Save();

        DialogResult = true;
        Close();
    }
}

using Microsoft.Terminal.Wpf;
using System;
using System.Windows;

namespace AgentDock;

public partial class MainWindow : Window
{
    private const string ShellCommand = "powershell.exe";
    private ConptyConnection _connection;

    public MainWindow()
    {
        InitializeComponent();
        Terminal.Loaded += Terminal_Loaded;
        Closed += MainWindow_Closed;
    }

    private void Terminal_Loaded(object sender, RoutedEventArgs e)
    {
        Terminal.Loaded -= Terminal_Loaded;

        SessionLabel.Text = "PowerShell";
        Terminal.SetTheme(CreateTheme(), "Cascadia Code", 13);

        try
        {
            _connection = new ConptyConnection(ShellCommand);
            Terminal.Connection = _connection;
            Terminal.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void MainWindow_Closed(object sender, EventArgs e)
    {
        _connection?.Close();
    }

    private static TerminalTheme CreateTheme()
    {
        return new TerminalTheme
        {
            DefaultBackground = 0x050505,
            DefaultForeground = 0xB8B8B8,
            DefaultSelectionBackground = 0x4A4A4A,
            CursorStyle = CursorStyle.BlinkingBlock,
            ColorTable =
            [
                0x050505, 0x7A3B3B, 0x4D8A66, 0x8A6A2A,
                0x965A2C, 0x7C4A8D, 0x5E7F7F, 0xB8B8B8,
                0x4D4D4D, 0xB85C5C, 0x67B789, 0xC99A3B,
                0xC97D45, 0xA66ABD, 0x7CAAAA, 0xE8E8E8,
            ],
        };
    }
}

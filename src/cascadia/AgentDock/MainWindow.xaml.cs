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
            DefaultBackground = 0x1E1720,
            DefaultForeground = 0xE5E7EB,
            DefaultSelectionBackground = 0x8B9CF0,
            CursorStyle = CursorStyle.SteadyBar,
            ColorTable =
            [
                0x1E1720, 0x5B34DA, 0x0EA56B, 0xB45309,
                0xDD6B20, 0xC026D3, 0xD97706, 0xD1D5DB,
                0x6B7280, 0x7C63F2, 0x34D399, 0xF59E0B,
                0xFB923C, 0xE879F9, 0xFCD34D, 0xF9FAFB,
            ],
        };
    }
}

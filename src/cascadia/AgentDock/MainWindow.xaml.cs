using Microsoft.Terminal.Wpf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace AgentDock;

public partial class MainWindow : Window
{
    private const string ShellCommand = "powershell.exe";

    private readonly List<TerminalSession> _sessions;
    private readonly TerminalControl _activeTerminal;
    private readonly Brush _runningBrush;
    private readonly Brush _idleBrush;
    private readonly Brush _attentionBrush;
    private readonly string _startupDirectory;

    private bool _themeApplied;
    private int _selectedSessionIndex;
    private int _attachedSessionIndex = -1;

    public MainWindow()
    {
        InitializeComponent();

        _startupDirectory = ResolveStartupDirectory();
        _runningBrush = (Brush)FindResource("RunningBrush");
        _idleBrush = (Brush)FindResource("IdleBrush");
        _attentionBrush = (Brush)FindResource("AttentionBrush");

        _activeTerminal = new TerminalControl
        {
            Focusable = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        _sessions = CreateSessions();
        WirePreviewUpdates();

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        ViewModeToggle.Checked += ViewModeToggle_CheckedChanged;
        ViewModeToggle.Unchecked += ViewModeToggle_CheckedChanged;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;

        foreach (TerminalSession session in _sessions)
        {
            session.Connection.EnsureStarted();
            UpdatePreview(session);
        }

        _selectedSessionIndex = 0;
        ApplyLayout();
        AttachActiveTerminal(_selectedSessionIndex, forceReconnect: true);
        FocusSelectedTerminal();
    }

    private void ViewModeToggle_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ApplyLayout();
        AttachActiveTerminal(_selectedSessionIndex, forceReconnect: false);
        FocusSelectedTerminal();
    }

    private void SessionCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        string tagText = (sender as Border)?.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(tagText) || !int.TryParse(tagText, out int sessionIndex))
        {
            return;
        }

        if (IsOverviewMode)
        {
            _selectedSessionIndex = sessionIndex;
            ViewModeToggle.IsChecked = true;
            return;
        }

        SelectSession(sessionIndex);
    }

    private void MainWindow_Closed(object sender, EventArgs e)
    {
        foreach (TerminalSession session in _sessions)
        {
            session.Connection.Dispose();
        }
    }

    private bool IsOverviewMode => ViewModeToggle.IsChecked != true;

    private List<TerminalSession> CreateSessions()
    {
        return
        [
            new TerminalSession(
                0,
                "PowerShell 01",
                SessionVisualState.Running,
                SessionCard0,
                SessionHost0,
                PreviewPanel0,
                PreviewText0,
                SessionFade0,
                SessionStatus0,
                SessionTitle0,
                SessionBadge0,
                SessionFooter0,
                new BufferedTerminalConnection(ShellCommand, _startupDirectory)),
            new TerminalSession(
                1,
                "PowerShell 02",
                SessionVisualState.Attention,
                SessionCard1,
                SessionHost1,
                PreviewPanel1,
                PreviewText1,
                SessionFade1,
                SessionStatus1,
                SessionTitle1,
                SessionBadge1,
                SessionFooter1,
                new BufferedTerminalConnection(ShellCommand, _startupDirectory)),
            new TerminalSession(
                2,
                "PowerShell 03",
                SessionVisualState.Running,
                SessionCard2,
                SessionHost2,
                PreviewPanel2,
                PreviewText2,
                SessionFade2,
                SessionStatus2,
                SessionTitle2,
                SessionBadge2,
                SessionFooter2,
                new BufferedTerminalConnection(ShellCommand, _startupDirectory)),
            new TerminalSession(
                3,
                "PowerShell 04",
                SessionVisualState.Running,
                SessionCard3,
                SessionHost3,
                PreviewPanel3,
                PreviewText3,
                SessionFade3,
                SessionStatus3,
                SessionTitle3,
                SessionBadge3,
                SessionFooter3,
                new BufferedTerminalConnection(ShellCommand, _startupDirectory)),
        ];
    }

    private void WirePreviewUpdates()
    {
        foreach (TerminalSession session in _sessions)
        {
            session.Connection.PreviewUpdated += (_, _) =>
            {
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                {
                    return;
                }

                _ = Dispatcher.InvokeAsync(() => UpdatePreview(session), DispatcherPriority.Background);
            };
        }
    }

    private void UpdatePreview(TerminalSession session)
    {
        session.PreviewText.Text = session.Connection.PreviewText;
    }

    private void SelectSession(int sessionIndex)
    {
        if (sessionIndex < 0 || sessionIndex >= _sessions.Count)
        {
            return;
        }

        _selectedSessionIndex = sessionIndex;
        ApplyLayout();
        AttachActiveTerminal(sessionIndex, forceReconnect: _attachedSessionIndex != sessionIndex);
        FocusSelectedTerminal();
    }

    private void ApplyLayout()
    {
        ViewModeToggle.ToolTip = IsOverviewMode
            ? $"Open {_sessions[_selectedSessionIndex].Title}"
            : "Return to the 2-column overview";

        if (IsOverviewMode)
        {
            ApplyOverviewLayout();
        }
        else
        {
            ApplyFocusedLayout();
        }

        ApplySessionVisualState();
    }

    private void ApplyOverviewLayout()
    {
        SidebarHeader.Visibility = Visibility.Collapsed;

        SurfaceColumn0.Width = new GridLength(1, GridUnitType.Star);
        SurfaceColumnGap.Width = new GridLength(24);
        SurfaceColumn1.Width = new GridLength(1, GridUnitType.Star);

        SurfaceRow0.Height = new GridLength(1, GridUnitType.Star);
        SurfaceGap0.Height = new GridLength(24);
        SurfaceRow1.Height = new GridLength(0);
        SurfaceGap1.Height = new GridLength(0);
        SurfaceRow2.Height = new GridLength(1, GridUnitType.Star);
        SurfaceGap2.Height = new GridLength(0);
        SurfaceRow3.Height = new GridLength(0);

        SetCardPlacement(_sessions[0], row: 0, column: 0, rowSpan: 1);
        SetCardPlacement(_sessions[1], row: 0, column: 2, rowSpan: 1);
        SetCardPlacement(_sessions[2], row: 4, column: 0, rowSpan: 1);
        SetCardPlacement(_sessions[3], row: 4, column: 2, rowSpan: 1);
    }

    private void ApplyFocusedLayout()
    {
        SidebarHeader.Visibility = Visibility.Visible;

        SurfaceColumn0.Width = new GridLength(292);
        SurfaceColumnGap.Width = new GridLength(24);
        SurfaceColumn1.Width = new GridLength(1, GridUnitType.Star);

        SurfaceRow0.Height = GridLength.Auto;
        SurfaceGap0.Height = new GridLength(14);
        SurfaceRow1.Height = new GridLength(1, GridUnitType.Star);
        SurfaceGap1.Height = new GridLength(14);
        SurfaceRow2.Height = new GridLength(1, GridUnitType.Star);
        SurfaceGap2.Height = new GridLength(14);
        SurfaceRow3.Height = new GridLength(1, GridUnitType.Star);

        TerminalSession selected = _sessions[_selectedSessionIndex];
        List<TerminalSession> backgroundSessions = _sessions.Where(session => session.Index != _selectedSessionIndex).ToList();

        SetCardPlacement(selected, row: 0, column: 2, rowSpan: 7);
        SetCardPlacement(backgroundSessions[0], row: 2, column: 0, rowSpan: 1);
        SetCardPlacement(backgroundSessions[1], row: 4, column: 0, rowSpan: 1);
        SetCardPlacement(backgroundSessions[2], row: 6, column: 0, rowSpan: 1);
    }

    private static void SetCardPlacement(TerminalSession session, int row, int column, int rowSpan)
    {
        Grid.SetRow(session.Card, row);
        Grid.SetColumn(session.Card, column);
        Grid.SetRowSpan(session.Card, rowSpan);
        session.Card.Visibility = Visibility.Visible;
    }

    private void ApplySessionVisualState()
    {
        foreach (TerminalSession session in _sessions)
        {
            bool isSelected = session.Index == _selectedSessionIndex;
            bool isFocusedMain = !IsOverviewMode && isSelected;

            session.Card.Style = ResolveCardStyle(session, isSelected);
            session.StatusDot.Fill = ResolveStatusBrush(session, isSelected);
            session.TitleText.Text = session.Title;
            session.BadgeText.Text = ResolveBadgeText(session, isSelected);
            session.BadgeText.Foreground = ResolveBadgeBrush(session, isSelected);
            session.PreviewPanel.Visibility = isSelected ? Visibility.Collapsed : Visibility.Visible;
            session.SessionHost.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
            session.FadeOverlay.Visibility = isSelected ? Visibility.Collapsed : Visibility.Visible;
            session.Footer.Visibility = isFocusedMain ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private Style ResolveCardStyle(TerminalSession session, bool isSelected)
    {
        if (isSelected)
        {
            return (Style)FindResource("SelectedTerminalCardStyle");
        }

        if (session.VisualState == SessionVisualState.Attention)
        {
            return (Style)FindResource("AttentionCardStyle");
        }

        return (Style)FindResource("TerminalCardStyle");
    }

    private Brush ResolveStatusBrush(TerminalSession session, bool isSelected)
    {
        if (session.VisualState == SessionVisualState.Attention && !isSelected)
        {
            return _attentionBrush;
        }

        if (session.VisualState == SessionVisualState.Idle)
        {
            return _idleBrush;
        }

        return _runningBrush;
    }

    private Brush ResolveBadgeBrush(TerminalSession session, bool isSelected)
    {
        if (session.VisualState == SessionVisualState.Attention && !isSelected)
        {
            return _attentionBrush;
        }

        return (Brush)FindResource("TextBrush");
    }

    private static string ResolveBadgeText(TerminalSession session, bool isSelected)
    {
        if (isSelected)
        {
            return "active";
        }

        return session.VisualState switch
        {
            SessionVisualState.Attention => "attention",
            SessionVisualState.Idle => "idle",
            _ => "running",
        };
    }

    private void AttachActiveTerminal(int sessionIndex, bool forceReconnect)
    {
        TerminalSession session = _sessions[sessionIndex];

        if (_activeTerminal.Parent is Panel currentHost && !ReferenceEquals(currentHost, session.SessionHost))
        {
            currentHost.Children.Remove(_activeTerminal);
        }

        if (!session.SessionHost.Children.Contains(_activeTerminal))
        {
            session.SessionHost.Children.Add(_activeTerminal);
        }

        if (!_themeApplied)
        {
            _activeTerminal.SetTheme(CreateTheme(), "Cascadia Code", 13);
            _themeApplied = true;
        }

        if (forceReconnect || _attachedSessionIndex != sessionIndex)
        {
            _activeTerminal.Connection = session.Connection;
            _attachedSessionIndex = sessionIndex;
        }
    }

    private void FocusSelectedTerminal()
    {
        _ = Dispatcher.InvokeAsync(() => _activeTerminal.Focus(), DispatcherPriority.Background);
    }

    private static string ResolveStartupDirectory()
    {
        string userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return userProfile;
        }

        string fallbackProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(fallbackProfile))
        {
            return fallbackProfile;
        }

        return Environment.CurrentDirectory;
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

    private sealed class TerminalSession
    {
        public TerminalSession(
            int index,
            string title,
            SessionVisualState visualState,
            Border card,
            Grid sessionHost,
            Grid previewPanel,
            TextBlock previewText,
            Rectangle fadeOverlay,
            Ellipse statusDot,
            TextBlock titleText,
            TextBlock badgeText,
            DockPanel footer,
            BufferedTerminalConnection connection)
        {
            Index = index;
            Title = title;
            VisualState = visualState;
            Card = card;
            SessionHost = sessionHost;
            PreviewPanel = previewPanel;
            PreviewText = previewText;
            FadeOverlay = fadeOverlay;
            StatusDot = statusDot;
            TitleText = titleText;
            BadgeText = badgeText;
            Footer = footer;
            Connection = connection;
        }

        public int Index { get; }
        public string Title { get; }
        public SessionVisualState VisualState { get; }
        public Border Card { get; }
        public Grid SessionHost { get; }
        public Grid PreviewPanel { get; }
        public TextBlock PreviewText { get; }
        public Rectangle FadeOverlay { get; }
        public Ellipse StatusDot { get; }
        public TextBlock TitleText { get; }
        public TextBlock BadgeText { get; }
        public DockPanel Footer { get; }
        public BufferedTerminalConnection Connection { get; }
    }

    private sealed class BufferedTerminalConnection : ITerminalConnection, IDisposable
    {
        private const int MaxRawChars = 262144;
        private const int MaxPreviewChars = 4096;

        private static readonly Regex OscRegex = new(@"\x1B\].*?(?:\x07|\x1B\\)", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex CsiRegex = new(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);
        private static readonly Regex EscRegex = new(@"\x1B[@-_]", RegexOptions.Compiled);

        private readonly ConptyConnection _backend;
        private readonly object _sync = new();
        private readonly Queue<string> _rawChunks = new();
        private readonly StringBuilder _preview = new();
        private readonly StringBuilder _pendingReplay = new();

        private bool _backendStarted;
        private bool _disposed;
        private bool _replaying;
        private int _rawCharCount;

        public BufferedTerminalConnection(string commandLine, string workingDirectory)
        {
            _backend = new ConptyConnection(commandLine, workingDirectory);
            _backend.TerminalOutput += Backend_TerminalOutput;
        }

        public event EventHandler<TerminalOutputEventArgs> TerminalOutput;
        public event EventHandler PreviewUpdated;

        public string PreviewText
        {
            get
            {
                lock (_sync)
                {
                    return _preview.Length == 0 ? "Starting session..." : _preview.ToString().TrimStart('\n');
                }
            }
        }

        public void EnsureStarted()
        {
            StartBackendIfNeeded(replayTranscript: false);
        }

        public void Start()
        {
            StartBackendIfNeeded(replayTranscript: true);
        }

        public void WriteInput(string data)
        {
            _backend.WriteInput(data);
        }

        public void Resize(uint rows, uint columns)
        {
            _backend.Resize(rows, columns);
        }

        public void Close()
        {
            Dispose();
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            _backend.TerminalOutput -= Backend_TerminalOutput;
            _backend.Dispose();
        }

        private void StartBackendIfNeeded(bool replayTranscript)
        {
            bool shouldStartBackend;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                shouldStartBackend = !_backendStarted;
                if (shouldStartBackend)
                {
                    _backendStarted = true;
                }
            }

            if (shouldStartBackend)
            {
                try
                {
                    _backend.Start();
                }
                catch
                {
                    lock (_sync)
                    {
                        _backendStarted = false;
                    }

                    throw;
                }

                return;
            }

            if (replayTranscript)
            {
                ReplayTranscript();
            }
        }

        private void ReplayTranscript()
        {
            string transcript;
            lock (_sync)
            {
                _replaying = true;
                _pendingReplay.Clear();
                transcript = string.Concat(_rawChunks);
            }

            if (!string.IsNullOrEmpty(transcript))
            {
                TerminalOutput?.Invoke(this, new TerminalOutputEventArgs(transcript));
            }

            string pendingOutput;
            lock (_sync)
            {
                pendingOutput = _pendingReplay.ToString();
                _pendingReplay.Clear();
                _replaying = false;
            }

            if (!string.IsNullOrEmpty(pendingOutput))
            {
                TerminalOutput?.Invoke(this, new TerminalOutputEventArgs(pendingOutput));
            }
        }

        private void Backend_TerminalOutput(object sender, TerminalOutputEventArgs e)
        {
            bool forwardLiveOutput;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                string data = e.Data;
                _rawChunks.Enqueue(data);
                _rawCharCount += data.Length;
                while (_rawCharCount > MaxRawChars && _rawChunks.Count > 1)
                {
                    string removed = _rawChunks.Dequeue();
                    _rawCharCount -= removed.Length;
                }

                AppendPreviewText(SanitizePreview(data));

                forwardLiveOutput = !_replaying;
                if (!forwardLiveOutput)
                {
                    _pendingReplay.Append(data);
                }
            }

            if (forwardLiveOutput)
            {
                TerminalOutput?.Invoke(this, e);
            }

            PreviewUpdated?.Invoke(this, EventArgs.Empty);
        }

        private void AppendPreviewText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            _preview.Append(text);
            if (_preview.Length > MaxPreviewChars)
            {
                _preview.Remove(0, _preview.Length - MaxPreviewChars);
            }
        }

        private static string SanitizePreview(string data)
        {
            if (string.IsNullOrEmpty(data))
            {
                return string.Empty;
            }

            string sanitized = OscRegex.Replace(data, string.Empty);
            sanitized = CsiRegex.Replace(sanitized, string.Empty);
            sanitized = EscRegex.Replace(sanitized, string.Empty);

            StringBuilder builder = new(sanitized.Length);
            foreach (char character in sanitized)
            {
                if (character == '\r')
                {
                    continue;
                }

                if (character == '\n' || character == '\t' || !char.IsControl(character))
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }
    }

    private enum SessionVisualState
    {
        Running,
        Idle,
        Attention,
    }
}

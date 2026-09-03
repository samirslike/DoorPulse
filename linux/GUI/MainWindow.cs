using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace DoorPulse.Linux.Gui;

public sealed class MainWindow : Window
{
    private static readonly IBrush Navy = Brush("#0D2A46");
    private static readonly IBrush Navy2 = Brush("#132F4C");
    private static readonly IBrush Accent = Brush("#0B74D1");
    private static readonly IBrush Bg = Brush("#F5F7FA");
    private static readonly IBrush Text = Brush("#172033");
    private static readonly IBrush Muted = Brush("#6B7280");
    private static readonly IBrush Line = Brush("#E5EAF0");
    private static readonly IBrush Good = Brush("#157347");
    private static readonly IBrush Warn = Brush("#B54708");
    private static readonly IBrush Bad = Brush("#B42318");

    private readonly string _home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private readonly string _dpHome;
    private readonly string _configPath;
    private readonly string _tokenPath;
    private readonly string _ftpPasswordPath;
    private readonly string _appDir;

    private readonly Grid _content = new();
    private readonly TextBlock _pageTitle = Label("Dashboard", 24, Text, true);
    private readonly TextBlock _pageSubtitle = Label("Monitor your Linux recorder and recent activity.", 12, Muted);
    private readonly TextBlock _topStatus = Label("Checking...", 12, Muted, true);
    private readonly TextBlock _sidebarStatus = Label("Checking...", 13, Brushes.White, true);
    private TextBlock _recorderValue = Label("Checking...", 20, Text, true);
    private TextBlock _cameraValue = Label("Checking...", 20, Text, true);
    private TextBlock _lengthValue = Label("25 sec", 20, Text, true);
    private TextBlock _storageValue = Label("Local", 20, Text, true);
    private TextBox _recentLog = LogBox(310);
    private TextBox _fullLog = LogBox(560);
    private StackPanel _videoList = new() { Spacing = 10 };
    private TextBlock _videoSummary = Label("Loading local recordings...", 12, Muted);
    private StackPanel _diagnosticsList = new() { Spacing = 10 };
    private ComboBox _storageCombo = new() { Width = 220 };
    private ComboBox _presetCombo = new() { Width = 220 };
    private TextBox _recordingDirectoryBox = new() { Width = 520 };
    private TextBox _ftpHostBox = new() { Width = 440 };
    private TextBox _ftpUsernameBox = new() { Width = 440 };
    private TextBox _ftpRemotePathBox = new() { Width = 440 };
    private TextBox _ftpPasswordBox = new() { Width = 440, PasswordChar = '●' };
    private TextBox _viewerUrlBox = new() { Width = 440 };
    private TextBlock _cloudStatus = Label("", 11, Muted);
    private TextBlock _settingsMessage = Label("", 12, Muted);
    private readonly DispatcherTimer _timer;

    public MainWindow()
    {
        _dpHome = Environment.GetEnvironmentVariable("DOORPULSE_HOME")
                  ?? Path.Combine(_home, ".local", "share", "DoorPulse");
        _configPath = Path.Combine(_dpHome, "config.json");
        _tokenPath = Path.Combine(_dpHome, "refresh-token.txt");
        _ftpPasswordPath = Path.Combine(_dpHome, "ftp-password.txt");
        _appDir = Environment.GetEnvironmentVariable("DOORPULSE_APP_DIR")
                  ?? Directory.GetCurrentDirectory();

        Title = "DoorPulse Linux";
        Width = 1180;
        Height = 760;
        MinWidth = 980;
        MinHeight = 650;
        Background = Bg;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = BuildShell();
        ShowDashboard();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += async (_, _) => await RefreshStatusAsync();
        _timer.Start();

        Opened += async (_, _) => await RefreshStatusAsync();
    }

    private Control BuildShell()
    {
        var shell = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("230,*")
        };

        shell.Children.Add(BuildSidebar());
        Grid.SetColumn(_content, 1);
        _content.RowDefinitions = new RowDefinitions("74,*");
        _content.Children.Add(BuildTopbar());
        shell.Children.Add(_content);
        return shell;
    }

    private Control BuildSidebar()
    {
        var border = new Border { Background = Navy };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };

        var brand = new StackPanel
        {
            Margin = new Thickness(20, 22, 18, 16),
            Orientation = Orientation.Horizontal,
            Spacing = 11
        };
        brand.Children.Add(new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(13),
            Background = Accent,
            Child = Label("D", 24, Brushes.White, true, HorizontalAlignment.Center, VerticalAlignment.Center)
        });
        var brandText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        brandText.Children.Add(Label("DoorPulse", 21, Brushes.White, true));
        brandText.Children.Add(Label("Linux Recorder", 11, Brush("#AFC6DD")));
        brand.Children.Add(brandText);
        grid.Children.Add(brand);

        var nav = new StackPanel { Margin = new Thickness(10, 12, 10, 0), Spacing = 6 };
        nav.Children.Add(NavButton("●   Dashboard", ShowDashboard));
        nav.Children.Add(NavButton("▶   Videos", ShowVideos));
        nav.Children.Add(NavButton("⚙   Settings", ShowSettings));
        nav.Children.Add(NavButton("≡   Activity Logs", ShowLogs));
        nav.Children.Add(NavButton("✓   Diagnostics", ShowDiagnostics));
        Grid.SetRow(nav, 1);
        grid.Children.Add(nav);

        var statusCard = new Border
        {
            Margin = new Thickness(16, 14, 16, 18),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            Background = Navy2,
            Child = new StackPanel
            {
                Children =
                {
                    Label("Recorder status", 11, Brush("#AFC6DD")),
                    _sidebarStatus
                }
            }
        };
        Grid.SetRow(statusCard, 2);
        grid.Children.Add(statusCard);
        border.Child = grid;
        return border;
    }

    private Control BuildTopbar()
    {
        var border = new Border
        {
            Background = Brushes.White,
            BorderBrush = Line,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var grid = new Grid { Margin = new Thickness(24, 0), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var headings = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        headings.Children.Add(_pageTitle);
        headings.Children.Add(_pageSubtitle);
        grid.Children.Add(headings);

        var pill = new Border
        {
            Padding = new Thickness(12, 7),
            CornerRadius = new CornerRadius(16),
            Background = Brush("#EEF2F6"),
            Child = _topStatus,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(pill, 1);
        grid.Children.Add(pill);
        border.Child = grid;
        return border;
    }

    private async void ShowDashboard()
    {
        SetPage("Dashboard", "Monitor your Linux recorder and recent activity.");

        _recorderValue = Label("Checking...", 20, Text, true);
        _cameraValue = Label("Checking...", 20, Text, true);
        _lengthValue = Label("25 sec", 20, Text, true);
        _storageValue = Label("Local", 20, Text, true);
        _recentLog = LogBox(310);

        var stack = new StackPanel { Margin = new Thickness(24), Spacing = 14 };

        var cards = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 12 };
        cards.Children.Add(MetricCard("RECORDER", _recorderValue, "systemd background service", 0));
        cards.Children.Add(MetricCard("CAMERAS", _cameraValue, "Ring cameras monitored", 1));
        cards.Children.Add(MetricCard("RECORDING", _lengthValue, "Push-first capture", 2));
        cards.Children.Add(MetricCard("STORAGE", _storageValue, "Local / Cloud / Both", 3));
        stack.Children.Add(cards);

        var controlGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 14 };
        var controlText = new StackPanel();
        controlText.Children.Add(Label("Recorder Control", 18, Text, true));
        controlText.Children.Add(Label("Start, stop or restart the Linux background recorder.", 12, Muted));
        controlGrid.Children.Add(controlText);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(ActionButton("Start", true, async () => await ServiceActionAsync("start")));
        buttons.Children.Add(ActionButton("Restart", false, async () => await ServiceActionAsync("restart")));
        buttons.Children.Add(ActionButton("Stop", false, async () => await ServiceActionAsync("stop")));
        Grid.SetColumn(buttons, 1);
        controlGrid.Children.Add(buttons);
        stack.Children.Add(Card(controlGrid));

        var logHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        logHeader.Children.Add(Label("Recent Activity", 18, Text, true));
        var refresh = ActionButton("Refresh", false, RefreshLogsAsync);
        Grid.SetColumn(refresh, 1);
        logHeader.Children.Add(refresh);
        var logPanel = new StackPanel { Spacing = 10 };
        logPanel.Children.Add(logHeader);
        logPanel.Children.Add(_recentLog);
        stack.Children.Add(Card(logPanel));

        SetBody(new ScrollViewer { Content = stack, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
        await RefreshStatusAsync();
        await RefreshLogsAsync();
    }

    private async void ShowVideos()
    {
        SetPage("Videos", "Browse locally stored motion and doorbell recordings.");

        _videoList = new StackPanel { Spacing = 10 };
        _videoSummary = Label("Loading local recordings...", 12, Muted);

        var root = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        var htext = new StackPanel();
        htext.Children.Add(Label("Video Library", 20, Text, true));
        htext.Children.Add(_videoSummary);
        header.Children.Add(htext);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(ActionButton("Refresh", false, RefreshVideosAsync));
        actions.Children.Add(ActionButton("Open Folder", false, OpenVideoFolderAsync));
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        root.Children.Add(Card(header));
        root.Children.Add(_videoList);
        SetBody(new ScrollViewer { Content = root, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
        await RefreshVideosAsync();
    }

    private async void ShowSettings()
    {
        SetPage("Settings", "Configure recording, local storage and cloud upload.");

        _storageCombo = new ComboBox { Width = 220 };
        _presetCombo = new ComboBox { Width = 220 };
        _recordingDirectoryBox = new TextBox
        {
            Width = 520,
            PlaceholderText = "Leave blank to use ~/Videos/DoorPulse"
        };
        _ftpHostBox = new TextBox { Width = 440, PlaceholderText = "Example: ftp.example.com" };
        _ftpUsernameBox = new TextBox { Width = 440, PlaceholderText = "FTP username" };
        _ftpRemotePathBox = new TextBox { Width = 440, PlaceholderText = "/path/to/recordings" };
        _ftpPasswordBox = new TextBox
        {
            Width = 440,
            PasswordChar = '●',
            PlaceholderText = "Leave blank to keep the saved password"
        };
        _viewerUrlBox = new TextBox { Width = 440, PlaceholderText = "Optional: https://example.com/viewer/" };
        _cloudStatus = Label("", 11, Muted);
        _settingsMessage = Label("", 12, Muted);

        _storageCombo.ItemsSource = new[] { "Local", "Cloud", "Both" };
        _presetCombo.ItemsSource = new[] { "Short — 15 sec", "Normal — 25 sec", "Long — 45 sec" };

        var panel = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 14,
            MaxWidth = 820,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var recordingForm = new StackPanel { Spacing = 16 };
        recordingForm.Children.Add(Label("Recording & Storage", 18, Text, true));
        recordingForm.Children.Add(Field(
            "Storage mode",
            "Local keeps recordings on this Linux PC. Cloud uploads them. Both keeps a local copy and uploads a cloud copy.",
            _storageCombo));
        recordingForm.Children.Add(Field(
            "Recording preset",
            "Length used for new motion/doorbell recordings.",
            _presetCombo));
        recordingForm.Children.Add(Field(
            "Local recording folder",
            "Optional. Leave blank to automatically use the current Linux user's ~/Videos/DoorPulse folder.",
            _recordingDirectoryBox));
        recordingForm.Children.Add(new Border { Height = 1, Background = Line, Margin = new Thickness(0, 2) });
        recordingForm.Children.Add(Label($"Configuration: {_configPath}", 11, Muted));
        recordingForm.Children.Add(Label($"Current local recordings: {GetRecordingDirectory()}", 11, Muted));
        panel.Children.Add(Card(recordingForm));

        var cloudForm = new StackPanel { Spacing = 14 };
        cloudForm.Children.Add(Label("Cloud Storage (FTP)", 18, Text, true));
        cloudForm.Children.Add(Label(
            "Required only when Storage mode is Cloud or Both. The FTP password is stored separately from config.json.",
            11, Muted));
        cloudForm.Children.Add(Field("FTP host", "Server hostname or IP address.", _ftpHostBox));
        cloudForm.Children.Add(Field("FTP username", "Account used for DoorPulse uploads.", _ftpUsernameBox));
        cloudForm.Children.Add(Field("FTP remote path", "Remote folder where DoorPulse creates YYYY/MM/DD folders.", _ftpRemotePathBox));
        cloudForm.Children.Add(Field("FTP password", "Masked and saved to ~/.local/share/DoorPulse/ftp-password.txt.", _ftpPasswordBox));
        cloudForm.Children.Add(Field("Viewer URL", "Optional web viewer address for your cloud recordings.", _viewerUrlBox));

        var cloudActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
        cloudActions.Children.Add(ActionButton("Test Connection", false, TestCloudConnectionAsync));
        cloudActions.Children.Add(ActionButton("Open Viewer", false, OpenViewerAsync));
        cloudForm.Children.Add(cloudActions);

        cloudForm.Children.Add(_cloudStatus);
        panel.Children.Add(Card(cloudForm));

        var saveForm = new StackPanel { Spacing = 10 };
        saveForm.Children.Add(_settingsMessage);
        saveForm.Children.Add(ActionButton("Save Settings", true, SaveSettingsAsync));
        panel.Children.Add(Card(saveForm));

        SetBody(new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        });

        await LoadSettingsAsync();
    }

    private async void ShowLogs()
    {
        SetPage("Activity Logs", "Live recorder output from the Linux systemd journal.");

        _fullLog = LogBox(560);

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        top.Children.Add(Label("Recorder Journal", 18, Text, true));
        var refresh = ActionButton("Refresh", false, RefreshLogsAsync);
        Grid.SetColumn(refresh, 1);
        top.Children.Add(refresh);
        panel.Children.Add(Card(top));
        panel.Children.Add(_fullLog);
        SetBody(panel);
        await RefreshLogsAsync();
    }

    private async void ShowDiagnostics()
    {
        SetPage("Diagnostics", "Check Linux, Ring and recorder requirements.");

        _diagnosticsList = new StackPanel { Spacing = 10 };

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        top.Children.Add(Label("System Checks", 20, Text, true));
        var refresh = ActionButton("Run Checks", true, RefreshDiagnosticsAsync);
        Grid.SetColumn(refresh, 1);
        top.Children.Add(refresh);
        panel.Children.Add(Card(top));
        panel.Children.Add(_diagnosticsList);
        SetBody(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
        await RefreshDiagnosticsAsync();
    }

    private void SetPage(string title, string subtitle)
    {
        _pageTitle.Text = title;
        _pageSubtitle.Text = subtitle;
    }

    private void SetBody(Control body)
    {
        if (_content.Children.Count > 1)
            _content.Children.RemoveAt(1);
        Grid.SetRow(body, 1);
        _content.Children.Add(body);
    }

    private async Task RefreshStatusAsync()
    {
        var active = (await RunAsync("systemctl", "--user", "is-active", "doorpulse-recorder.service")).Trim() == "active";
        var enabled = (await RunAsync("systemctl", "--user", "is-enabled", "doorpulse-recorder.service")).Trim();
        var config = ReadConfig();
        var cameras = config?["cameras"] as JsonArray;
        var enabledCameras = cameras?.Count(n => n?["enabled"]?.GetValue<bool>() != false) ?? 0;
        var preset = config?["recordingPreset"]?.GetValue<string>()?.ToLowerInvariant() ?? "normal";
        var storage = config?["storageMode"]?.GetValue<string>()?.ToLowerInvariant() ?? "local";

        _recorderValue.Text = active ? "Running" : (enabled.Contains("enabled") ? "Stopped" : "Not installed");
        _recorderValue.Foreground = active ? Good : Warn;
        _cameraValue.Text = enabledCameras == 1 ? "1 camera" : $"{enabledCameras} cameras";
        _lengthValue.Text = preset switch { "short" => "15 sec", "long" => "45 sec", _ => "25 sec" };
        _storageValue.Text = storage switch { "cloud" => "Cloud", "both" => "Local + Cloud", _ => "Local" };
        _sidebarStatus.Text = active ? "● Running" : "○ Stopped";
        _topStatus.Text = active ? "● Recorder running" : "○ Recorder stopped";
        _topStatus.Foreground = active ? Good : Warn;
    }

    private async Task ServiceActionAsync(string action)
    {
        var unit = Path.Combine(_home, ".config", "systemd", "user", "doorpulse-recorder.service");
        if (!File.Exists(unit) && action == "start")
        {
            var installer = Path.Combine(_appDir, "install-service.sh");
            if (File.Exists(installer))
                await RunAsync("bash", installer);
        }
        else
        {
            await RunAsync("systemctl", "--user", action, "doorpulse-recorder.service");
        }

        await Task.Delay(500);
        await RefreshStatusAsync();
        await RefreshLogsAsync();
    }

    private async Task RefreshLogsAsync()
    {
        var logs = await RunAsync("journalctl", "--user", "-u", "doorpulse-recorder.service", "-n", "250", "--no-pager");
        if (string.IsNullOrWhiteSpace(logs))
            logs = "No systemd recorder logs yet. Install/start the background service from Dashboard.";
        _fullLog.Text = logs;
        var lines = logs.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        _recentLog.Text = string.Join('\n', lines.TakeLast(Math.Min(70, lines.Length)));
        _recentLog.CaretIndex = _recentLog.Text?.Length ?? 0;
        _fullLog.CaretIndex = _fullLog.Text?.Length ?? 0;
    }

    private async Task RefreshVideosAsync()
    {
        _videoList.Children.Clear();
        var dir = GetRecordingDirectory();
        if (!Directory.Exists(dir))
        {
            _videoSummary.Text = "Recording directory does not exist yet.";
            _videoList.Children.Add(Card(Label("No local recordings yet.", 14, Muted)));
            return;
        }

        var files = Directory.EnumerateFiles(dir, "*.mp4", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .Take(100)
            .ToList();
        _videoSummary.Text = files.Count == 1 ? "1 local recording" : $"{files.Count} recent local recordings";

        if (files.Count == 0)
        {
            _videoList.Children.Add(Card(Label("No local recordings yet. Trigger a Ring motion event to create one.", 14, Muted)));
            return;
        }

        foreach (var file in files)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            var left = new StackPanel { Spacing = 3 };
            left.Children.Add(Label(ParseFriendlyName(file.Name), 14, Text, true));
            left.Children.Add(Label($"{file.LastWriteTime:g}   •   {FormatBytes(file.Length)}", 11, Muted));
            left.Children.Add(Label(file.FullName, 10, Muted));
            row.Children.Add(left);
            var play = ActionButton("▶ Play", true, async () => await OpenVideoAsync(file.FullName));
            Grid.SetColumn(play, 1);
            row.Children.Add(play);
            _videoList.Children.Add(Card(row));
        }
    }

    private async Task OpenVideoAsync(string file)
    {
        if (!File.Exists(file))
            return;

        if (IsWsl())
        {
            try
            {
                var windowsPath = (await RunAsync("wslpath", "-w", file)).Trim();

                if (!string.IsNullOrWhiteSpace(windowsPath) &&
                    !windowsPath.StartsWith("wslpath:", StringComparison.OrdinalIgnoreCase))
                {
                    // Use the Windows file association so an MP4 opens in the user's
                    // normal Windows video player while DoorPulse is being tested in WSL.
                    var escaped = windowsPath.Replace("'", "''");
                    var psi = new ProcessStartInfo("powershell.exe")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    psi.ArgumentList.Add("-NoProfile");
                    psi.ArgumentList.Add("-Command");
                    psi.ArgumentList.Add($"Start-Process -FilePath '{escaped}'");
                    Process.Start(psi);
                    return;
                }
            }
            catch
            {
                // Fall through to native Linux opener.
            }
        }

        await OpenPathAsync(file);
    }

    private async Task OpenVideoFolderAsync()
    {
        var folder = GetRecordingDirectory();

        if (IsWsl())
        {
            try
            {
                var windowsPath = (await RunAsync("wslpath", "-w", folder)).Trim();
                if (!string.IsNullOrWhiteSpace(windowsPath) &&
                    !windowsPath.StartsWith("wslpath:", StringComparison.OrdinalIgnoreCase))
                {
                    var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                    psi.ArgumentList.Add(windowsPath);
                    Process.Start(psi);
                    return;
                }
            }
            catch { }
        }

        await OpenPathAsync(folder);
    }

    private async Task LoadSettingsAsync()
    {
        var config = ReadConfig();
        var storage = config?["storageMode"]?.GetValue<string>()?.ToLowerInvariant() ?? "local";
        var preset = config?["recordingPreset"]?.GetValue<string>()?.ToLowerInvariant() ?? "normal";

        _storageCombo.SelectedIndex = storage switch { "cloud" => 1, "both" => 2, _ => 0 };
        _presetCombo.SelectedIndex = preset switch { "short" => 0, "long" => 2, _ => 1 };

        _recordingDirectoryBox.Text = config?["recordingDirectory"]?.GetValue<string>() ?? "";
        _ftpHostBox.Text = config?["ftpHost"]?.GetValue<string>() ?? "";
        _ftpUsernameBox.Text = config?["ftpUsername"]?.GetValue<string>() ?? "";
        _ftpRemotePathBox.Text = config?["ftpRemotePath"]?.GetValue<string>() ?? "";
        _viewerUrlBox.Text = config?["viewerUrl"]?.GetValue<string>() ?? "";
        _ftpPasswordBox.Text = "";

        _cloudStatus.Text = File.Exists(_ftpPasswordPath)
            ? "✓ FTP password is already saved."
            : "FTP password has not been saved yet.";
        _cloudStatus.Foreground = File.Exists(_ftpPasswordPath) ? Good : Muted;

        _settingsMessage.Text = "";
        await Task.CompletedTask;
    }

    private async Task TestCloudConnectionAsync()
    {
        var host = (_ftpHostBox.Text ?? "").Trim();
        var username = (_ftpUsernameBox.Text ?? "").Trim();
        var remotePath = (_ftpRemotePathBox.Text ?? "").Trim();
        var password = _ftpPasswordBox.Text ?? "";

        if (string.IsNullOrWhiteSpace(password) && File.Exists(_ftpPasswordPath))
        {
            try { password = (await File.ReadAllTextAsync(_ftpPasswordPath)).Trim(); }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(remotePath) ||
            string.IsNullOrWhiteSpace(password))
        {
            _cloudStatus.Text = "Enter FTP host, username, remote path and password before testing.";
            _cloudStatus.Foreground = Warn;
            return;
        }

        _cloudStatus.Text = "Testing FTP connection...";
        _cloudStatus.Foreground = Muted;

        var result = await RunFtpTestAsync(host, username, password, remotePath);

        if (result.Success)
        {
            _cloudStatus.Text = "✓ Cloud connection successful. Remote folder is reachable.";
            _cloudStatus.Foreground = Good;
        }
        else
        {
            _cloudStatus.Text = $"Cloud connection failed: {result.Message}";
            _cloudStatus.Foreground = Bad;
        }
    }

    private async Task OpenViewerAsync()
    {
        var url = (_viewerUrlBox.Text ?? "").Trim();

        if (string.IsNullOrWhiteSpace(url))
        {
            _cloudStatus.Text = "Enter a Viewer URL first.";
            _cloudStatus.Foreground = Warn;
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            _cloudStatus.Text = "Viewer URL must begin with http:// or https://";
            _cloudStatus.Foreground = Warn;
            return;
        }

        try
        {
            if (IsWsl())
            {
                var escaped = url.Replace("'", "''");
                var psi = new ProcessStartInfo("powershell.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-Command");
                psi.ArgumentList.Add($"Start-Process '{escaped}'");
                Process.Start(psi);
            }
            else
            {
                var psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                psi.ArgumentList.Add(url);
                Process.Start(psi);
            }

            _cloudStatus.Text = "Viewer opened.";
            _cloudStatus.Foreground = Good;
        }
        catch (Exception ex)
        {
            _cloudStatus.Text = $"Could not open viewer: {ex.Message}";
            _cloudStatus.Foreground = Bad;
        }

        await Task.CompletedTask;
    }

    private static async Task<(bool Success, string Message)> RunFtpTestAsync(
        string host,
        string username,
        string password,
        string remotePath)
    {
        try
        {
            var cleanHost = host.Trim().TrimEnd('/');
            if (cleanHost.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
                cleanHost = cleanHost[6..];

            var cleanPath = "/" + remotePath.Trim().Trim('/');
            var url = $"ftp://{cleanHost}{cleanPath}/";

            var psi = new ProcessStartInfo("curl")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            psi.ArgumentList.Add("--silent");
            psi.ArgumentList.Add("--show-error");
            psi.ArgumentList.Add("--fail");
            psi.ArgumentList.Add("--connect-timeout");
            psi.ArgumentList.Add("12");
            psi.ArgumentList.Add("--max-time");
            psi.ArgumentList.Add("20");
            psi.ArgumentList.Add("--list-only");
            psi.ArgumentList.Add("--config");
            psi.ArgumentList.Add("-");
            psi.ArgumentList.Add(url);

            using var process = Process.Start(psi);
            if (process is null)
                return (false, "Could not start curl.");

            static string CurlEscape(string value) =>
                value.Replace("\\", "\\\\").Replace("\"", "\\\"");

            // Credentials are supplied through curl's stdin config instead of
            // being placed directly on the process command line.
            await process.StandardInput.WriteLineAsync(
                $"user = \"{CurlEscape(username + ":" + password)}\"");
            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var output = await stdoutTask;
            var error = await stderrTask;

            if (process.ExitCode == 0)
                return (true, string.IsNullOrWhiteSpace(output) ? "Connected." : output.Trim());

            var message = string.IsNullOrWhiteSpace(error)
                ? $"curl exited with code {process.ExitCode}"
                : error.Trim();

            if (message.Length > 240)
                message = message[..240] + "…";

            return (false, message);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            var config = ReadConfig() ?? new JsonObject();
            var storageMode = _storageCombo.SelectedIndex switch { 1 => "cloud", 2 => "both", _ => "local" };

            var ftpHost = (_ftpHostBox.Text ?? "").Trim();
            var ftpUsername = (_ftpUsernameBox.Text ?? "").Trim();
            var ftpRemotePath = (_ftpRemotePathBox.Text ?? "").Trim();
            var viewerUrl = (_viewerUrlBox.Text ?? "").Trim();
            var newPassword = _ftpPasswordBox.Text ?? "";

            if (storageMode is "cloud" or "both")
            {
                var hasPassword = !string.IsNullOrWhiteSpace(newPassword) || File.Exists(_ftpPasswordPath);
                if (string.IsNullOrWhiteSpace(ftpHost) ||
                    string.IsNullOrWhiteSpace(ftpUsername) ||
                    string.IsNullOrWhiteSpace(ftpRemotePath) ||
                    !hasPassword)
                {
                    _settingsMessage.Text = "Cloud/Both requires FTP host, username, remote path and password.";
                    _settingsMessage.Foreground = Bad;
                    return;
                }
            }

            config["storageMode"] = storageMode;
            config["recordingPreset"] = _presetCombo.SelectedIndex switch { 0 => "short", 2 => "long", _ => "normal" };

            var recordingDirectory = (_recordingDirectoryBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(recordingDirectory))
                config.Remove("recordingDirectory");
            else
                config["recordingDirectory"] = ExpandHome(recordingDirectory);

            config["ftpHost"] = ftpHost;
            config["ftpUsername"] = ftpUsername;
            config["ftpRemotePath"] = ftpRemotePath;
            config["viewerUrl"] = viewerUrl;

            Directory.CreateDirectory(_dpHome);
            await File.WriteAllTextAsync(
                _configPath,
                config.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            if (!string.IsNullOrWhiteSpace(newPassword))
            {
                await File.WriteAllTextAsync(_ftpPasswordPath, newPassword.Trim());

                try
                {
                    File.SetUnixFileMode(
                        _ftpPasswordPath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch
                {
                    await RunAsync("chmod", "600", _ftpPasswordPath);
                }

                _ftpPasswordBox.Text = "";
            }

            _cloudStatus.Text = File.Exists(_ftpPasswordPath)
                ? "✓ FTP password is saved."
                : "FTP password has not been saved yet.";
            _cloudStatus.Foreground = File.Exists(_ftpPasswordPath) ? Good : Muted;

            _settingsMessage.Text = "Settings saved. Restart the recorder to apply them.";
            _settingsMessage.Foreground = Good;
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            _settingsMessage.Text = $"Could not save settings: {ex.Message}";
            _settingsMessage.Foreground = Bad;
        }
    }

    private async Task RefreshDiagnosticsAsync()
    {
        _diagnosticsList.Children.Clear();
        AddDiagnostic("Linux environment", true, await RunAsync("uname", "-sr"));
        AddDiagnostic("Node.js", !string.IsNullOrWhiteSpace(await RunAsync("bash", "-lc", "command -v node")), (await RunAsync("node", "-v")).Trim());
        AddDiagnostic("FFmpeg", !string.IsNullOrWhiteSpace(await RunAsync("bash", "-lc", "command -v ffmpeg")), (await RunAsync("bash", "-lc", "ffmpeg -version 2>/dev/null | head -1")).Trim());
        AddDiagnostic("curl", !string.IsNullOrWhiteSpace(await RunAsync("bash", "-lc", "command -v curl")), (await RunAsync("curl", "--version")).Split('\n').FirstOrDefault() ?? "");
        AddDiagnostic("Ring token", File.Exists(_tokenPath), File.Exists(_tokenPath) ? "Connected token present" : "Run ./auth.sh");
        AddDiagnostic("DoorPulse config", File.Exists(_configPath), _configPath);
        AddDiagnostic("Recording folder", Directory.Exists(GetRecordingDirectory()), GetRecordingDirectory());

        var cloudConfig = ReadConfig();
        var cloudMode = cloudConfig?["storageMode"]?.GetValue<string>()?.ToLowerInvariant() ?? "local";
        var cloudConfigured =
            !string.IsNullOrWhiteSpace(cloudConfig?["ftpHost"]?.GetValue<string>()) &&
            !string.IsNullOrWhiteSpace(cloudConfig?["ftpUsername"]?.GetValue<string>()) &&
            !string.IsNullOrWhiteSpace(cloudConfig?["ftpRemotePath"]?.GetValue<string>()) &&
            File.Exists(_ftpPasswordPath);

        AddDiagnostic(
            "Cloud storage",
            cloudMode == "local" || cloudConfigured,
            cloudMode == "local"
                ? "Local mode selected"
                : cloudConfigured
                    ? $"{cloudMode.ToUpperInvariant()} mode configured"
                    : $"{cloudMode.ToUpperInvariant()} mode selected but FTP settings are incomplete");

        var service = (await RunAsync("systemctl", "--user", "is-active", "doorpulse-recorder.service")).Trim();
        AddDiagnostic("Automatic recovery", service == "active", service == "active" ? "systemd service active (Restart=always)" : "Service not currently active");
    }

    private void AddDiagnostic(string name, bool ok, string detail)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,220,*"), ColumnSpacing = 12 };
        row.Children.Add(Label(ok ? "✓" : "!", 18, ok ? Good : Warn, true));
        var n = Label(name, 14, Text, true);
        Grid.SetColumn(n, 1);
        row.Children.Add(n);
        var d = Label(string.IsNullOrWhiteSpace(detail) ? "Not available" : detail.Trim(), 12, Muted);
        d.TextWrapping = TextWrapping.Wrap;
        Grid.SetColumn(d, 2);
        row.Children.Add(d);
        _diagnosticsList.Children.Add(Card(row));
    }

    private JsonObject? ReadConfig()
    {
        try
        {
            return File.Exists(_configPath) ? JsonNode.Parse(File.ReadAllText(_configPath)) as JsonObject : null;
        }
        catch { return null; }
    }

    private string GetRecordingDirectory()
    {
        var fromConfig = ReadConfig()?["recordingDirectory"]?.GetValue<string>();

        if (string.IsNullOrWhiteSpace(fromConfig))
            return Path.Combine(_home, "Videos", "DoorPulse");

        return ExpandHome(fromConfig);
    }

    private string ExpandHome(string path)
    {
        var value = (path ?? "").Trim();

        if (value == "~")
            return _home;

        if (value.StartsWith("~/", StringComparison.Ordinal))
            return Path.Combine(_home, value[2..]);

        return value;
    }

    private static async Task<string> RunAsync(string fileName, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in args) psi.ArgumentList.Add(arg);
            using var process = Process.Start(psi);
            if (process is null) return "";
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await stdout;
            var error = await stderr;
            return string.IsNullOrWhiteSpace(output) ? error : output;
        }
        catch (Exception ex) { return ex.Message; }
    }

    private static bool IsWsl()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME")))
            return true;

        try
        {
            return File.Exists("/proc/version") &&
                   File.ReadAllText("/proc/version").Contains("microsoft", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task OpenPathAsync(string path)
    {
        try
        {
            var psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            psi.ArgumentList.Add(path);
            Process.Start(psi);
        }
        catch
        {
            try
            {
                var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                psi.ArgumentList.Add(path);
                Process.Start(psi);
            }
            catch { }
        }
        await Task.CompletedTask;
    }

    private static string ParseFriendlyName(string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name).Replace('_', ' ');
        return stem.Length > 70 ? stem[..70] + "…" : stem;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024 * 1024):0.0} GB";
        if (bytes >= 1024L * 1024) return $"{bytes / (1024d * 1024):0.0} MB";
        return $"{bytes / 1024d:0} KB";
    }

    private static Border MetricCard(string title, TextBlock value, string note, int column)
    {
        var stack = new StackPanel { Spacing = 5 };
        stack.Children.Add(Label(title, 11, Muted, true));
        stack.Children.Add(value);
        stack.Children.Add(Label(note, 11, Muted));
        var card = Card(stack);
        Grid.SetColumn(card, column);
        return card;
    }

    private static Border Field(string title, string note, Control input)
    {
        var s = new StackPanel { Spacing = 5 };
        s.Children.Add(Label(title, 13, Text, true));
        s.Children.Add(Label(note, 11, Muted));
        s.Children.Add(input);
        return new Border { Child = s };
    }

    private static Button NavButton(string text, Action click)
    {
        var b = new Button
        {
            Content = text,
            Background = Brushes.Transparent,
            Foreground = Brush("#DCE9F7"),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(14, 12),
            FontSize = 14,
            CornerRadius = new CornerRadius(10),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        b.Click += (_, _) => click();
        return b;
    }

    private static Button ActionButton(string text, bool primary, Func<Task> click)
    {
        var b = new Button
        {
            Content = text,
            Background = primary ? Accent : Brushes.White,
            Foreground = primary ? Brushes.White : Text,
            BorderBrush = primary ? Accent : Line,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 9),
            FontWeight = FontWeight.SemiBold,
            CornerRadius = new CornerRadius(10),
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center
        };
        b.Click += async (_, _) =>
        {
            b.IsEnabled = false;
            try { await click(); }
            finally { b.IsEnabled = true; }
        };
        return b;
    }

    private static Border Card(Control child) => new()
    {
        Background = Brushes.White,
        BorderBrush = Line,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(16),
        Padding = new Thickness(18),
        Child = child
    };

    private static TextBlock Label(string text, double size, IBrush brush, bool bold = false,
        HorizontalAlignment h = HorizontalAlignment.Left, VerticalAlignment v = VerticalAlignment.Center) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = brush,
        FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        HorizontalAlignment = h,
        VerticalAlignment = v
    };

    private static TextBox LogBox(double height)
    {
        var box = new TextBox
        {
            Height = height,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("monospace"),
            FontSize = 12,
            Background = Brush("#0F172A"),
            Foreground = Brush("#DDE7F0"),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12)
        };

        Avalonia.Controls.ScrollViewer.SetVerticalScrollBarVisibility(
            box,
            Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

        Avalonia.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(
            box,
            Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

        return box;
    }

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
}

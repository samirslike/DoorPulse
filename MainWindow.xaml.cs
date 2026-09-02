using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DoorPulse.Models;
using DoorPulse.Services;

namespace DoorPulse;

public partial class MainWindow : Window
{
    private AppConfig _config;
    private readonly DispatcherTimer _statusTimer;
    private List<LocalVideoItem> _allVideos = new();

    public MainWindow()
    {
        InitializeComponent();

        _config = ConfigService.Load();
        LoadConfigIntoUi();

        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _statusTimer.Start();

        Loaded += async (_, _) =>
        {
            await RefreshStatusAsync();
            RefreshLogs();
        };
    }

    private void ShowPage(UIElement page, string title, string subtitle)
    {
        DashboardPage.Visibility = Visibility.Collapsed;
        VideosPage.Visibility = Visibility.Collapsed;
        SetupPage.Visibility = Visibility.Collapsed;
        LogsPage.Visibility = Visibility.Collapsed;
        DiagnosticsPage.Visibility = Visibility.Collapsed;

        page.Visibility = Visibility.Visible;
        PageTitle.Text = title;
        PageSubtitle.Text = subtitle;
    }

    private void Dashboard_Click(object sender, RoutedEventArgs e) =>
        ShowPage(DashboardPage, "Dashboard", "Monitor your recorder and recent activity.");

    private void Videos_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(VideosPage, "Videos", "Browse and play your locally stored DoorPulse recordings.");
        RefreshVideoLibrary();
    }


    private void Setup_Click(object sender, RoutedEventArgs e) =>
        ShowPage(SetupPage, "Settings", "Review DoorPulse settings or rerun the guided setup.");

    private void Logs_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(LogsPage, "Activity Logs", "Live recorder events, uploads and errors.");
        RefreshLogs();
    }

    private void Diagnostics_Click(object sender, RoutedEventArgs e) =>
        ShowPage(DiagnosticsPage, "Diagnostics", "Check everything DoorPulse needs on this PC.");

    private void LoadConfigIntoUi()
    {
        var cameraNames = _config.Cameras?
            .Where(c => c.Enabled)
            .Select(c => c.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList() ?? new List<string>();

        CameraNameBox.Text = cameraNames.Count > 0
            ? string.Join(", ", cameraNames)
            : _config.CameraName;
        CooldownBox.Text = _config.CooldownSeconds.ToString();
        PollBox.Text = _config.BackupPollSeconds.ToString();
        RetentionBox.Text = _config.RetentionHours.ToString();
        RecordingDirectoryBox.Text = _config.RecordingDirectory;

        FtpHostBox.Text = _config.FtpHost;
        FtpUserBox.Text = _config.FtpUsername;
        FtpRemoteBox.Text = _config.FtpRemotePath;
        ViewerUrlBox.Text = _config.ViewerUrl;

        NodePathBox.Text = _config.NodePath;
        FfmpegPathBox.Text = _config.FfmpegPath;
        ScriptPathBox.Text = _config.RecorderScriptPath;

        foreach (ComboBoxItem item in PresetBox.Items)
        {
            if (string.Equals(item.Content?.ToString(), _config.RecordingPreset, StringComparison.OrdinalIgnoreCase))
            {
                PresetBox.SelectedItem = item;
                break;
            }
        }

        PresetBox.SelectedIndex = PresetBox.SelectedIndex < 0 ? 1 : PresetBox.SelectedIndex;

        SavedTokenNote.Text = File.Exists(ConfigService.RingTokenPath)
            ? "✓ Ring token is already saved on this PC."
            : "No Ring token saved yet.";
    }

    private AppConfig ReadUiIntoConfig()
    {
        var preset = (PresetBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "normal";

        // Update the existing configuration rather than creating a new object.
        // This preserves selected cameras, onboarding state and future settings.
        _config.RecordingPreset = preset;
        _config.CooldownSeconds = ParseInt(CooldownBox.Text, 15);
        _config.BackupPollSeconds = ParseInt(PollBox.Text, 60);
        _config.RetentionHours = ParseInt(RetentionBox.Text, 24);
        _config.ThumbnailSecond = 1;
        _config.RecordingDirectory = RecordingDirectoryBox.Text.Trim();

        _config.FtpHost = FtpHostBox.Text.Trim();
        _config.FtpUsername = FtpUserBox.Text.Trim();
        _config.FtpRemotePath = FtpRemoteBox.Text.Trim();
        _config.ViewerUrl = ViewerUrlBox.Text.Trim();

        _config.NodePath = NodePathBox.Text.Trim();
        _config.FfmpegPath = FfmpegPathBox.Text.Trim();
        _config.RecorderScriptPath = ScriptPathBox.Text.Trim();
        _config.AutoStart = true;

        return _config;
    }

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text, out var n) ? n : fallback;

    private async Task RefreshStatusAsync()
    {
        var status = await TaskService.GetStatusAsync();

        SidebarStatus.Text = status;
        TopStatusText.Text = status;
        DashRecorder.Text = status;

        if (status.Equals("Running", StringComparison.OrdinalIgnoreCase))
        {
            TopStatusPill.Background = new SolidColorBrush(Color.FromRgb(231, 247, 237));
            TopStatusText.Foreground = new SolidColorBrush(Color.FromRgb(21, 128, 61));
        }
        else
        {
            TopStatusPill.Background = new SolidColorBrush(Color.FromRgb(238, 242, 246));
            TopStatusText.Foreground = new SolidColorBrush(Color.FromRgb(71, 84, 103));
        }

        var monitoredCameraCount = _config.Cameras?
            .Count(c => c.Enabled) ?? 0;

        if (monitoredCameraCount > 0)
        {
            DashRing.Text = monitoredCameraCount == 1
                ? "1 monitored"
                : $"{monitoredCameraCount} monitored";
        }
        else
        {
            DashRing.Text = File.Exists(ConfigService.RingTokenPath)
                ? "Connected"
                : "Not connected";
        }

        var seconds = _config.RecordingPreset.ToLowerInvariant() switch
        {
            "short" => 15,
            "long" => 45,
            _ => 25
        };

        DashLength.Text = $"{seconds} sec";
        DashFtp.Text = string.IsNullOrWhiteSpace(_config.FtpHost) ? "Not configured" : _config.FtpHost;
    }

    private void RefreshLogs()
    {
        var text = LogService.Tail(180);
        DashboardLog.Text = text;
        FullLog.Text = text;

        DashboardLog.ScrollToEnd();
        FullLog.ScrollToEnd();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await TaskService.ExistsAsync())
            {
                MessageBox.Show(
                    "Install the DoorPulse startup task first from Setup.",
                    "DoorPulse",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            await TaskService.StartAsync();
            await Task.Delay(800);
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await TaskService.StopAsync();
            await Task.Delay(700);
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await TaskService.RestartAsync();
            await Task.Delay(900);
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void SaveApply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _config = ReadUiIntoConfig();
            ConfigService.Save(_config);

            if (!string.IsNullOrWhiteSpace(RingTokenBox.Password))
            {
                await SecretService.SaveRingTokenAsync(RingTokenBox.Password);
                RingTokenBox.Clear();
            }

            if (!string.IsNullOrWhiteSpace(FtpPasswordBox.Password))
            {
                await SecretService.SaveFtpPasswordAsync(FtpPasswordBox.Password);
                FtpPasswordBox.Clear();
            }

            EngineDeployer.Deploy(_config.RecorderScriptPath);

            if (await TaskService.ExistsAsync())
                await TaskService.RestartAsync();

            SavedTokenNote.Text = File.Exists(ConfigService.RingTokenPath)
                ? "✓ Ring token is saved on this PC."
                : "No Ring token saved yet.";

            MessageBox.Show(
                "DoorPulse settings saved and recorder engine deployed.",
                "DoorPulse",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void ImportExisting_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            const string oldToken = @"C:\RingRecorder\refresh-token.txt";
            const string oldFtp = @"C:\RingRecorder\ftp-config.txt";

            if (File.Exists(oldToken))
            {
                await SecretService.SaveRingTokenAsync(File.ReadAllText(oldToken).Trim());
            }

            if (File.Exists(oldFtp))
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var raw in File.ReadAllLines(oldFtp))
                {
                    var line = raw.Trim();
                    var pos = line.IndexOf('=');

                    if (pos > 0)
                        values[line[..pos].Trim()] = line[(pos + 1)..].Trim();
                }

                if (values.TryGetValue("host", out var host))
                    _config.FtpHost = host;

                if (values.TryGetValue("username", out var user))
                    _config.FtpUsername = user;

                if (values.TryGetValue("remote", out var remote))
                    _config.FtpRemotePath = remote;

                if (values.TryGetValue("password", out var password))
                    await SecretService.SaveFtpPasswordAsync(password);
            }

            if (File.Exists(@"C:\Program Files\nodejs\node.exe"))
                _config.NodePath = @"C:\Program Files\nodejs\node.exe";

            if (File.Exists(@"C:\ffmpeg\bin\ffmpeg.exe"))
                _config.FfmpegPath = @"C:\ffmpeg\bin\ffmpeg.exe";

            _config.RecorderScriptPath = @"C:\RingRecorder\recorder.mjs";
            _config.RecordingDirectory = @"C:\RingRecordings";

            ConfigService.Save(_config);
            EngineDeployer.Deploy(_config.RecorderScriptPath);

            LoadConfigIntoUi();

            MessageBox.Show(
                "Existing Ring recorder settings were imported into DoorPulse.",
                "DoorPulse",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void InstallStartup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var exePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Could not determine DoorPulse executable path.");

            var answer = MessageBox.Show(
                "DoorPulse will install an automatic startup task running as SYSTEM.\n\n" +
                "If the old 'Ring Camera Recorder' task exists, DoorPulse will stop and disable it to prevent duplicate recordings.\n\nContinue?",
                "Install DoorPulse Startup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
                return;

            await TaskService.InstallOrUpdateAsync(exePath);
            await TaskService.StartAsync();

            MessageBox.Show(
                "DoorPulse startup task installed. It will run automatically after Windows restarts.",
                "DoorPulse",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void RefreshLog_Click(object sender, RoutedEventArgs e) => RefreshLogs();

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        ConfigService.EnsureFolders();

        Process.Start(new ProcessStartInfo
        {
            FileName = ConfigService.LogsPath,
            UseShellExecute = true
        });
    }

    private void OpenViewer_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_config.ViewerUrl))
        {
            MessageBox.Show("Set the mobile viewer URL in Setup first.", "DoorPulse");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _config.ViewerUrl,
            UseShellExecute = true
        });
    }

    private async void RunDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        DiagnosticsList.Children.Clear();

        var items = await DiagnosticsService.RunAsync(_config);

        foreach (var item in items)
        {
            var border = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(229, 234, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 9)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var badge = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(
                    item.Ok ? Color.FromRgb(231, 247, 237) : Color.FromRgb(254, 235, 235)),
                Child = new TextBlock
                {
                    Text = item.Ok ? "✓" : "!",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(
                        item.Ok ? Color.FromRgb(21, 128, 61) : Color.FromRgb(185, 28, 28)),
                    FontWeight = FontWeights.Bold
                }
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = item.Name,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(23, 32, 51))
            });
            stack.Children.Add(new TextBlock
            {
                Text = item.Detail,
                Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128)),
                FontSize = 11,
                Margin = new Thickness(0, 3, 0, 0)
            });

            Grid.SetColumn(badge, 0);
            Grid.SetColumn(stack, 1);

            grid.Children.Add(badge);
            grid.Children.Add(stack);
            border.Child = grid;

            DiagnosticsList.Children.Add(border);
        }
    }


    private void RunSetupWizard_Click(object sender, RoutedEventArgs e)
    {
        _config.SetupCompleted = false;
        _config.SetupStep = 0;
        ConfigService.Save(_config);

        var wizard = new SetupWizardWindow();
        Application.Current.MainWindow = wizard;
        wizard.Show();
        Close();
    }


    private void RefreshVideoLibrary()
    {
        var storage = (_config.StorageMode ?? "cloud").ToLowerInvariant();
        var localEnabled = storage == "local" || storage == "both";

        CloudOnlyVideosPanel.Visibility = localEnabled
            ? Visibility.Collapsed
            : Visibility.Visible;

        VideoScrollViewer.Visibility = localEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!localEnabled)
        {
            EmptyVideosPanel.Visibility = Visibility.Collapsed;
            VideoLibrarySummary.Text = "Cloud-only storage is enabled.";
            VideoItemsControl.ItemsSource = null;
            return;
        }

        _allVideos = VideoLibraryService.Load(_config.RecordingDirectory);

        var cameras = _allVideos
            .Select(v => v.CameraName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

        var currentCamera = (VideoCameraFilter.SelectedItem as ComboBoxItem)?.Content?.ToString()
                            ?? VideoCameraFilter.SelectedValue?.ToString()
                            ?? "All cameras";

        VideoCameraFilter.Items.Clear();
        VideoCameraFilter.Items.Add(new ComboBoxItem { Content = "All cameras" });

        foreach (var camera in cameras)
            VideoCameraFilter.Items.Add(new ComboBoxItem { Content = camera });

        var matchIndex = 0;
        for (var i = 0; i < VideoCameraFilter.Items.Count; i++)
        {
            if ((VideoCameraFilter.Items[i] as ComboBoxItem)?.Content?.ToString()
                ?.Equals(currentCamera, StringComparison.OrdinalIgnoreCase) == true)
            {
                matchIndex = i;
                break;
            }
        }

        VideoCameraFilter.SelectedIndex = matchIndex;

        if (VideoEventFilter.SelectedIndex < 0)
            VideoEventFilter.SelectedIndex = 0;

        ApplyVideoFilters();
    }

    private void ApplyVideoFilters()
    {
        if (VideosPage.Visibility != Visibility.Visible)
            return;

        var filtered = _allVideos.AsEnumerable();

        var camera = (VideoCameraFilter.SelectedItem as ComboBoxItem)?.Content?.ToString()
                     ?? "All cameras";

        if (!camera.Equals("All cameras", StringComparison.OrdinalIgnoreCase))
            filtered = filtered.Where(v =>
                v.CameraName.Equals(camera, StringComparison.OrdinalIgnoreCase));

        var eventFilter = (VideoEventFilter.SelectedItem as ComboBoxItem)?.Content?.ToString()
                          ?? "All events";

        if (eventFilter.Equals("Motion", StringComparison.OrdinalIgnoreCase))
            filtered = filtered.Where(v => v.EventType == "motion");
        else if (eventFilter.Equals("Doorbell", StringComparison.OrdinalIgnoreCase))
            filtered = filtered.Where(v => v.EventType == "doorbell");

        var search = VideoSearchBox.Text.Trim();

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(v =>
                v.CameraName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                v.FileName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                v.EventLabel.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var list = filtered.ToList();

        VideoItemsControl.ItemsSource = list;

        EmptyVideosPanel.Visibility = list.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        VideoScrollViewer.Visibility = list.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var totalBytes = _allVideos.Sum(v => v.SizeBytes);
        var totalMb = totalBytes / 1024d / 1024d;
        var sizeText = totalMb >= 1024
            ? $"{totalMb / 1024d:0.0} GB"
            : $"{totalMb:0} MB";

        VideoLibrarySummary.Text =
            $"{_allVideos.Count} local recording{(_allVideos.Count == 1 ? "" : "s")} • {sizeText} • {_config.RecordingDirectory}";
    }

    private void VideoFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            ApplyVideoFilters();
    }

    private void VideoSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded)
            ApplyVideoFilters();
    }

    private void RefreshVideos_Click(object sender, RoutedEventArgs e) =>
        RefreshVideoLibrary();

    private void OpenVideoFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_config.RecordingDirectory))
            Directory.CreateDirectory(_config.RecordingDirectory);

        Process.Start(new ProcessStartInfo
        {
            FileName = _config.RecordingDirectory,
            UseShellExecute = true
        });
    }

    private void VideoCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border border &&
            border.DataContext is LocalVideoItem video)
        {
            OpenLocalVideo(video);
        }
    }

    private void PlayVideoButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is Button button &&
            button.Tag is LocalVideoItem video)
        {
            OpenLocalVideo(video);
        }
    }

    private void OpenLocalVideo(LocalVideoItem video)
    {
        if (!File.Exists(video.VideoPath))
        {
            MessageBox.Show(
                "This video is no longer available on the local PC.",
                "DoorPulse",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            RefreshVideoLibrary();
            return;
        }

        var player = new VideoPlayerWindow(video)
        {
            Owner = this
        };

        player.ShowDialog();
    }

    private void DeleteVideoButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is not Button button ||
            button.Tag is not LocalVideoItem video)
            return;

        var answer = MessageBox.Show(
            $"Delete this local recording?\n\n{video.CameraName}\n{video.EventTime:G}\n\n" +
            "If this recording was also uploaded to the cloud, the cloud copy is not deleted.",
            "Delete Local Video",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            VideoLibraryService.Delete(video);
            RefreshVideoLibrary();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "DoorPulse",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void ShowError(Exception ex)
    {
        MessageBox.Show(
            ex.Message,
            "DoorPulse",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}

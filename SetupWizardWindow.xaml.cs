using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DoorPulse.Models;
using DoorPulse.Services;

namespace DoorPulse;

public partial class SetupWizardWindow : Window
{
    private readonly AppConfig _config;
    private int _step;
    private RuntimeStatus? _runtime;
    private RingAuthSession? _ringAuth;
    private bool _ringConnected;
    private bool _ringPushReady;
    private bool _ringPushPreparing;
    private bool _ftpTested;
    private readonly List<RingCameraChoice> _cameraChoices = new();

    private readonly UIElement[] _steps;

    public SetupWizardWindow()
    {
        InitializeComponent();

        _config = ConfigService.Load();
        _step = Math.Clamp(_config.SetupStep, 0, 6);

        _steps = new UIElement[]
        {
            WelcomeStep,
            RuntimeStep,
            RingStep,
            CameraStep,
            RecordingStep,
            StorageStep,
            FinishStep
        };

        RingEmailBox.Text = _config.RingEmail;
        FtpHostBox.Text = _config.FtpHost;
        FtpUserBox.Text = _config.FtpUsername;
        FtpRemoteBox.Text = _config.FtpRemotePath;
        LocalFolderBox.Text = string.IsNullOrWhiteSpace(_config.RecordingDirectory)
            ? ConfigService.DefaultRecordingPath
            : _config.RecordingDirectory;

        RetentionBox.SelectedIndex = _config.RetentionHours switch
        {
            72 => 1,
            168 => 2,
            720 => 3,
            _ => 0
        };

        if (_config.StorageMode.Equals("local", StringComparison.OrdinalIgnoreCase))
            LocalStorageChoice.IsChecked = true;
        else if (_config.StorageMode.Equals("both", StringComparison.OrdinalIgnoreCase))
            BothStorageChoice.IsChecked = true;
        else
            CloudStorageChoice.IsChecked = true;

        switch (_config.RecordingPreset.ToLowerInvariant())
        {
            case "short": PresetShort.IsChecked = true; break;
            case "long": PresetLong.IsChecked = true; break;
            default: PresetNormal.IsChecked = true; break;
        }

        _ringConnected = File.Exists(ConfigService.RingTokenPath);
        if (_ringConnected)
        {
            RingConnectedPanel.Visibility = Visibility.Visible;
            RingCredentialsPanel.Visibility = Visibility.Collapsed;
            RingPushPanel.Visibility = Visibility.Visible;
        }

        Loaded += async (_, _) =>
        {
            ShowStep(_step);

            if (_step == 1)
                await AutoPrepareRuntimeAsync();

            if (_step == 2 && _ringConnected)
                await EnsureRingPushReadyAsync();

            if (_step == 3)
                await LoadCamerasAsync();
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        _ringAuth?.Dispose();
        base.OnClosed(e);
    }

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, _steps.Length - 1);

        for (var i = 0; i < _steps.Length; i++)
            _steps[i].Visibility = i == _step ? Visibility.Visible : Visibility.Collapsed;

        StepLabel.Text = $"Step {_step + 1} of {_steps.Length}";
        BackButton.Visibility = _step == 0 ? Visibility.Hidden : Visibility.Visible;
        NextButton.Visibility = _step == _steps.Length - 1 ? Visibility.Collapsed : Visibility.Visible;
        FinishButton.Visibility = _step == _steps.Length - 1 ? Visibility.Visible : Visibility.Collapsed;

        HeaderSubtitle.Text = _step switch
        {
            0 => "Let's get your recorder ready.",
            1 => "Preparing system components.",
            2 => "Securely connect your Ring account.",
            3 => "Select the camera DoorPulse should monitor.",
            4 => "Choose your event recording length.",
            5 => "Choose local or cloud storage.",
            _ => "Review and start DoorPulse."
        };

        _config.SetupStep = _step;
        SaveWizardFields();
        ConfigService.Save(_config);

        if (_step == 6)
            UpdateFinishSummary();
    }

    private void SaveWizardFields()
    {
        _config.RingEmail = RingEmailBox.Text.Trim();

        _config.RecordingPreset =
            PresetShort.IsChecked == true ? "short" :
            PresetLong.IsChecked == true ? "long" :
            "normal";

        _config.StorageMode =
            BothStorageChoice.IsChecked == true ? "both" :
            LocalStorageChoice.IsChecked == true ? "local" :
            "cloud";

        if (_config.StorageMode == "local" || _config.StorageMode == "both")
        {
            _config.RecordingDirectory = string.IsNullOrWhiteSpace(LocalFolderBox.Text)
                ? ConfigService.DefaultRecordingPath
                : LocalFolderBox.Text.Trim();

            if (RetentionBox.SelectedItem is ComboBoxItem retentionItem &&
                int.TryParse(retentionItem.Tag?.ToString(), out var hours))
                _config.RetentionHours = hours;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(_config.RecordingDirectory))
                _config.RecordingDirectory = ConfigService.DefaultRecordingPath;

            _config.RetentionHours = 24; // cloud-only local fallback safety
        }

        if (_config.StorageMode == "cloud" || _config.StorageMode == "both")
        {
            _config.FtpHost = FtpHostBox.Text.Trim();
            _config.FtpUsername = FtpUserBox.Text.Trim();
            _config.FtpRemotePath = FtpRemoteBox.Text.Trim();
        }
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await ValidateCurrentStepAsync())
                return;

            SaveWizardFields();
            ConfigService.Save(_config);
            ShowStep(_step + 1);

            if (_step == 1)
                await AutoPrepareRuntimeAsync();

            if (_step == 2 && _ringConnected)
                await EnsureRingPushReadyAsync();

            if (_step == 3)
                await LoadCamerasAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "DoorPulse Setup", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) =>
        ShowStep(_step - 1);

    private async Task<bool> ValidateCurrentStepAsync()
    {
        switch (_step)
        {
            case 1:
                _runtime = RuntimeService.Detect(_config.RecorderScriptPath);
                if (!_runtime.AllReady)
                {
                    MessageBox.Show(
                        "DoorPulse still needs one or more system components. Click Install Required Components first.",
                        "DoorPulse Setup",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return false;
                }
                break;

            case 2:
                if (!_ringConnected)
                {
                    MessageBox.Show(
                        "Connect your Ring account before continuing.",
                        "DoorPulse Setup");
                    return false;
                }

                if (!_ringPushReady)
                {
                    await EnsureRingPushReadyAsync();

                    if (!_ringPushReady)
                    {
                        MessageBox.Show(
                            "DoorPulse still needs to finish Ring Push registration. Click Retry and wait for the green Ready status before continuing.",
                            "DoorPulse Setup",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return false;
                    }
                }
                break;

            case 3:
                var selected = _cameraChoices
                    .Where(c => c.IsSelected)
                    .ToList();

                if (selected.Count == 0)
                {
                    MessageBox.Show(
                        "Choose at least one camera before continuing.",
                        "DoorPulse Setup",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return false;
                }

                if (selected.Any(c => !c.MonitorMotion && !c.MonitorDoorbell))
                {
                    MessageBox.Show(
                        "Each selected camera must monitor Motion, Doorbell press, or both.",
                        "DoorPulse Setup",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return false;
                }

                _config.Cameras = selected
                    .Select(c => new CameraSelection
                    {
                        Id = c.Id,
                        Name = c.Name,
                        Enabled = true,
                        MonitorMotion = c.MonitorMotion,
                        MonitorDoorbell = c.MonitorDoorbell
                    })
                    .ToList();

                // Legacy fields retain the first selected camera for compatibility.
                _config.CameraId = _config.Cameras[0].Id;
                _config.CameraName = _config.Cameras[0].Name;
                break;

            case 5:
                if (CloudStorageChoice.IsChecked == true || BothStorageChoice.IsChecked == true)
                {
                    if (string.IsNullOrWhiteSpace(FtpHostBox.Text) ||
                        string.IsNullOrWhiteSpace(FtpUserBox.Text) ||
                        string.IsNullOrWhiteSpace(FtpRemoteBox.Text))
                    {
                        MessageBox.Show("Enter the FTP host, username and remote folder.", "DoorPulse Setup");
                        return false;
                    }

                    if (!_ftpTested && string.IsNullOrWhiteSpace(SecretService.ReadFtpPassword()))
                    {
                        MessageBox.Show("Enter the FTP password and test the connection.", "DoorPulse Setup");
                        return false;
                    }
                }
                break;
        }

        await Task.CompletedTask;
        return true;
    }

    private async Task RefreshRuntimeAsync()
    {
        _runtime = RuntimeService.Detect(_config.RecorderScriptPath);

        NodeStatus.Text = (_runtime.NodeReady ? "✓" : "○") +
                          " Node.js runtime — " + (_runtime.NodeReady ? "Ready" : "Preparing");

        FfmpegStatus.Text = (_runtime.FfmpegReady ? "✓" : "○") +
                            " FFmpeg — " + (_runtime.FfmpegReady ? "Ready" : "Preparing");

        RingRuntimeStatus.Text = (_runtime.RingRuntimeReady ? "✓" : "○") +
                                 " Ring connection engine — " + (_runtime.RingRuntimeReady ? "Ready" : "Preparing");

        RuntimeMessage.Text = _runtime.AllReady
            ? "Everything DoorPulse needs is ready."
            : "DoorPulse is preparing its built-in components.";

        InstallComponentsButton.Visibility = _runtime.AllReady
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (_runtime.NodeReady)
            _config.NodePath = _runtime.NodePath;

        if (_runtime.FfmpegReady)
            _config.FfmpegPath = _runtime.FfmpegPath;

        ConfigService.Save(_config);
        await Task.CompletedTask;
    }

    private async Task AutoPrepareRuntimeAsync()
    {
        InstallComponentsButton.Visibility = Visibility.Collapsed;
        NextButton.IsEnabled = false;

        try
        {
            var progress = new Progress<string>(text =>
            {
                RuntimeMessage.Text = text;
            });

            _runtime = await RuntimeService.EnsureAsync(_config, progress);
            ConfigService.Save(_config);

            await RefreshRuntimeAsync();
            NextButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            RuntimeMessage.Text = ex.Message;
            InstallComponentsButton.Visibility = Visibility.Visible;
            InstallComponentsButton.IsEnabled = true;
            NextButton.IsEnabled = false;
        }
    }

    private async void InstallComponents_Click(object sender, RoutedEventArgs e)
    {
        await AutoPrepareRuntimeAsync();
    }

    private string GetEngineDirectory()
    {
        var managed = ConfigService.ManagedEnginePath;

        if (Directory.Exists(
            Path.Combine(managed, "node_modules", "ring-client-api")))
        {
            _config.RecorderScriptPath = ConfigService.DefaultRecorderPath;
            return managed;
        }

        var current = Path.GetDirectoryName(_config.RecorderScriptPath);

        if (!string.IsNullOrWhiteSpace(current) &&
            Directory.Exists(Path.Combine(current, "node_modules", "ring-client-api")))
            return current;

        if (Directory.Exists(@"C:\RingRecorder\node_modules\ring-client-api"))
        {
            _config.RecorderScriptPath = @"C:\RingRecorder\recorder.mjs";
            return @"C:\RingRecorder";
        }

        return managed;
    }

    private async void ConnectRing_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RingEmailBox.Text) ||
            string.IsNullOrWhiteSpace(RingPasswordBox.Password))
        {
            RingLoginMessage.Text = "Enter your Ring email and password.";
            return;
        }

        try
        {
            RingLoginMessage.Text = "Connecting to Ring...";

            var runtime = RuntimeService.Detect(_config.RecorderScriptPath);
            if (!runtime.NodeReady || !runtime.RingRuntimeReady)
                throw new InvalidOperationException("System components are not ready. Go back one step.");

            var engineDir = GetEngineDirectory();
            EngineDeployer.DeployAuthHelpers(engineDir);

            var helper = Path.Combine(engineDir, "ring-auth-helper.mjs");

            _ringAuth?.Dispose();
            var started = await RingAuthSession.StartAsync(
                runtime.NodePath,
                helper,
                RingEmailBox.Text.Trim(),
                RingPasswordBox.Password);

            _ringAuth = started.Session;
            await HandleAuthReplyAsync(started.Reply);
        }
        catch (Exception ex)
        {
            RingLoginMessage.Text = ex.Message;
        }
    }

    private async void Verify2fa_Click(object sender, RoutedEventArgs e)
    {
        if (_ringAuth is null)
        {
            RingLoginMessage.Text = "Start the Ring connection again.";
            return;
        }

        if (string.IsNullOrWhiteSpace(TwoFactorBox.Text))
            return;

        try
        {
            var reply = await _ringAuth.SubmitCodeAsync(TwoFactorBox.Text.Trim());
            await HandleAuthReplyAsync(reply);
        }
        catch (Exception ex)
        {
            RingLoginMessage.Text = ex.Message;
        }
    }

    private async Task HandleAuthReplyAsync(RingAuthReply reply)
    {
        if (reply.Type == "need2fa")
        {
            TwoFactorPanel.Visibility = Visibility.Visible;
            TwoFactorPrompt.Text = reply.Message;
            RingLoginMessage.Text = "Ring sent a verification challenge.";
            return;
        }

        if (reply.Type == "success")
        {
            await SecretService.SaveRingTokenAsync(reply.Token);

            _config.RingEmail = RingEmailBox.Text.Trim();
            ConfigService.Save(_config);

            RingPasswordBox.Clear();
            TwoFactorBox.Clear();
            TwoFactorPanel.Visibility = Visibility.Collapsed;
            RingCredentialsPanel.Visibility = Visibility.Collapsed;
            RingConnectedPanel.Visibility = Visibility.Visible;
            RingPushPanel.Visibility = Visibility.Visible;
            RingLoginMessage.Text = "";
            _ringConnected = true;
            _ringPushReady = false;

            _ringAuth?.Dispose();
            _ringAuth = null;

            await EnsureRingPushReadyAsync();
            return;
        }

        RingLoginMessage.Text = string.IsNullOrWhiteSpace(reply.Message)
            ? "Ring login failed."
            : reply.Message;
    }

    private async Task EnsureRingPushReadyAsync()
    {
        if (_ringPushPreparing || !_ringConnected)
            return;

        _ringPushPreparing = true;
        RingPushPanel.Visibility = Visibility.Visible;
        RetryPushButton.Visibility = Visibility.Collapsed;

        try
        {
            var runtime =
                RuntimeService.Detect(
                    _config.RecorderScriptPath);

            if (!runtime.NodeReady ||
                !runtime.RingRuntimeReady)
            {
                _ringPushReady = false;
                RingPushStatusText.Text =
                    "System components are not ready.";
                RetryPushButton.Visibility =
                    Visibility.Visible;
                return;
            }

            var engineDir = GetEngineDirectory();
            EngineDeployer.DeployAuthHelpers(engineDir);

            // If the saved token already has the persistent push credentials,
            // do not register again.
            var existing =
                await PushStatusService.CheckAsync(
                    runtime.NodePath,
                    engineDir);

            if (existing.Ready)
            {
                SetRingPushReady(
                    "✓ Ready for instant motion and doorbell events.");
                return;
            }

            RingPushStatusText.Text =
                "Registering this PC for instant Ring notifications...";

            var result =
                await PushBootstrapService.RunAsync(
                    runtime.NodePath,
                    engineDir,
                    25);

            if (result.Ready)
            {
                SetRingPushReady(
                    "✓ Ready for instant motion and doorbell events.");
                return;
            }

            _ringPushReady = false;
            RingPushStatusText.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(185, 28, 28));

            RingPushStatusText.Text =
                string.IsNullOrWhiteSpace(result.Message)
                    ? "Ring Push registration needs another attempt."
                    : result.Message;

            RetryPushButton.Visibility =
                Visibility.Visible;
        }
        catch (Exception ex)
        {
            _ringPushReady = false;

            RingPushStatusText.Foreground =
                new SolidColorBrush(
                    Color.FromRgb(185, 28, 28));

            RingPushStatusText.Text =
                ex.Message;

            RetryPushButton.Visibility =
                Visibility.Visible;
        }
        finally
        {
            _ringPushPreparing = false;
        }
    }

    private void SetRingPushReady(string message)
    {
        _ringPushReady = true;

        RingPushStatusText.Foreground =
            new SolidColorBrush(
                Color.FromRgb(22, 128, 59));

        RingPushStatusText.Text = message;

        RetryPushButton.Visibility =
            Visibility.Collapsed;
    }

    private async void RetryPush_Click(
        object sender,
        RoutedEventArgs e)
    {
        RetryPushButton.IsEnabled = false;

        try
        {
            await EnsureRingPushReadyAsync();
        }
        finally
        {
            RetryPushButton.IsEnabled = true;
        }
    }

    private async Task LoadCamerasAsync()
    {
        if (!_ringConnected || !File.Exists(ConfigService.RingTokenPath))
            return;

        try
        {
            CameraMessage.Text = "Loading cameras...";

            var runtime = RuntimeService.Detect(_config.RecorderScriptPath);
            var engineDir = GetEngineDirectory();
            EngineDeployer.DeployAuthHelpers(engineDir);

            var helper = Path.Combine(engineDir, "ring-cameras-helper.mjs");

            var cameras = await RingCameraService.GetCamerasAsync(
                runtime.NodePath,
                helper,
                ConfigService.RingTokenPath);

            var previous = _cameraChoices.ToDictionary(
                c => c.Id,
                c => c,
                StringComparer.OrdinalIgnoreCase);

            _cameraChoices.Clear();

            foreach (var camera in cameras)
            {
                var saved = _config.Cameras?
                    .FirstOrDefault(c => string.Equals(c.Id, camera.Id, StringComparison.OrdinalIgnoreCase));

                var oldChoice = previous.TryGetValue(camera.Id, out var existing)
                    ? existing
                    : null;

                var isLegacyCamera =
                    (_config.Cameras is null || _config.Cameras.Count == 0) &&
                    (
                        string.Equals(_config.CameraId, camera.Id, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(_config.CameraName, camera.Name, StringComparison.OrdinalIgnoreCase)
                    );

                _cameraChoices.Add(new RingCameraChoice
                {
                    Id = camera.Id,
                    Name = camera.Name,
                    IsSelected = saved?.Enabled ??
                                 oldChoice?.IsSelected ??
                                 (isLegacyCamera || cameras.Count == 1),
                    MonitorMotion = saved?.MonitorMotion ??
                                    oldChoice?.MonitorMotion ??
                                    true,
                    MonitorDoorbell = saved?.MonitorDoorbell ??
                                      oldChoice?.MonitorDoorbell ??
                                      true
                });
            }

            // Brand-new account with multiple cameras: select all initially.
            // Customer can uncheck anything they do not want monitored.
            if ((_config.Cameras is null || _config.Cameras.Count == 0) &&
                string.IsNullOrWhiteSpace(_config.CameraId) &&
                string.IsNullOrWhiteSpace(_config.CameraName) &&
                _cameraChoices.Count > 1)
            {
                foreach (var choice in _cameraChoices)
                    choice.IsSelected = true;
            }

            CameraList.ItemsSource = null;
            CameraList.ItemsSource = _cameraChoices;

            UpdateCameraCount();

            CameraMessage.Text = cameras.Count == 0
                ? "No compatible cameras were found."
                : $"{cameras.Count} camera(s) found on this Ring account.";
        }
        catch (Exception ex)
        {
            CameraMessage.Text = ex.Message;
        }
    }

    private void UpdateCameraCount()
    {
        var selected = _cameraChoices.Count(c => c.IsSelected);
        CameraCountText.Text = $"{selected} selected";
    }

    private void CameraSelection_Changed(object sender, RoutedEventArgs e)
    {
        UpdateCameraCount();

        // Re-render so Motion / Doorbell controls immediately enable/disable
        // when the parent camera checkbox changes.
        CameraList.Items.Refresh();
    }

    private void SelectAllCameras_Click(object sender, RoutedEventArgs e)
    {
        foreach (var camera in _cameraChoices)
            camera.IsSelected = true;

        CameraList.Items.Refresh();
        UpdateCameraCount();
    }

    private void ClearAllCameras_Click(object sender, RoutedEventArgs e)
    {
        foreach (var camera in _cameraChoices)
            camera.IsSelected = false;

        CameraList.Items.Refresh();
        UpdateCameraCount();
    }

    private async void RefreshCameras_Click(object sender, RoutedEventArgs e) =>
        await LoadCamerasAsync();

    private void StorageChoice_Changed(object sender, RoutedEventArgs e)
    {
        if (CloudPanel is null || LocalPanel is null)
            return;

        var cloud =
            CloudStorageChoice.IsChecked == true ||
            BothStorageChoice.IsChecked == true;

        var local =
            LocalStorageChoice.IsChecked == true ||
            BothStorageChoice.IsChecked == true;

        CloudPanel.Visibility = cloud
            ? Visibility.Visible
            : Visibility.Collapsed;

        LocalPanel.Visibility = local
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void TestFtp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var password = !string.IsNullOrWhiteSpace(FtpPasswordBox.Password)
                ? FtpPasswordBox.Password
                : SecretService.ReadFtpPassword();

            if (string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Enter the FTP password.");

            FtpTestMessage.Text = "Testing...";

            await FtpService.TestAsync(
                FtpHostBox.Text.Trim(),
                FtpUserBox.Text.Trim(),
                password,
                FtpRemoteBox.Text.Trim());

            await SecretService.SaveFtpPasswordAsync(password);
            FtpPasswordBox.Clear();

            _ftpTested = true;
            FtpTestMessage.Text = "✓ Connected successfully";
            FtpTestMessage.Foreground = new SolidColorBrush(Color.FromRgb(22, 128, 59));
        }
        catch (Exception ex)
        {
            _ftpTested = false;
            FtpTestMessage.Text = ex.Message;
            FtpTestMessage.Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
        }
    }

    private void UpdateFinishSummary()
    {
        SaveWizardFields();

        var seconds = _config.RecordingPreset switch
        {
            "short" => 15,
            "long" => 45,
            _ => 25
        };

        var storage = _config.StorageMode switch
        {
            "local" => $"Local • {_config.RecordingDirectory} • {_config.RetentionHours} hour retention",
            "both" => $"Local + Cloud • {_config.RecordingDirectory} • FTP {_config.FtpHost}",
            _ => $"Cloud / FTP • {_config.FtpHost}"
        };

        var enabledCameras = _config.Cameras?
            .Where(c => c.Enabled)
            .ToList() ?? new List<CameraSelection>();

        var cameraSummary = enabledCameras.Count switch
        {
            0 => string.IsNullOrWhiteSpace(_config.CameraName)
                ? "No camera selected"
                : _config.CameraName,
            1 => enabledCameras[0].Name,
            <= 3 => string.Join(", ", enabledCameras.Select(c => c.Name)),
            _ => $"{enabledCameras.Count} cameras selected"
        };

        FinishSummary.Text =
            $"Cameras: {cameraSummary}\n" +
            $"Recording: {seconds} seconds\n" +
            $"Storage: {storage}\n\n" +
            "DoorPulse will monitor every selected camera independently and start automatically after Windows restarts.";
    }

    private async void Finish_Click(object sender, RoutedEventArgs e)
    {
        FinishButton.IsEnabled = false;
        BackButton.IsEnabled = false;

        try
        {
            SaveWizardFields();

            _config.AutoStart = StartWithWindowsBox.IsChecked == true;

            var runtime = RuntimeService.Detect(_config.RecorderScriptPath);

            if (!runtime.AllReady)
                throw new InvalidOperationException("System components are not ready.");

            var engineDir = GetEngineDirectory();
            EngineDeployer.DeployAuthHelpers(engineDir);

            var pushStatus =
                await PushStatusService.CheckAsync(
                    runtime.NodePath,
                    engineDir);

            if (!pushStatus.Ready)
            {
                FinishMessage.Text =
                    "Preparing Ring Push Service...";

                var pushBootstrap =
                    await PushBootstrapService.RunAsync(
                        runtime.NodePath,
                        engineDir,
                        25);

                if (!pushBootstrap.Ready)
                {
                    throw new InvalidOperationException(
                        "Ring Push Service is not ready yet. Go back to the Ring step and click Retry.");
                }
            }

            // Deploy the final recorder into the selected engine directory.
            EngineDeployer.Deploy(_config.RecorderScriptPath);

            _config.SetupCompleted = true;
            _config.SetupStep = 6;
            ConfigService.Save(_config);

            FinishMessage.Text = "Installing DoorPulse background recorder...";

            // Ensure the permanent Program Files copy exists.
            // The Setup Wizard itself may still be running from Downloads/Desktop.
            SelfInstallService.EnsureInstalledCopy();
            SelfInstallService.FinalizeInstallation();

            var exePath =
                SelfInstallService.GetBackgroundExecutablePath();

            await TaskService.InstallOrUpdateAsync(exePath);

            if (_config.AutoStart)
                await TaskService.StartAsync();

            FinishMessage.Text = "✓ Setup complete. DoorPulse is starting.";

            await Task.Delay(1200);

            var main = new MainWindow();
            Application.Current.MainWindow = main;
            main.Show();
            Close();
        }
        catch (Exception ex)
        {
            _config.SetupCompleted = false;
            ConfigService.Save(_config);

            FinishMessage.Text = ex.Message;
            FinishButton.IsEnabled = true;
            BackButton.IsEnabled = true;
        }
    }
}

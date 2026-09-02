using System.Windows;
using DoorPulse.Services;

namespace DoorPulse;

public partial class App : Application
{
    protected override async void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

        ConfigService.EnsureFolders();
        SelfInstallService.LogInstall("DoorPulse process started.");

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(
                        ConfigService.LogsPath,
                        "gui-crash.log"),
                    $"[{DateTime.Now:G}] {args.Exception}\r\n\r\n");
            }
            catch { }

            MessageBox.Show(
                args.Exception.Message,
                "DoorPulse",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            args.Handled = true;
        };

        // Independent recovery watchdog.
        if (e.Args.Any(a =>
            string.Equals(
                a,
                "--watchdog",
                StringComparison.OrdinalIgnoreCase)))
        {
            ShutdownMode =
                ShutdownMode.OnExplicitShutdown;

            try
            {
                await TaskService.EnsureRecorderRunningAsync();
                Shutdown(0);
            }
            catch (Exception ex)
            {
                try
                {
                    await File.AppendAllTextAsync(
                        ConfigService.WatchdogLogPath,
                        $"[{DateTime.Now:G}] WATCHDOG ERROR: {ex}\r\n");
                }
                catch { }

                Shutdown(1);
            }

            return;
        }

        // Background recorder agent.
        if (e.Args.Any(a =>
            string.Equals(
                a,
                "--agent",
                StringComparison.OrdinalIgnoreCase)))
        {
            ShutdownMode =
                ShutdownMode.OnExplicitShutdown;

            try
            {
                var host = new RecorderHost();
                await host.RunAsync();

                // RunAsync is intended to run forever. If it returns without an
                // explicit duplicate-agent condition, finish normally.
                Shutdown(0);
            }
            catch (Exception ex)
            {
                try
                {
                    await File.AppendAllTextAsync(
                        ConfigService.AgentLogPath,
                        $"{DateTime.Now:G} AGENT FATAL: {ex}\r\n");
                }
                catch { }

                // Non-zero exit allows Task Scheduler's automatic restart policy
                // to recover the recorder.
                Shutdown(1);
            }

            return;
        }

        ShutdownMode =
            ShutdownMode.OnMainWindowClose;

        // Fresh customer setup:
        // Create the permanent copy, but KEEP THIS PROCESS ALIVE and continue
        // directly into the wizard. No relaunch, no async blocking call.
        if (SelfInstallService.IsSetupExecutable())
        {
            try
            {
                SelfInstallService.LogInstall("Setup executable detected.");
                SelfInstallService.EnsureInstalledCopy();
                SelfInstallService.LogInstall("Permanent copy stage finished.");
            }
            catch (Exception ex)
            {
                SelfInstallService.LogInstall("INSTALL ERROR: " + ex);

                MessageBox.Show(
                    "DoorPulse could not create its permanent installation copy.\n\n" +
                    ex.Message,
                    "DoorPulse Setup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown();
                return;
            }
        }

        try
        {
            SelfInstallService.LogInstall("Loading DoorPulse configuration.");

            var config = ConfigService.Load();

            if (config.SetupCompleted &&
                config.SetupSchemaVersion < 4)
            {
                config.SetupCompleted = false;
                config.SetupStep = 5;
                config.SetupSchemaVersion = 4;
                ConfigService.Save(config);
            }

            if (File.Exists(
                    ConfigService.RingTokenPath) &&
                (config.Cameras is null ||
                 config.Cameras.Count == 0) &&
                (!string.IsNullOrWhiteSpace(
                    config.CameraName) ||
                 !string.IsNullOrWhiteSpace(
                    config.CameraId)))
            {
                config.SetupCompleted = false;
                config.SetupStep = 3;
                config.SetupSchemaVersion = 4;
                ConfigService.Save(config);
            }

            if (!config.SetupCompleted)
            {
                SelfInstallService.LogInstall(
                    $"Opening Setup Wizard at step {config.SetupStep + 1}.");

                var wizard =
                    new SetupWizardWindow();

                MainWindow = wizard;
                wizard.Show();

                SelfInstallService.LogInstall("Setup Wizard shown.");
                return;
            }

            // Existing installations are repaired automatically when the
            // DoorPulse GUI is opened. No customer PowerShell is required.
            try
            {
                var installedExe =
                    SelfInstallService.GetBackgroundExecutablePath();

                await TaskService.EnsureResilienceAsync(
                    installedExe);
            }
            catch (Exception ex)
            {
                SelfInstallService.LogInstall(
                    "Automatic recovery setup warning: " +
                    ex.Message);
            }

            SelfInstallService.LogInstall("Opening Dashboard.");

            var window =
                new MainWindow();

            MainWindow = window;
            window.Show();

            SelfInstallService.LogInstall("Dashboard shown.");
        }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(
                        ConfigService.LogsPath,
                        "startup.log"),
                    $"[{DateTime.Now:G}] STARTUP ERROR: {ex}\r\n\r\n");
            }
            catch { }

            MessageBox.Show(
                ex.ToString(),
                "DoorPulse Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown();
        }
    }
}

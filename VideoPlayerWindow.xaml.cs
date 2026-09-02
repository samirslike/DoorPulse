using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DoorPulse.Models;

namespace DoorPulse;

public partial class VideoPlayerWindow : Window
{
    private readonly LocalVideoItem _video;
    private readonly DispatcherTimer _timer;
    private bool _dragging;
    private bool _playing = true;
    private WindowState _previousState = WindowState.Normal;
    private WindowStyle _previousStyle = WindowStyle.SingleBorderWindow;

    public VideoPlayerWindow(LocalVideoItem video)
    {
        InitializeComponent();

        _video = video;

        TitleText.Text = video.CameraName;
        MetaText.Text = $"{video.EventLabel} • {video.EventTime:dddd, MMMM d, yyyy • h:mm tt} • {video.SizeText}";

        Player.Source = new Uri(video.VideoPath);
        Player.Volume = VolumeSlider.Value;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };

        _timer.Tick += (_, _) =>
        {
            if (_dragging || !Player.NaturalDuration.HasTimeSpan)
                return;

            var duration = Player.NaturalDuration.TimeSpan.TotalSeconds;
            var current = Player.Position.TotalSeconds;

            SeekSlider.Maximum = Math.Max(1, duration);
            SeekSlider.Value = Math.Min(current, SeekSlider.Maximum);

            CurrentTimeText.Text = FormatTime(Player.Position);
            DurationText.Text = FormatTime(Player.NaturalDuration.TimeSpan);
        };

        Loaded += (_, _) =>
        {
            Player.Play();
            _timer.Start();
        };

        Closed += (_, _) =>
        {
            _timer.Stop();
            Player.Stop();
            Player.Source = null;
        };
    }

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss")
            : value.ToString(@"m\:ss");

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (Player.NaturalDuration.HasTimeSpan)
        {
            SeekSlider.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
            DurationText.Text = FormatTime(Player.NaturalDuration.TimeSpan);
        }
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e)
    {
        _playing = false;
        PlayPauseButton.Content = "Play";
        Player.Position = TimeSpan.Zero;
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_playing)
        {
            Player.Pause();
            PlayPauseButton.Content = "Play";
        }
        else
        {
            Player.Play();
            PlayPauseButton.Content = "Pause";
        }

        _playing = !_playing;
    }

    private void SeekSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        _dragging = true;

    private void SeekSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        Player.Position = TimeSpan.FromSeconds(SeekSlider.Value);
        _dragging = false;
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_dragging)
            CurrentTimeText.Text = FormatTime(TimeSpan.FromSeconds(SeekSlider.Value));
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Player is not null)
            Player.Volume = VolumeSlider.Value;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{_video.VideoPath}\"",
            UseShellExecute = true
        });
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) =>
        ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (WindowStyle != WindowStyle.None)
        {
            _previousState = WindowState;
            _previousStyle = WindowStyle;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            Topmost = true;
        }
        else
        {
            Topmost = false;
            WindowStyle = _previousStyle;
            WindowState = _previousState;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            PlayPause_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && WindowStyle == WindowStyle.None)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
    }
}
